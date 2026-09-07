using CScore.Fem;
using CScore.Planar;

namespace CScore.PlateStrip;

public sealed record StripLoadMappingResult(
    bool IsCalculable,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics,
    StripLoad? Load);

/// <summary>Проецирует PlanarLoad (Surface, Point) и явный источник собственного веса на
/// PlateStripBeamAnalogy. См. docs/superpowers/specs/2026-08-13-plate-strip-loads-design.md.</summary>
public static class StripLoadMapper
{
    /// <param name="interfaces">Границы заменяемой области полосы. Нужны только для
    /// PlanarLoadKind.Boundary (Срез 7); вызовы Срезов 4–5 их не передают и не ломаются.</param>
    public static StripLoadMappingResult Map(
        Frame3D regionFrame,
        PlateStripBeamAnalogy analogy,
        PlanarLoad load,
        double torqueToleranceKnM = 1e-6,
        IReadOnlyList<StripBoundaryInterface>? interfaces = null)
    {
        ArgumentNullException.ThrowIfNull(analogy);
        ArgumentNullException.ThrowIfNull(load);

        var diagnostics = new List<FemValidationDiagnostic>();

        if (!TryValidateRegionFrame(regionFrame, diagnostics) ||
            !TryValidateStripGeometry(analogy, diagnostics))
            return new(false, diagnostics, null);

        try
        {
            load.Validate();
        }
        catch (ArgumentException ex)
        {
            diagnostics.Add(new("plate_strip_load_invalid_input", ex.Message));
            return new(false, diagnostics, null);
        }

        switch (load.Kind)
        {
            case PlanarLoadKind.Boundary:
                return MapBoundary(regionFrame, analogy, load, interfaces ?? [], diagnostics);

            case PlanarLoadKind.Surface:
                return MapSurface(regionFrame, analogy, load, diagnostics);

            case PlanarLoadKind.Point:
                return MapPoint(regionFrame, analogy, load, torqueToleranceKnM, diagnostics);

            default:
                diagnostics.Add(new("plate_strip_load_kind_unsupported",
                    $"Нагрузка «{load.Tag}» имеет неизвестный тип."));
                return new(false, diagnostics, null);
        }
    }

    public static StripLoadMappingResult MapSelfWeight(
        PlateStripBeamAnalogy analogy,
        double plateThicknessM,
        double unitWeightKnM3,
        string sourceTag = "self_weight")
    {
        ArgumentNullException.ThrowIfNull(analogy);
        var diagnostics = new List<FemValidationDiagnostic>();

        if (!TryValidateStripGeometry(analogy, diagnostics))
            return new(false, diagnostics, null);

        if (!double.IsFinite(unitWeightKnM3) || unitWeightKnM3 < 0.0)
        {
            diagnostics.Add(new("plate_strip_load_negative_unit_weight",
                $"Удельный вес «{sourceTag}» должен быть конечным и неотрицательным."));
            return new(false, diagnostics, null);
        }
        if (!double.IsFinite(plateThicknessM) || plateThicknessM <= 0.0)
        {
            diagnostics.Add(new("plate_strip_load_invalid_input",
                $"Толщина плиты для «{sourceTag}» должна быть конечной и положительной."));
            return new(false, diagnostics, null);
        }

        double qzGlobal = -unitWeightKnM3 * plateThicknessM * analogy.ExplicitWidthM;
        var local = PlanarBoundaryFrameConverter.ToLocalVector(
            analogy.StripFrame, new PlanarVector3(0.0, 0.0, qzGlobal));

        var result = new StripLoad
        {
            Kind = StripLoadKind.DistributedUniform,
            SourceTag = sourceTag,
            StationStartFraction = 0.0,
            StationEndFraction = 1.0,
            QxKnM = local.X,
            QyKnM = local.Y,
            QzKnM = local.Z
        };
        return new(true, diagnostics, result);
    }

