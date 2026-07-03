using CScore;

namespace CScore.Fire;

/// <summary>
/// Построение сечения с пониженными прочностями для MVP R-проверки.
/// </summary>
public static class FireMaterialReducer
{
    /// <summary>
    /// Создаёт копию сечения с масштабированными прочностями материалов.
    /// Геометрия и сетка фибр не изменяются.
    /// </summary>
    public static CrossSection CreateReduced(
        CrossSection source,
        double gammaBt,
        double gammaStMin)
    {
        ArgumentNullException.ThrowIfNull(source);
        var reduced = new CrossSection
        {
            Id = source.Id,
            Tag = source.Tag,
            Description = source.Description
        };

        foreach (var area in source.Areas)
        {
            var copy = new MaterialArea
            {
                Id = area.Id,
                Tag = area.Tag,
                Category = area.Category,
                Contours = area.Contours,
                Hull = area.Hull,
                WKT = area.WKT,
                H = area.H,
                NX = area.NX,
                NY = area.NY,
                DiagrammType = area.DiagrammType,
                Fibers = area.Fibers
            };

            if (area.Material is null)
            {
                reduced.Areas.Add(copy);
                continue;
            }

            copy.Material = area.Material.Type switch
            {
                MatType.Concrete => ScaleConcreteMaterial(area.Material, gammaBt),
                MatType.ReSteelF or MatType.ReSteelU or MatType.Steel => ScaleRebarMaterial(area.Material, gammaStMin),
                _ => area.Material
            };
            copy.MaterialId = copy.Material.Id;
            copy.ResolveAndBuildDiagramms();
            reduced.Areas.Add(copy);
        }

        return reduced;
    }

    static Material ScaleConcreteMaterial(Material src, double gamma)
    {
        var m = new Material
        {
            Id = src.Id,
            Tag = src.Tag,
            Type = src.Type,
            E = src.E,
            AggregateType = src.AggregateType
        };
        m.MaterialChars = src.MaterialChars.Select(ch =>
        {
            var c = ch.Clone();
            c.Fc *= gamma;
            c.Ft *= gamma;
            if (c.Ru != 0) c.Ru *= gamma;
            return c;
        }).ToList();
        return m;
    }

    static Material ScaleRebarMaterial(Material src, double gamma)
    {
        var m = new Material
        {
            Id = src.Id,
            Tag = src.Tag,
            Type = src.Type,
            E = src.E
        };
        m.MaterialChars = src.MaterialChars.Select(ch =>
        {
            var c = ch.Clone();
            c.Ry *= gamma;
            c.Ru *= gamma;
            if (c.Fc != 0) c.Fc *= gamma;
            if (c.Ft != 0) c.Ft *= gamma;
            return c;
        }).ToList();
        return m;
    }
}
