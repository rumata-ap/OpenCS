namespace CScore.PlateRebar;

/// <summary>Одна уникальная (по составу слоёв) комбинация армирования, обнаруженная пробной
/// сеткой точек внутри региона — ключ дедупликации, как и на уровне резолвера/маппера.</summary>
public sealed record RebarSectionCombination(IReadOnlyList<PlateRebarLayer> Layers, string Fingerprint);

/// <summary>Приближение будущей триангуляции: вместо резолва по центрам тяжести реальных КЭ —
/// резолв по прямоугольной сетке пробных точек внутри Hull (за вычетом Holes) с дедупликацией
/// результатов по fingerprint. Не знает о PlanarRegion/PlateSection/БД.</summary>
public static class PlateRebarFieldGridResolver
{
    public static IReadOnlyList<RebarSectionCombination> Resolve(
        PlateRebarField field, Contour hull, IEnumerable<Contour> holes, double step)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(hull);
        if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step));

        var seen = new Dictionary<string, RebarSectionCombination>();

        string backgroundFingerprint = PlateRebarLayoutFingerprint.Compute(field.BaseLayout);
        seen[backgroundFingerprint] = new RebarSectionCombination(field.BaseLayout, backgroundFingerprint);

        var holeList = holes.Select(BuildPolygon).ToList();
        var hullPoly = BuildPolygon(hull);

        double xMin = hull.X.Min(), xMax = hull.X.Max();
        double yMin = hull.Y.Min(), yMax = hull.Y.Max();

        for (double x = xMin; x <= xMax; x += step)
        {
            for (double y = yMin; y <= yMax; y += step)
            {
                if (!CSTriangulation.GeometryUtils.PointInPolygon(x, y, hullPoly)) continue;
                if (holeList.Any(h => CSTriangulation.GeometryUtils.PointInPolygon(x, y, h))) continue;

                var layout = PlateRebarFieldResolver.Resolve(field, x, y);
                string fp = PlateRebarLayoutFingerprint.Compute(layout.Layers);
                if (!seen.ContainsKey(fp))
                    seen[fp] = new RebarSectionCombination(layout.Layers, fp);
            }
        }

        return [.. seen.Values];
    }

    static List<(double X, double Y)> BuildPolygon(Contour c) =>
        [.. c.X.Zip(c.Y, (x, y) => (x, y))];
}