    static StripLoadMappingResult MapSurface(
        Frame3D regionFrame, PlateStripBeamAnalogy analogy, PlanarLoad load,
        List<FemValidationDiagnostic> diagnostics)
    {
        var vector = ToStripLocalVector(regionFrame, analogy.StripFrame, load.Components, load.CoordinateSystem);
        var result = new StripLoad
        {
            Kind = StripLoadKind.DistributedUniform,
            SourceTag = load.Tag,
            StationStartFraction = 0.0,
            StationEndFraction = 1.0,
            QxKnM = vector.X * analogy.ExplicitWidthM,
            QyKnM = vector.Y * analogy.ExplicitWidthM,
            QzKnM = vector.Z * analogy.ExplicitWidthM
        };
        return new(true, diagnostics, result);
    }

    static StripLoadMappingResult MapPoint(
        Frame3D regionFrame, PlateStripBeamAnalogy analogy, PlanarLoad load,
        double torqueToleranceKnM, List<FemValidationDiagnostic> diagnostics)
    {
        var regionPoint = new PlanarVector3(load.PointU, load.PointV, 0.0);
        var globalPoint = PlanarBoundaryFrameConverter.ToGlobalPoint(regionFrame, regionPoint);
        var stripPoint = PlanarBoundaryFrameConverter.ToLocalPoint(analogy.StripFrame, globalPoint);

        double lengthM = analogy.Geometry.LengthM;
        double stationFraction = stripPoint.X / lengthM;
        double v = stripPoint.Y;
        double stationToleranceFraction = load.PointToleranceM / lengthM;
        double halfWidth = analogy.ExplicitWidthM / 2.0;

        if (stationFraction < -stationToleranceFraction || stationFraction > 1.0 + stationToleranceFraction ||
            Math.Abs(v) > halfWidth + load.PointToleranceM)
        {
            diagnostics.Add(new("plate_strip_load_outside_strip",
                $"Точка нагрузки «{load.Tag}» вне коридора полосы «{analogy.Id}»."));
            return new(false, diagnostics, null);
        }
        stationFraction = Math.Clamp(stationFraction, 0.0, 1.0);

        var force = ToStripLocalVector(regionFrame, analogy.StripFrame, load.Components, load.CoordinateSystem);
        double px = force.X, py = force.Y, pz = force.Z;
        double mx = v * pz;
        double mz = -v * px;

        if (Math.Abs(mx) > torqueToleranceKnM)
        {
            diagnostics.Add(new("plate_strip_load_produces_torque",
                $"Точечная нагрузка «{load.Tag}» с эксцентриситетом v={v:G6} даёт крутящий момент " +
                $"Mx={mx:G6} кН·м — не редуцируется текущей стержневой моделью (TorsionalStiffness=0)."));
            return new(false, diagnostics, null);
        }

        var result = new StripLoad
        {
            Kind = StripLoadKind.Point,
            SourceTag = load.Tag,
            StationFraction = stationFraction,
            PxKn = px,
            PyKn = py,
            PzKn = pz,
            MxKnM = mx,
            MzKnM = mz
        };
        return new(true, diagnostics, result);
    }

