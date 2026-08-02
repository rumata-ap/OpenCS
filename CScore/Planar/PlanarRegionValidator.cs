using CScore.Fem;

namespace CScore.Planar;

public static class PlanarRegionValidator
{
    public static IReadOnlyList<FemValidationDiagnostic> Validate(PlanarRegion region)
    {
        var diagnostics = new List<FemValidationDiagnostic>();

        try
        {
            PlanarRegionTopologyValidator.ValidateLoop(region.Hull.X, region.Hull.Y, "внешний контур");
        }
        catch (InvalidOperationException ex)
        {
            diagnostics.Add(new FemValidationDiagnostic("planar_region_outer_contour_invalid", ex.Message));
        }

        int holeIndex = 0;
        foreach (var hole in region.Holes)
        {
            try
            {
                PlanarRegionTopologyValidator.ValidateLoop(hole.X, hole.Y, $"отверстие #{holeIndex}");
            }
            catch (InvalidOperationException ex)
            {
                diagnostics.Add(new FemValidationDiagnostic("planar_region_hole_invalid", ex.Message));
            }
            holeIndex++;
        }

        try
        {
            region.Frame.Validate();
        }
        catch (InvalidOperationException ex)
        {
            diagnostics.Add(new FemValidationDiagnostic("planar_region_frame_invalid", ex.Message));
        }

        if (region.FrameIsRecovered)
            diagnostics.Add(new FemValidationDiagnostic(
                "planar_region_frame_recovered",
                "Локальная система координат не задана явно; принята глобальная 2D-рамка по умолчанию.",
                IsError: false));

        if (region.BoundarySegments.Count == 0)
            diagnostics.Add(new FemValidationDiagnostic(
                "planar_region_boundary_unclassified",
                "Рёбра контура не размечены (роль unclassified).",
                IsError: false));

        try
        {
            diagnostics.AddRange(PlanarConstraintValidator.Validate(region, region.ConstraintObjects));
        }
        catch (InvalidOperationException ex)
        {
            diagnostics.Add(new FemValidationDiagnostic("planar_constraint_host_invalid", ex.Message));
        }

        return diagnostics;
    }
}
