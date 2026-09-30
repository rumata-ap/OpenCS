namespace CScore.Fem;

/// <summary>Плоскость, в которой откладываются ординаты эпюры на стержне схемы.</summary>
public enum BarDiagramPlane
{
    /// <summary>Плоскость X1Z1 стержня: ординаты вдоль местной оси Z1.</summary>
    Z1,
    /// <summary>Плоскость X1Y1 стержня: ординаты вдоль местной оси Y1.</summary>
    Y1,
}

/// <summary>Отрезок эпюры в пространстве схемы.</summary>
public readonly record struct BarDiagramLine((double X, double Y, double Z) A, (double X, double Y, double Z) B);

/// <summary>Геометрия эпюр на стержнях схемы: ординаты и контур в местных осях стержня.</summary>
public static class BarDiagramGeometry
{
    /// <summary>
    /// Местные оси стержня по правилу ЛИРЫ при нулевом угле чистого вращения (угол в схему не импортируется):
    /// у невертикального стержня Z1 лежит в вертикальной плоскости стержня и направлена вверх,
    /// у вертикального — против глобальной X (Y1 — вдоль глобальной Y); Y1 = Z1 × X1.
    /// </summary>
    /// <returns>Единичные векторы Y1 и Z1; null — стержень нулевой длины.</returns>
    public static ((double X, double Y, double Z) Y1, (double X, double Y, double Z) Z1)? LocalAxes(
        (double X, double Y, double Z) p1, (double X, double Y, double Z) p2)
    {
        var x = (X: p2.X - p1.X, Y: p2.Y - p1.Y, Z: p2.Z - p1.Z);
        double length = Math.Sqrt(x.X * x.X + x.Y * x.Y + x.Z * x.Z);
        if (length < 1e-12) return null;
        x = (x.X / length, x.Y / length, x.Z / length);

        (double X, double Y, double Z) z;
        if (Math.Sqrt(x.X * x.X + x.Y * x.Y) < 1e-6)
            z = (-1, 0, 0);
        else
        {
            // Вертикаль без составляющей вдоль стержня.
            z = (-x.Z * x.X, -x.Z * x.Y, 1 - x.Z * x.Z);
            double zl = Math.Sqrt(z.X * z.X + z.Y * z.Y + z.Z * z.Z);
            z = (z.X / zl, z.Y / zl, z.Z / zl);
        }
        var y = (z.Y * x.Z - z.Z * x.Y, z.Z * x.X - z.X * x.Z, z.X * x.Y - z.Y * x.X);
        return (y, z);
    }

    /// <summary>
    /// Добавить отрезки эпюры одного стержня: ординаты в точках профиля и контур между ними.
    /// Положительные значения откладываются в сторону +Z1 (+Y1); участок контура, меняющий знак,
    /// делится в точке нуля. Нулевые ординаты не рисуются.
    /// </summary>
    /// <param name="positive">Куда складывать отрезки положительной части эпюры.</param>
    /// <param name="negative">Куда складывать отрезки отрицательной части.</param>
    /// <param name="profile">Точки (t, значение) вдоль стержня, t = 0 — начальный узел; по возрастанию t
    /// (две точки с одним t — скачок).</param>
    /// <param name="scale">Длина ординаты на единицу значения, м.</param>
    public static void AddBar(
        List<BarDiagramLine> positive, List<BarDiagramLine> negative,
        (double X, double Y, double Z) p1, (double X, double Y, double Z) p2,
        IReadOnlyList<(double T, double V)> profile, BarDiagramPlane plane, double scale)
    {
        if (profile.Count == 0 || LocalAxes(p1, p2) is not { } axes) return;
        var d = plane == BarDiagramPlane.Z1 ? axes.Z1 : axes.Y1;

        (double, double, double) Axis(double t) =>
            (p1.X + (p2.X - p1.X) * t, p1.Y + (p2.Y - p1.Y) * t, p1.Z + (p2.Z - p1.Z) * t);
        (double, double, double) Tip(double t, double v)
        {
            var a = Axis(t);
            return (a.Item1 + d.X * v * scale, a.Item2 + d.Y * v * scale, a.Item3 + d.Z * v * scale);
        }
        void Add(double v, (double, double, double) a, (double, double, double) b) =>
            (v > 0 ? positive : negative).Add(new BarDiagramLine(a, b));

        for (int i = 0; i < profile.Count; i++)
        {
            var (t, v) = profile[i];
            if (v != 0) Add(v, Axis(t), Tip(t, v));
            if (i + 1 == profile.Count) break;

            var (t2, v2) = profile[i + 1];
            if (v == 0 && v2 == 0) continue;
            if (v * v2 < 0)
            {
                double tz = t + (t2 - t) * v / (v - v2);
                Add(v, Tip(t, v), Axis(tz));
                Add(v2, Axis(tz), Tip(t2, v2));
            }
            else
                Add(v != 0 ? v : v2, Tip(t, v), Tip(t2, v2));
        }
    }
}