    /// <summary>Перенос краевого действия (Срез 7). Интенсивность берётся из
    /// StripBoundaryInterface.ForceAction, если он задан: только он умеет описывать переменную
    /// интенсивность вдоль границы. Иначе используется константный PlanarLoad.Components.
    ///
    /// Единицы: PlanarBoundaryForceAction задан в СИ (Н, м — PlanarBoundaryUnitSystem.Si), а
    /// StripLoad — в кН, поэтому выполняется явная конверсия с тем же множителем и по тому же
    /// основанию, что в ShellMeshPatchPlateSectionResponse.</summary>
    static StripLoadMappingResult MapBoundary(
        Frame3D regionFrame, PlateStripBeamAnalogy analogy, PlanarLoad load,
        IReadOnlyList<StripBoundaryInterface> interfaces, List<FemValidationDiagnostic> diagnostics)
    {
        var boundary = interfaces.FirstOrDefault(
            i => i.StripId == analogy.Id && Equals(i.BoundaryKey, load.BoundaryKey));
        if (boundary == null)
        {
            diagnostics.Add(new("plate_strip_boundary_interface_missing",
                $"Краевая нагрузка «{load.Tag}» не имеет StripBoundaryInterface на полосе " +
                $"«{analogy.Id}»: перенести действие молча нельзя."));
            return new(false, diagnostics, null);
        }

        var modes = boundary.ModeByDof ?? PlanarBoundaryModeByDof.None;
        PlanarBoundaryDofMode[] values = [modes.UX, modes.UY, modes.UZ, modes.RX, modes.RY, modes.RZ];
        if (values.Any(m => m == PlanarBoundaryDofMode.Incomplete))
        {
            diagnostics.Add(new("plate_strip_boundary_mode_incomplete",
                $"Граница «{boundary.Id}» нагрузки «{load.Tag}»: режим Incomplete недопустим."));
            return new(false, diagnostics, null);
        }

        if (!values.Any(m => m == PlanarBoundaryDofMode.Force))
        {
            if (values.Any(m => m == PlanarBoundaryDofMode.Kinematic))
                diagnostics.Add(new("plate_strip_boundary_kinematic_not_transferred",
                    $"Граница «{boundary.Id}»: кинематические DOF не переносятся на полосу — " +
                    "предписанные перемещения балочной задачи вне объёма среза.", false));
            // PreserveSupport/None/Free: действие остаётся у сохранённой части, это не ошибка.
            return new(true, diagnostics, null);
        }

        var geometry = ProjectGeometryToStrip(regionFrame, analogy, boundary);
        if (geometry == null)
        {
            diagnostics.Add(new("plate_strip_boundary_geometry_invalid",
                $"Граница «{boundary.Id}» нагрузки «{load.Tag}» не проецируется на полосу."));
            return new(false, diagnostics, null);
        }

        var (startIntensity, endIntensity) = ResolveIntensity(regionFrame, analogy, boundary, load);
        double lengthM = analogy.Geometry.LengthM;

        if (boundary.TryProjectToStation(regionFrame, analogy, out double stationFraction))
        {
            // Граница пересекает ось поперёк: действие сводится к сосредоточенному в станции.
            // Равнодействующая — средняя интенсивность на длине части границы внутри коридора.
            double span = CorridorSpan(geometry, analogy.ExplicitWidthM);
            var px = 0.5 * (startIntensity.X + endIntensity.X) * span;
            var py = 0.5 * (startIntensity.Y + endIntensity.Y) * span;
            var pz = 0.5 * (startIntensity.Z + endIntensity.Z) * span;

            var point = new StripLoad
            {
                Kind = StripLoadKind.Point,
                SourceTag = load.Tag,
                StationFraction = stationFraction,
                PxKn = px,
                PyKn = py,
                PzKn = pz
            };
            return new(true, diagnostics, point);
        }

        // Граница идёт вдоль полосы: интенсивность на единицу длины границы совпадает с
        // интенсивностью на единицу длины полосы.
        double s1 = Math.Clamp(geometry[0].X / lengthM, 0.0, 1.0);
        double s2 = Math.Clamp(geometry[^1].X / lengthM, 0.0, 1.0);
        double a = Math.Min(s1, s2), b = Math.Max(s1, s2);
        if (b - a < 1e-12)
        {
            diagnostics.Add(new("plate_strip_load_outside_strip",
                $"Краевая нагрузка «{load.Tag}» не покрывает ни одного участка полосы " +
                $"«{analogy.Id}»."));
            return new(false, diagnostics, null);
        }
        if (s2 < s1) (startIntensity, endIntensity) = (endIntensity, startIntensity);

        bool uniform =
            Math.Abs(startIntensity.X - endIntensity.X) < 1e-12 &&
            Math.Abs(startIntensity.Y - endIntensity.Y) < 1e-12 &&
            Math.Abs(startIntensity.Z - endIntensity.Z) < 1e-12;

        var distributed = new StripLoad
        {
            Kind = uniform ? StripLoadKind.DistributedUniform : StripLoadKind.DistributedLinear,
            SourceTag = load.Tag,
            StationStartFraction = a,
            StationEndFraction = b,
            QxKnM = startIntensity.X,
            QyKnM = startIntensity.Y,
            QzKnM = startIntensity.Z,
            QxEndKnM = uniform ? 0.0 : endIntensity.X,
            QyEndKnM = uniform ? 0.0 : endIntensity.Y,
            QzEndKnM = uniform ? 0.0 : endIntensity.Z
        };
        return new(true, diagnostics, distributed);
    }

