namespace CScore.LimitSurface;

/// <summary>Общие для критериев поверхностей выборки геометрии сечения: бетонные области и вершины их контуров.</summary>
internal static class StrainCriterionGeometry
{
    /// <summary>Область из бетона (в том числе пользовательский материал на основе бетона).</summary>
    public static bool IsConcrete(MaterialArea area) =>
        area.Material?.Type == MatType.Concrete ||
        (area.Material?.Type == MatType.Custom && area.Material.BaseType == MatType.Concrete);

    /// <summary>Внешний контур и отверстия области.</summary>
    public static IEnumerable<Contour> Contours(MaterialArea area)
    {
        if (area.Hull is not null) yield return area.Hull;
        foreach (var h in area.Holes) yield return h;
    }

    /// <summary>Вершины контура без замыкающего повтора и без подряд идущих дублей.</summary>
    public static IEnumerable<(double X, double Y)> OpenVertices(Contour c)
    {
        int n = Math.Min(c.X.Count, c.Y.Count);
        if (n == 0) yield break;
        double x0 = c.X[0], y0 = c.Y[0];
        double px = double.NaN, py = double.NaN;
        for (int i = 0; i < n; i++)
        {
            double x = c.X[i], y = c.Y[i];
            if (x == px && y == py) continue;
            if (i == n - 1 && x == x0 && y == y0) break;
            yield return (x, y);
            px = x; py = y;
        }
    }
}
