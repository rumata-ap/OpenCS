using CScore.Fem;

namespace CScore.Planar;

/// <summary>Создаёт PlanarRegion и валидирует его как одну операцию, без исключений наружу —
/// UI-слой (PlanarRegionMemberVM) работает только со списком диагностик.</summary>
public static class PlanarRegionCreation
{
    public static (PlanarRegion? Region, IReadOnlyList<FemValidationDiagnostic> Diagnostics) TryCreate(
        Contour hull, IEnumerable<Contour> holes, Frame3D frame, string tag)
    {
        PlanarRegion region;
        try
        {
            region = PlanarRegion.CreateFromContour(hull, holes, frame, tag);
        }
        catch (InvalidOperationException ex)
        {
            return (null, [new FemValidationDiagnostic("planar_region_geometry_invalid", ex.Message)]);
        }

        var diagnostics = PlanarRegionValidator.Validate(region);
        if (diagnostics.Any(d => d.IsError))
            return (null, diagnostics);
        return (region, diagnostics);
    }
}
