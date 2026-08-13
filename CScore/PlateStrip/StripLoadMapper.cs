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
    public static StripLoadMappingResult Map(
        Frame3D regionFrame,
        PlateStripBeamAnalogy analogy,
        PlanarLoad load,
        double torqueToleranceKnM = 1e-6)
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
                diagnostics.Add(new("plate_strip_load_kind_unsupported",
                    $"Краевая нагрузка «{load.Tag}» не поддерживается в этом срезе (StripBoundaryInterface — Срез 7)."));
                return new(false, diagnostics, null);

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
