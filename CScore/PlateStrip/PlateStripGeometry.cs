using CScore.Planar;

namespace CScore.PlateStrip;

/// <summary>Геометрия полосы плиты в локальной плоскости (u,v) региона-источника (см.
/// CScore.Planar.PlanarRegion.Frame). Polygon — уже клиппированный по Hull результат;
/// LeftBoundary/RightBoundary — полные (неклиппированные) продольные рёбра прямоугольника
/// полосы шириной ExplicitWidthM, CenterLine — ось полосы (2 точки, прямая для v1).</summary>
public sealed class PlateStripGeometry
{
    public IReadOnlyList<PlanarPoint2D> CenterLine { get; set; } = [];
    public IReadOnlyList<PlanarPoint2D> LeftBoundary { get; set; } = [];
    public IReadOnlyList<PlanarPoint2D> RightBoundary { get; set; } = [];
    public IReadOnlyList<PlanarPoint2D> Polygon { get; set; } = [];
    public double LengthM { get; set; }
}
