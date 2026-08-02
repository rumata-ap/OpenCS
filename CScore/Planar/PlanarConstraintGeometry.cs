namespace CScore.Planar;

/// <summary>Тип локальной геометрии внутреннего constraint-объекта.</summary>
public enum PlanarConstraintGeometryKind
{
    Point,
    Curve,
    Region
}

/// <summary>Точка во внутренних координатах PlanarRegion.</summary>
public sealed record PlanarPoint2D(double U, double V)
{
    public bool IsFinite => double.IsFinite(U) && double.IsFinite(V);
}

/// <summary>Локальная линейная геометрия constraint-объекта. Для Point хранится одна точка,
/// для Curve — открытая polyline, для Region — замкнутый polygon без дублирования первой точки.</summary>
public sealed record PlanarConstraintGeometry(
    PlanarConstraintGeometryKind Kind,
    IReadOnlyList<PlanarPoint2D> Points);