    /// <summary>Точки границы в координатах полосы; null, если геометрия непригодна.</summary>
    static List<PlanarVector3>? ProjectGeometryToStrip(
        Frame3D regionFrame, PlateStripBeamAnalogy analogy, StripBoundaryInterface boundary)
    {
        var points = boundary.Geometry?.Points;
        if (points == null || points.Count < 2) return null;

        var local = new List<PlanarVector3>(points.Count);
        foreach (var point in points)
        {
            if (!point.IsFinite) return null;
            var global = PlanarBoundaryFrameConverter.ToGlobalPoint(
                regionFrame, new PlanarVector3(point.U, point.V, 0.0));
            local.Add(PlanarBoundaryFrameConverter.ToLocalPoint(analogy.StripFrame, global));
        }
        return local;
    }

    /// <summary>Длина части поперечной границы, попавшей в коридор полосы.</summary>
    static double CorridorSpan(List<PlanarVector3> geometry, double widthM)
    {
        double half = widthM / 2.0;
        double lo = geometry.Min(p => p.Y), hi = geometry.Max(p => p.Y);
        double clippedLo = Math.Max(lo, -half), clippedHi = Math.Min(hi, half);
        return Math.Max(0.0, clippedHi - clippedLo);
    }

    /// <summary>Интенсивности в начале и конце границы, приведённые к осям полосы и к кН.</summary>
    static (PlanarVector3 Start, PlanarVector3 End) ResolveIntensity(
        Frame3D regionFrame, PlateStripBeamAnalogy analogy,
        StripBoundaryInterface boundary, PlanarLoad load)
    {
        var action = boundary.ForceAction;
        if (action?.Samples is { Count: > 0 } samples)
        {
            double scale = action.UnitSystem == PlanarBoundaryUnitSystem.Si ? 1.0 / 1000.0 : 1.0;
            var first = samples[0].ForcePerLength;
            var last = samples[^1].ForcePerLength;
            return (
                ScaleToStrip(regionFrame, analogy, first, scale),
                ScaleToStrip(regionFrame, analogy, last, scale));
        }

        var constant = ToStripLocalVector(
            regionFrame, analogy.StripFrame, load.Components, load.CoordinateSystem);
        return (constant, constant);
    }

    static PlanarVector3 ScaleToStrip(
        Frame3D regionFrame, PlateStripBeamAnalogy analogy, PlanarVector3 vector, double scale)
    {
        var scaled = new PlanarVector3(vector.X * scale, vector.Y * scale, vector.Z * scale);
        var global = PlanarBoundaryFrameConverter.ToGlobalVector(regionFrame, scaled);
        return PlanarBoundaryFrameConverter.ToLocalVector(analogy.StripFrame, global);
    }

    static bool TryValidateRegionFrame(Frame3D regionFrame, List<FemValidationDiagnostic> diagnostics)
    {
        try
        {
            regionFrame.Validate();
            return true;
        }
        catch (InvalidOperationException ex)
        {
            diagnostics.Add(new("plate_strip_load_invalid_geometry", ex.Message));
            return false;
        }
    }

    static bool TryValidateStripGeometry(PlateStripBeamAnalogy analogy, List<FemValidationDiagnostic> diagnostics)
    {
        try
        {
            analogy.StripFrame.Validate();
        }
        catch (InvalidOperationException ex)
        {
            diagnostics.Add(new("plate_strip_load_invalid_geometry", ex.Message));
            return false;
        }

        if (!double.IsFinite(analogy.Geometry.LengthM) || analogy.Geometry.LengthM <= 0.0 ||
            !double.IsFinite(analogy.ExplicitWidthM) || analogy.ExplicitWidthM <= 0.0)
        {
            diagnostics.Add(new("plate_strip_load_invalid_geometry",
                $"Полоса «{analogy.Id}» имеет непостроенную или вырожденную геометрию (LengthM/ExplicitWidthM)."));
            return false;
        }
        return true;
    }

    static PlanarVector3 ToStripLocalVector(
        Frame3D regionFrame, Frame3D stripFrame, PlanarVector3 vector, PlanarLoadCoordinateSystem system)
    {
        var global = system == PlanarLoadCoordinateSystem.Global
            ? vector
            : PlanarBoundaryFrameConverter.ToGlobalVector(regionFrame, vector);
        return PlanarBoundaryFrameConverter.ToLocalVector(stripFrame, global);
    }
}
