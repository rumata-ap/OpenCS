using CScore.Planar;

namespace CScore.Fem.Fragment;

/// <summary>
/// Часть КЭ родителя внутри границ фрагмента: доля длины (стержень) или площади (пластина), центр части;
/// <see cref="Whole"/> — КЭ целиком внутри (нагрузки берутся в своих узлах, а не долей в центре).
/// </summary>
public readonly record struct FragmentElementPart(double Share, PlanarVector3 Centroid, bool Whole);

/// <summary>
/// Доли КЭ внутри <see cref="FragmentBounds"/> (спека 2026-11-05, разд. 6): общие для нагрузок полосы родителя и поправки
/// масок на полоску между ступенькой рёбер КЭ и гранью. Равномерная нагрузка КЭ — пропорционально доле в центре части.
/// </summary>
public static class FragmentElementShare
{
    /// <summary>
    /// Часть КЭ с точками <paramref name="points"/> (концы стержня или контур пластины) внутри границ; null — КЭ вне
    /// фрагмента, в том числе целиком по внешнюю сторону какой-либо плоскости или на ней.
    /// </summary>
    public static FragmentElementPart? Of(FragmentBounds bounds, IReadOnlyList<PlanarVector3> points)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(points);
        double tol = bounds.Tol;
        if (points.Count == 0 || bounds.Planes.Any(pl => points.All(p => pl.Eval(p) >= -tol))) return null;
        if (points.All(bounds.InBox))
            return new(1, points.Aggregate(PlanarVector3.Zero, (s, p) => s + p) * (1.0 / points.Count), true);
        if (points.Count == 2)
        {
            var d = points[1] - points[0];
            var (lo, hi) = bounds.ClipRange(points[0], d, 0, 1);
            if (hi <= lo) return null;
            return new(hi - lo, points[0] + d * ((lo + hi) / 2), false);
        }
        var full = FragmentBounds.AreaCentroid(points.ToList()).Area;
        var part = bounds.ClipPolygon(points.ToList());
        if (part.Count < 3 || full <= 0) return null;
        var (area, c) = FragmentBounds.AreaCentroid(part);
        return new(area / full, c, false);
    }
}
