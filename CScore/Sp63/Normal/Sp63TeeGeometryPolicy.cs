using CScore.Sp63;

namespace CScore.Sp63.Normal;

/// <summary>Нормализованная геометрия таврового/двутаврового сечения.</summary>
/// <param name="Bw">Ширина стенки, м.</param>
/// <param name="H">Полная высота сечения, м.</param>
/// <param name="HeightCoordMin">Нижняя граница по координате высоты, м.</param>
/// <param name="HeightCoordMax">Верхняя граница по координате высоты, м.</param>
/// <param name="TopFlangeWidth">Ширина верхней полки, м (0 — полки нет).</param>
/// <param name="TopFlangeThickness">Толщина верхней полки, м (0 — полки нет).</param>
/// <param name="BottomFlangeWidth">Ширина нижней полки, м (0 — полки нет).</param>
/// <param name="BottomFlangeThickness">Толщина нижней полки, м (0 — полки нет).</param>
public sealed record Sp63TeeGeometry(
    double Bw,
    double H,
    double HeightCoordMin,
    double HeightCoordMax,
    double TopFlangeWidth,
    double TopFlangeThickness,
    double BottomFlangeWidth,
    double BottomFlangeThickness);

/// <summary>Причина неприменимости тавровой геометрии.</summary>
public enum Sp63TeeGeometryFailure
{
    /// <summary>Геометрия применима.</summary>
    None,
    /// <summary>Контур — одна полоса, то есть прямоугольник.</summary>
    NotATeeShape,
    /// <summary>Контур не сводится к осевым полосам тавра/двутавра.</summary>
    UnsupportedGeometry
}

/// <summary>Результат классификации тавровой геометрии.</summary>
public sealed record Sp63TeeGeometryClassification(
    bool IsApplicable,
    Sp63TeeGeometry? Geometry,
    Sp63TeeGeometryFailure Failure,
    IReadOnlyList<string> Reasons);

/// <summary>
/// Классифицирует осевой контур как тавр/двутавр из двух-трёх полос.
/// Это ограничение автоматизированного режима OpenCS, а не утверждение о границах СП.
/// </summary>
public static class Sp63TeeGeometryPolicy
{
    /// <summary>Геометрический допуск сравнения координат, м.</summary>
    public const double GeometryTolerance = Sp63RectangularGeometryPolicy.GeometryTolerance;

    sealed record Strip(double HeightLow, double HeightHigh, double Width,
        double Center)
    {
        public double Thickness => HeightHigh - HeightLow;
    }

