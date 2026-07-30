namespace CScore.PlateRebar;

/// <summary>Отрезок штриховки в локальных координатах поверхности (u,v).</summary>
public readonly record struct HatchSegment(double U1, double V1, double U2, double V2);

/// <summary>Схематичная (не буквальная по шагу) генерация штриховки направлений
/// армирования для визуализации на канвасе. Число линий фиксировано —
/// подсказка направления и угла, а не точный чертёж.</summary>
public static class RebarHatchGeometry
{
    public const int LineCount = 5;

    /// <summary>Линии вдоль повёрнутой на <paramref name="angleDeg"/> локальной оси X
    /// (шаг между линиями — вдоль повёрнутой оси Y).</summary>
    public static IReadOnlyList<HatchSegment> BuildDirectionX(
        IReadOnlyList<(double U, double V)> polygon, double angleDeg) =>
        BuildLines(polygon, angleDeg, alongX: true);

    /// <summary>Линии вдоль повёрнутой на <paramref name="angleDeg"/> локальной оси Y
    /// (шаг между линиями — вдоль повёрнутой оси X).</summary>
    public static IReadOnlyList<HatchSegment> BuildDirectionY(
        IReadOnlyList<(double U, double V)> polygon, double angleDeg) =>
        BuildLines(polygon, angleDeg, alongX: false);

    static List<HatchSegment> BuildLines(
        IReadOnlyList<(double U, double V)> polygon, double angleDeg, bool alongX)
    {
        var result = new List<HatchSegment>(LineCount);
        if (polygon.Count < 2) return result;

        double rad = angleDeg * System.Math.PI / 180.0;
        double cos = System.Math.Cos(rad), sin = System.Math.Sin(rad);

        // Повернуть на -angleDeg: направление штриховки становится осью U рабочей системы.
        var rotated = new (double U, double V)[polygon.Count];
        for (int i = 0; i < polygon.Count; i++)
        {
            double u = polygon[i].U, v = polygon[i].V;
            rotated[i] = (u * cos + v * sin, -u * sin + v * cos);
        }

        double uMin = double.MaxValue, uMax = double.MinValue;
        double vMin = double.MaxValue, vMax = double.MinValue;
        foreach (var p in rotated)
        {
            if (p.U < uMin) uMin = p.U;
            if (p.U > uMax) uMax = p.U;
            if (p.V < vMin) vMin = p.V;
            if (p.V > vMax) vMax = p.V;
        }

        if (alongX)
        {
            if (vMax - vMin < 1e-9) return result;
            for (int i = 1; i <= LineCount; i++)
            {
                double v = vMin + i / (double)(LineCount + 1) * (vMax - vMin);
                result.Add(RotateBack(uMin, v, uMax, v, cos, sin));
            }
        }
        else
        {
            if (uMax - uMin < 1e-9) return result;
            for (int i = 1; i <= LineCount; i++)
            {
                double u = uMin + i / (double)(LineCount + 1) * (uMax - uMin);
                result.Add(RotateBack(u, vMin, u, vMax, cos, sin));
            }
        }
        return result;
    }

    /// <summary>Обратный поворот (+angleDeg) точки из рабочей системы в исходные (u,v).</summary>
    static HatchSegment RotateBack(double u1, double v1, double u2, double v2, double cos, double sin)
    {
        (double U, double V) Back(double u, double v) => (u * cos - v * sin, u * sin + v * cos);
        var (U1, V1) = Back(u1, v1);
        var (U2, V2) = Back(u2, v2);
        return new HatchSegment(U1, V1, U2, V2);
    }

    /// <summary>Среднее вершин полигона — точка привязки подписи.</summary>
    public static (double U, double V) Centroid(IReadOnlyList<(double U, double V)> polygon)
    {
        if (polygon.Count == 0) return (0, 0);
        double su = 0, sv = 0;
        foreach (var p in polygon) { su += p.U; sv += p.V; }
        return (su / polygon.Count, sv / polygon.Count);
    }
}
