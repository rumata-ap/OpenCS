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
                    $"Краевая нагрузка «{load.Tag}» не поддерживается в этом срезе (StripBoundaryInterface — Срез 5)."));
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
        => throw new NotImplementedException();

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
