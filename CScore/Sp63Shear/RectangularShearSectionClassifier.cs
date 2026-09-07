namespace CScore.Sp63Shear;

/// <summary>Результат проверки автоматически поддерживаемой геометрии сечения.</summary>
/// <param name="IsSupported">Соответствует ли бетонное тело сплошному осевому прямоугольнику.</param>
/// <param name="Reasons">Причины, по которым автоматическая нормативная геометрия недоступна.</param>
public sealed record RectangularShearSectionClassification(
    bool IsSupported, IReadOnlyList<string> Reasons);

/// <summary>
/// Выделяет узкое автоматически проверяемое подмножество: одну сплошную прямоугольную
/// бетонную область без отверстий. Это ограничение OpenCS, а не утверждение о границах СП.
/// </summary>
public static class RectangularShearSectionClassifier
{
    /// <summary>Геометрический допуск, м.</summary>
    public const double GeometryTolerance = 1e-9;

    /// <summary>Проверяет бетонную геометрию сечения.</summary>
    public static RectangularShearSectionClassification Classify(CrossSection section)
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
            return new RectangularShearSectionClassification(false, reasons);
        }

        var area = concrete[0];
        if (area.Holes.Count != 0)
            reasons.Add("Автоматический нормативный режим не поддерживает отверстия в бетонной области.");
        if (area.Hull is null)
            reasons.Add("У бетонной области отсутствует замкнутый внешний контур.");
        if (reasons.Count != 0)
            return new RectangularShearSectionClassification(false, reasons);

        var hull = area.Hull!;
        var vertices = DistinctContourVertices(hull);
        if (vertices.Count != 4)
            reasons.Add("Автоматический нормативный режим требует контур с четырьмя различными вершинами.");

        if (Math.Abs(WktHelper.PolygonArea(hull.X, hull.Y)) <= GeometryTolerance * GeometryTolerance)
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

        return new RectangularShearSectionClassification(reasons.Count == 0, reasons);
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
        if (vertices.Count > 1 && Same(vertices[0], vertices[^1])) vertices.RemoveAt(vertices.Count - 1);
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

    /// <summary>Есть ли вершина в ожидаемом углу прямоугольника.</summary>
    static bool HasPoint(IEnumerable<(double X, double Y)> points, double x, double y) =>
        points.Any(point => Math.Abs(point.X - x) <= GeometryTolerance &&
                            Math.Abs(point.Y - y) <= GeometryTolerance);

    /// <summary>Совпадают ли точки в геометрическом допуске.</summary>
    static bool Same((double X, double Y) left, (double X, double Y) right) =>
        Math.Abs(left.X - right.X) <= GeometryTolerance &&
        Math.Abs(left.Y - right.Y) <= GeometryTolerance;
}
