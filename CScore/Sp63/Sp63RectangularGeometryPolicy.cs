namespace CScore.Sp63;

/// <summary>Габариты осевого прямоугольника бетонной области.</summary>
/// <param name="MinX">Минимальная координата X, м.</param>
/// <param name="MaxX">Максимальная координата X, м.</param>
/// <param name="MinY">Минимальная координата Y, м.</param>
/// <param name="MaxY">Максимальная координата Y, м.</param>
public sealed record Sp63RectangularGeometry(
    double MinX,
    double MaxX,
    double MinY,
    double MaxY)
{
    /// <summary>Ширина прямоугольника по оси X, м.</summary>
    public double Width => MaxX - MinX;

    /// <summary>Высота прямоугольника по оси Y, м.</summary>
    public double Height => MaxY - MinY;

    /// <summary>Площадь прямоугольника, м².</summary>
    public double Area => Width * Height;
}

/// <summary>Результат проверки применимости осевой прямоугольной геометрии.</summary>
/// <param name="IsApplicable">Доступен ли поддержанный прямоугольный режим.</param>
/// <param name="Geometry">Габариты прямоугольника или <see langword="null"/>.</param>
/// <param name="Reasons">Причины неприменимости.</param>
public sealed record Sp63RectangularGeometryClassification(
    bool IsApplicable,
    Sp63RectangularGeometry? Geometry,
    IReadOnlyList<string> Reasons);

/// <summary>
/// Общая политика распознавания сплошного осевого прямоугольника.
/// Это ограничение автоматизированных режимов OpenCS, а не утверждение о границах СП.
/// </summary>
public static class Sp63RectangularGeometryPolicy
{
    /// <summary>Геометрический допуск сравнения координат, м.</summary>
    public const double GeometryTolerance = 1e-9;

    /// <summary>Проверяет бетонную геометрию сечения.</summary>
    public static Sp63RectangularGeometryClassification Classify(CrossSection section)
    {
        ArgumentNullException.ThrowIfNull(section);

        var reasons = new List<string>();
        var concrete = section.Areas
            .Where(area => area.Category == AreaCategory.Region &&
                           area.Material?.Type == MatType.Concrete)
            .ToList();

        if (concrete.Count != 1)
        {
            reasons.Add("Автоматический нормативный режим требует ровно одну бетонную область.");
            return NotApplicable(reasons);
        }

        var area = concrete[0];
        if (area.Holes.Count != 0)
            reasons.Add("Автоматический нормативный режим не поддерживает отверстия в бетонной области.");
        if (area.Hull is null)
            reasons.Add("У бетонной области отсутствует замкнутый внешний контур.");
        if (reasons.Count != 0)
            return NotApplicable(reasons);

        var hull = area.Hull!;
        var vertices = DistinctContourVertices(hull);
        if (vertices.Count != 4)
            reasons.Add("Автоматический нормативный режим требует контур с четырьмя различными вершинами.");

        if (Math.Abs(WktHelper.PolygonArea(hull.X, hull.Y)) <=
            GeometryTolerance * GeometryTolerance)
            reasons.Add("Площадь бетонного контура вырождена.");

        if (vertices.Count == 4)
        {
            var xs = DistinctLevels(vertices.Select(point => point.X));
            var ys = DistinctLevels(vertices.Select(point => point.Y));
            if (xs.Count != 2 || ys.Count != 2 ||
                !HasPoint(vertices, xs[0], ys[0]) || !HasPoint(vertices, xs[0], ys[1]) ||
                !HasPoint(vertices, xs[1], ys[0]) || !HasPoint(vertices, xs[1], ys[1]))
                reasons.Add("Бетонный контур не является осевым прямоугольником.");
        }

        if (reasons.Count != 0)
            return NotApplicable(reasons);

        var xsValid = vertices.Select(point => point.X).ToList();
        var ysValid = vertices.Select(point => point.Y).ToList();
        var geometry = new Sp63RectangularGeometry(
            xsValid.Min(), xsValid.Max(), ysValid.Min(), ysValid.Max());
        return new Sp63RectangularGeometryClassification(true, geometry, reasons);
    }

    /// <summary>Возвращает вершины без повторяющейся замыкающей точки.</summary>
    static List<(double X, double Y)> DistinctContourVertices(Contour contour)
    {
        int count = Math.Min(contour.X.Count, contour.Y.Count);
        var vertices = new List<(double X, double Y)>();
        for (int index = 0; index < count; index++)
        {
            var point = (contour.X[index], contour.Y[index]);
            if (vertices.Count > 0 && Same(vertices[^1], point)) continue;
            vertices.Add(point);
        }

        if (vertices.Count > 1 && Same(vertices[0], vertices[^1]))
            vertices.RemoveAt(vertices.Count - 1);
        return vertices;
    }

    /// <summary>Возвращает уровни координаты, различающиеся больше допуска.</summary>
    static List<double> DistinctLevels(IEnumerable<double> values)
    {
        var levels = new List<double>();
        foreach (double value in values.OrderBy(value => value))
            if (!levels.Any(level => Math.Abs(level - value) <= GeometryTolerance))
                levels.Add(value);
        return levels;
    }

    /// <summary>Проверяет наличие вершины в ожидаемом углу прямоугольника.</summary>
    static bool HasPoint(IEnumerable<(double X, double Y)> points, double x, double y) =>
        points.Any(point => Math.Abs(point.X - x) <= GeometryTolerance &&
                            Math.Abs(point.Y - y) <= GeometryTolerance);

    /// <summary>Проверяет совпадение точек с геометрическим допуском.</summary>
    static bool Same((double X, double Y) left, (double X, double Y) right) =>
        Math.Abs(left.X - right.X) <= GeometryTolerance &&
        Math.Abs(left.Y - right.Y) <= GeometryTolerance;

    /// <summary>Создаёт результат неприменимости без габаритов.</summary>
    static Sp63RectangularGeometryClassification NotApplicable(List<string> reasons) =>
        new(false, null, reasons);
}
