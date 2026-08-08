using CScore.Planar;

namespace CScore.PlateStrip;

/// <summary>Региональная (u,v) точка полосы на произвольной станции по длине и произвольном
/// поперечном смещении v — чистая геометрия, без БД/материалов. Используется резолвером
/// пространственного армирования (EquivalentSectionProjectService).</summary>
public static class PlateStripWidthSampler
{
    /// <summary>
    /// Точка в локальной плоскости (u,v) региона-источника на станции [0,1] вдоль пролёта
    /// полосы и поперечном смещении v от средней линии (v>0 — сторона LeftBoundary,
    /// v&lt;0 — сторона RightBoundary, вдоль StripFrame.LocalY).
    /// </summary>
    public static PlanarPoint2D Point(PlateStripBeamAnalogy analogy, double stationFraction, double v)
    {
        ArgumentNullException.ThrowIfNull(analogy);
        if (!double.IsFinite(stationFraction) || stationFraction < 0.0 || stationFraction > 1.0)
            throw new ArgumentOutOfRangeException(nameof(stationFraction),
                "Станция вдоль пролёта должна быть конечным числом в диапазоне [0,1].");

        double halfWidth = analogy.ExplicitWidthM / 2.0;
        if (!double.IsFinite(v) || v < -halfWidth || v > halfWidth)
            throw new ArgumentOutOfRangeException(nameof(v),
                "Поперечная координата должна быть конечной и в диапазоне [-ширина/2, +ширина/2].");

        var left = analogy.Geometry.LeftBoundary;
        var right = analogy.Geometry.RightBoundary;
        if (left.Count < 2 || right.Count < 2)
            throw new ArgumentException(
                "Геометрия полосы повреждена: LeftBoundary/RightBoundary должны содержать минимум 2 точки.",
                nameof(analogy));
        var leftAtStation = Lerp(left[0], left[1], stationFraction);
        var rightAtStation = Lerp(right[0], right[1], stationFraction);

        double t = (v + halfWidth) / analogy.ExplicitWidthM;
        return Lerp(rightAtStation, leftAtStation, t);
    }

    static PlanarPoint2D Lerp(PlanarPoint2D a, PlanarPoint2D b, double t) =>
        new(a.U + (b.U - a.U) * t, a.V + (b.V - a.V) * t);
}