    /// <summary>Проверяет, что контур сводится к полосам тавра/двутавра.</summary>
    public static Sp63TeeGeometryClassification Classify(CrossSection section,
        Sp63NormalAxis axis)
    {
        ArgumentNullException.ThrowIfNull(section);

        var concrete = section.Areas
            .Where(area => area.Category == AreaCategory.Region &&
                           area.Material?.Type == MatType.Concrete)
            .ToList();
        if (concrete.Count != 1)
            return Unsupported("Тавровый режим требует ровно одну бетонную область.");

        var area = concrete[0];
        if (area.Holes.Count != 0)
            return Unsupported("Тавровый режим не поддерживает отверстия в бетоне.");
        if (area.Hull is null)
            return Unsupported("У бетонной области отсутствует замкнутый контур.");

        var hull = area.Hull;
        if (hull.X.Count < 4 || !IsClosed(hull))
            return Unsupported("Контур бетонной области не замкнут.");

        var vertices = ContourVertices(hull);
        if (vertices.Count < 4)
            return Unsupported("Контур бетонной области вырожден.");

        int count = vertices.Count;
        var isConstantWidthEdge = new bool[count];
        for (int index = 0; index < count; index++)
        {
            var start = vertices[index];
            var end = vertices[(index + 1) % count];
            double du = HeightCoord(end, axis) - HeightCoord(start, axis);
            double dv = WidthCoord(end, axis) - WidthCoord(start, axis);
            bool constantHeight = Math.Abs(du) <= GeometryTolerance;
            bool constantWidth = Math.Abs(dv) <= GeometryTolerance;
            if (constantHeight == constantWidth)
                return Unsupported("Контур не является осевым многоугольником.");
            isConstantWidthEdge[index] = constantWidth;
        }

        for (int index = 0; index < count; index++)
            if (isConstantWidthEdge[index] == isConstantWidthEdge[(index + 1) % count])
                return Unsupported("Контур содержит лишние коллинеарные вершины.");

        var levels = vertices
            .Select(vertex => HeightCoord(vertex, axis))
            .OrderBy(value => value)
            .Aggregate(new List<double>(), (list, value) =>
            {
                if (list.Count == 0 ||
                    Math.Abs(list[^1] - value) > GeometryTolerance)
                    list.Add(value);
                return list;
            });
        if (levels.Count < 2)
            return Unsupported("Высота бетонной области вырождена.");

        var strips = new List<Strip>();
        for (int index = 0; index < levels.Count - 1; index++)
        {
            double heightLow = levels[index];
            double heightHigh = levels[index + 1];
            if (heightHigh - heightLow <= GeometryTolerance)
                continue;

            double middle = (heightLow + heightHigh) / 2.0;
            var crossings = new List<double>();
            for (int edge = 0; edge < count; edge++)
            {
                var start = vertices[edge];
                var end = vertices[(edge + 1) % count];
                if (!isConstantWidthEdge[edge])
                    continue;
                double uStart = HeightCoord(start, axis);
                double uEnd = HeightCoord(end, axis);
                if (Math.Min(uStart, uEnd) < middle && middle < Math.Max(uStart, uEnd))
                    crossings.Add(WidthCoord(start, axis));
            }

            if (crossings.Count != 2)
                return Unsupported("Полосы контура не образуют непрерывной ширины.");
            double widthLow = Math.Min(crossings[0], crossings[1]);
            double widthHigh = Math.Max(crossings[0], crossings[1]);
            if (widthHigh - widthLow <= GeometryTolerance)
                return Unsupported("Полоса контура вырождена по ширине.");
            strips.Add(new Strip(heightLow, heightHigh, widthHigh - widthLow,
                (widthLow + widthHigh) / 2.0));
        }

        if (strips.Count == 1)
            return new Sp63TeeGeometryClassification(false, null,
                Sp63TeeGeometryFailure.NotATeeShape,
                ["Контур является прямоугольником; выберите прямоугольную форму."]);
        if (strips.Count > 3)
            return Unsupported("Тавровый режим поддерживает не более трёх полос по высоте.");

        Strip web;
        Strip topFlange;
        Strip bottomFlange;
        if (strips.Count == 2)
        {
            var lower = strips[0];
            var upper = strips[1];
            if (lower.Width > upper.Width)
            {
                web = upper;
                bottomFlange = lower;
                topFlange = new Strip(0, 0, 0, 0);
            }
            else if (upper.Width > lower.Width)
            {
                web = lower;
                topFlange = upper;
                bottomFlange = new Strip(0, 0, 0, 0);
            }
            else
            {
                return Unsupported("Полка не шире стенки.");
            }
        }
        else
        {
            bottomFlange = strips[0];
            web = strips[1];
            topFlange = strips[2];
            if (!(bottomFlange.Width > web.Width) || !(topFlange.Width > web.Width))
                return Unsupported("Полка не шире стенки.");
        }

        if (topFlange.Width > 0 &&
            Math.Abs(topFlange.Center - web.Center) > GeometryTolerance ||
            bottomFlange.Width > 0 &&
            Math.Abs(bottomFlange.Center - web.Center) > GeometryTolerance)
            return Unsupported("Полка смещена относительно стенки.");

        double heightCoordMin = levels[0];
        double heightCoordMax = levels[^1];
        var geometry = new Sp63TeeGeometry(
            web.Width,
            heightCoordMax - heightCoordMin,
            heightCoordMin,
            heightCoordMax,
            topFlange.Width,
            topFlange.Thickness,
            bottomFlange.Width,
            bottomFlange.Thickness);
        return new Sp63TeeGeometryClassification(true, geometry,
            Sp63TeeGeometryFailure.None, []);
    }

    static bool IsClosed(Contour hull)
    {
        int count = Math.Min(hull.X.Count, hull.Y.Count);
        if (count < 2) return false;
        return Math.Abs(hull.X[0] - hull.X[count - 1]) <= GeometryTolerance &&
               Math.Abs(hull.Y[0] - hull.Y[count - 1]) <= GeometryTolerance;
    }

    static List<(double X, double Y)> ContourVertices(Contour contour)
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

    static double HeightCoord((double X, double Y) point, Sp63NormalAxis axis) =>
        axis == Sp63NormalAxis.Mx ? point.Y : point.X;

    static double WidthCoord((double X, double Y) point, Sp63NormalAxis axis) =>
        axis == Sp63NormalAxis.Mx ? point.X : point.Y;

    static bool Same((double X, double Y) left, (double X, double Y) right) =>
        Math.Abs(left.X - right.X) <= GeometryTolerance &&
        Math.Abs(left.Y - right.Y) <= GeometryTolerance;

    static Sp63TeeGeometryClassification Unsupported(string reason) =>
        new(false, null, Sp63TeeGeometryFailure.UnsupportedGeometry, [reason]);
}
