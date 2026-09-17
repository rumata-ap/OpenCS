using CScore.Sp63;

namespace CScore.Sp63.Normal;

/// <summary>Метрики близости многоугольного контура к окружности.</summary>
/// <param name="CenterX">X центроида, м.</param>
/// <param name="CenterY">Y центроида, м.</param>
/// <param name="Area">Площадь многоугольника, м².</param>
/// <param name="MeanVertexRadius">ρ̄ — среднее расстояние вершин до центроида, м.</param>
/// <param name="RadialDeviation">max|ρᵢ − ρ̄|/ρ̄.</param>
/// <param name="AreaDeviation">|A − πρ̄²|/(πρ̄²).</param>
internal readonly record struct Sp63ContourCircleMeasure(double CenterX, double CenterY,
    double Area, double MeanVertexRadius, double RadialDeviation, double AreaDeviation)
{
    /// <summary>Контур признан окружностью по обоим критериям.</summary>
    public bool IsCircular =>
        Area > 0.0 &&
        RadialDeviation <= Sp63CircularGeometryPolicy.RadialTolerance &&
        AreaDeviation <= Sp63CircularGeometryPolicy.AreaTolerance;
}

/// <summary>
/// Распознавание сплошного круга и кольца из многоугольных контуров для приложения Д.
/// Это ограничение автоматизированного режима OpenCS, а не утверждение о границах СП.
/// </summary>
public static class Sp63CircularGeometryPolicy
{
    /// <summary>Допуск радиального отклонения вершин (ловит эллипсы и перекосы).</summary>
    public const double RadialTolerance = 0.01;
    /// <summary>Допуск отклонения площади многоугольника от круга (отсекает грубую аппроксимацию).</summary>
    public const double AreaTolerance = 0.01;
    /// <summary>Допуск смещения центра отверстия в долях r₂.</summary>
    public const double ConcentricityTolerance = 0.01;
    /// <summary>Минимальное отношение r₁/r₂ по п. Д.1.</summary>
    public const double MinRadiusRatio = 0.5;

    /// <summary>Проверяет геометрию для выбранной формы Circular или Annular.</summary>
    public static Sp63CircularGeometryClassification Classify(CrossSection section,
        Sp63NormalShapeKind shapeKind)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (shapeKind is not (Sp63NormalShapeKind.Circular or Sp63NormalShapeKind.Annular))
            throw new ArgumentOutOfRangeException(nameof(shapeKind));
        bool annular = shapeKind == Sp63NormalShapeKind.Annular;
        string reference = annular ? "Д.1" : "Д.2";

        var concrete = section.Areas
            .Where(area => area.Category == AreaCategory.Region &&
                           area.Material?.Type == MatType.Concrete)
            .ToList();
        if (concrete.Count != 1)
            return Fail("unsupported_geometry", reference, "Sp63Normal_CircularSingleConcreteRegion");

        var region = concrete[0];
        if (region.Hull is null)
            return Fail("unsupported_geometry", reference, "Sp63Normal_NotCircularContour");
        var holes = region.Holes;
        if (!annular && holes.Count != 0)
            return Fail("unsupported_geometry", reference, "Sp63Normal_CircularHasHole");
        if (annular && holes.Count != 1)
            return Fail("unsupported_geometry", reference, "Sp63Normal_AnnularHoleCount");

        var outer = Measure(region.Hull);
        if (!outer.IsCircular)
            return Fail("unsupported_geometry", reference, "Sp63Normal_NotCircularContour");

        var inner = default(Sp63ContourCircleMeasure);
        if (annular)
        {
            inner = Measure(holes[0]);
            if (!inner.IsCircular)
                return Fail("unsupported_geometry", reference, "Sp63Normal_NotCircularContour");
        }

        double r2 = Math.Sqrt(outer.Area / Math.PI);
        double r1 = annular ? Math.Sqrt(inner.Area / Math.PI) : 0.0;
        double centerOffset = annular
            ? Math.Sqrt(Math.Pow(inner.CenterX - outer.CenterX, 2) +
                        Math.Pow(inner.CenterY - outer.CenterY, 2)) / r2
            : 0.0;
        if (annular && centerOffset > ConcentricityTolerance)
            return Fail("unsupported_geometry", reference, "Sp63Normal_AnnularNotConcentric");
        if (annular && r1 / r2 < MinRadiusRatio)
            return Fail("annular_radius_ratio", "Д, примечание 3", "Sp63Normal_AnnularRadiusRatio");

        var geometry = new Sp63CircularGeometry(
            outer.CenterX, outer.CenterY, r2, r1,
            outer.Area - (annular ? inner.Area : 0.0),
            outer.MeanVertexRadius, outer.RadialDeviation, outer.AreaDeviation,
            annular ? inner.MeanVertexRadius : 0.0,
            annular ? inner.RadialDeviation : 0.0,
            annular ? inner.AreaDeviation : 0.0,
            centerOffset);
        return new Sp63CircularGeometryClassification(geometry, []);
    }

    /// <summary>Вычисляет метрики близости контура к окружности.</summary>
    internal static Sp63ContourCircleMeasure Measure(Contour contour)
    {
        var vertices = Sp63RectangularGeometryPolicy.DistinctContourVertices(contour);
        if (vertices.Count < 3)
            return Degenerate();

        var xs = vertices.Select(v => v.X).Append(vertices[0].X).ToList();
        var ys = vertices.Select(v => v.Y).Append(vertices[0].Y).ToList();
        double area = WktHelper.PolygonArea(xs, ys);
        if (!(area > Sp63RectangularGeometryPolicy.GeometryTolerance))
            return Degenerate();

        var (cx, cy) = WktHelper.PolygonCentroid(xs, ys);
        var radii = vertices
            .Select(v => Math.Sqrt((v.X - cx) * (v.X - cx) + (v.Y - cy) * (v.Y - cy)))
            .ToList();
        double mean = radii.Average();
        double radial = radii.Max(r => Math.Abs(r - mean)) / mean;
        double circleArea = Math.PI * mean * mean;
        double areaDeviation = Math.Abs(area - circleArea) / circleArea;
        return new Sp63ContourCircleMeasure(cx, cy, area, mean, radial, areaDeviation);
    }

    static Sp63ContourCircleMeasure Degenerate() =>
        new(0.0, 0.0, 0.0, 0.0, double.PositiveInfinity, double.PositiveInfinity);

    static Sp63CircularGeometryClassification Fail(string code, string reference,
        string text) =>
        new(null, [new Sp63NormalMessage(code, Sp63NormalMessageKind.Applicability,
            reference, text)]);
}
