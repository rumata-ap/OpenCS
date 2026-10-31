using System.Globalization;
using CScore.Planar;

namespace CScore.Fem.Fragment;

/// <summary>Плоскость, ограничивающая фрагмент: внешняя нормаль, смещение (внутри — n·p ≤ смещения), сторона разреза.</summary>
public sealed record FragmentBoundPlane(PlanarVector3 Normal, double Offset, FragmentCutSide Side)
{
    /// <summary>Отстояние со знаком: больше нуля — снаружи фрагмента.</summary>
    public double Eval(PlanarVector3 p) => Normal.Dot(p) - Offset;
}

/// <summary>
/// Границы фрагмента — полоса z₁…z₂ и (если задана) призма в плане: принадлежность точки, отсечение отрезка и
/// многоугольника. Общие для построителя и сверки масок с нагрузками полосы родителя.
/// </summary>
public sealed class FragmentBounds
{
    public FragmentBounds(double z1, double z2, FragmentPrism? prism, double tol)
    {
        Z1 = z1;
        Z2 = z2;
        Prism = prism;
        Tol = tol;
        Planes =
        [
            new(new PlanarVector3(0, 0, -1), -z1, FragmentCutSide.Bottom),
            new(new PlanarVector3(0, 0, 1), z2, FragmentCutSide.Top),
            .. prism?.Faces.Select(f => new FragmentBoundPlane(f.Normal, f.Offset, FragmentCutSide.Lateral)) ?? [],
        ];
    }

    public double Z1 { get; }
    public double Z2 { get; }
    public FragmentPrism? Prism { get; }
    public double Tol { get; }
    /// <summary>z₁, z₂ и боковые грани призмы.</summary>
    public IReadOnlyList<FragmentBoundPlane> Planes { get; }
    public IEnumerable<FragmentBoundPlane> Lateral => Planes.Where(p => p.Side == FragmentCutSide.Lateral);

    /// <summary>Точка внутри фрагмента или на его границе в допуске.</summary>
    public bool InBox(PlanarVector3 p) => Planes.All(pl => pl.Eval(p) <= Tol);

    /// <summary>
    /// Часть s0…s1 луча a + u·s внутри фрагмента (Сайрус–Бек; пустая — Hi ≤ Lo). Луч вдоль плоскости — целиком внутри
    /// или снаружи по допуску.
    /// </summary>
    public (double Lo, double Hi) ClipRange(PlanarVector3 a, PlanarVector3 u, double s0, double s1)
    {
        double lo = s0, hi = s1;
        foreach (var pl in Planes)
        {
            double den = pl.Normal.Dot(u), num = -pl.Eval(a);
            if (Math.Abs(den) < 1e-12)
            {
                if (num < -Tol) return (0, 0);
                continue;
            }
            if (den > 0) hi = Math.Min(hi, num / den);
            else lo = Math.Max(lo, num / den);
        }
        return (lo, hi);
    }

    /// <summary>Многоугольник в 3D, отсечённый всеми плоскостями фрагмента; пусто — снаружи.</summary>
    public List<PlanarVector3> ClipPolygon(List<PlanarVector3> polygon)
    {
        foreach (var plane in Planes)
        {
            polygon = ClipPlane(polygon, plane);
            if (polygon.Count < 3) return [];
        }
        return polygon;
    }

    /// <summary>Сазерленд–Ходжман по одной плоскости: остаётся внутренняя сторона.</summary>
    public static List<PlanarVector3> ClipPlane(List<PlanarVector3> polygon, FragmentBoundPlane plane)
    {
        var result = new List<PlanarVector3>(polygon.Count + 2);
        for (int i = 0; i < polygon.Count; i++)
        {
            var p = polygon[i];
            var q = polygon[(i + 1) % polygon.Count];
            double dp = -plane.Eval(p), dq = -plane.Eval(q);
            if (dp >= 0) result.Add(p);
            if (dp >= 0 != dq >= 0) result.Add(p + (q - p) * (dp / (dp - dq)));
        }
        return result;
    }

    /// <summary>Площадь и центр плоского выпуклого многоугольника в 3D (веер треугольников).</summary>
    public static (double Area, PlanarVector3 Centroid) AreaCentroid(List<PlanarVector3> polygon)
    {
        double area = 0;
        var moment = PlanarVector3.Zero;
        for (int k = 1; k + 1 < polygon.Count; k++)
        {
            double a = 0.5 * (polygon[k] - polygon[0]).Cross(polygon[k + 1] - polygon[0]).Length;
            area += a;
            moment += (polygon[0] + polygon[k] + polygon[k + 1]) * (a / 3);
        }
        return (area, area > 0 ? moment * (1 / area) : polygon[0]);
    }
}

/// <summary>Подписи сторон разрезов фрагмента для сообщений: «низ», «верх», «бок, грань +X», «бок, ∠30°».</summary>
public static class FragmentCutLabel
{
    /// <summary>Нормаль вдоль общей оси, если косинус не меньше этого (1°).</summary>
    const double AxisAlignedCos = 0.99985;

    public static string Side(FragmentCut cut)
    {
        ArgumentNullException.ThrowIfNull(cut);
        return cut.Side switch
        {
            FragmentCutSide.Bottom => "низ",
            FragmentCutSide.Top => "верх",
            _ => FaceCode(cut.Outward, out bool axis) is var code && axis ? "бок, грань " + code : "бок, " + code,
        };
    }

    /// <summary>
    /// Грань по внешней нормали в плане: «+X» … «−Y» (<paramref name="alongAxis"/> = true) или азимут нормали «∠30°».
    /// </summary>
    public static string FaceCode(PlanarVector3 normal, out bool alongAxis)
    {
        alongAxis = true;
        double len = Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y);
        if (len >= 1e-12)
        {
            double x = normal.X / len, y = normal.Y / len;
            if (x >= AxisAlignedCos) return "+X";
            if (x <= -AxisAlignedCos) return "−X";
            if (y >= AxisAlignedCos) return "+Y";
            if (y <= -AxisAlignedCos) return "−Y";
        }
        alongAxis = false;
        if (len < 1e-12) return "∠?";
        double deg = Math.Atan2(normal.Y, normal.X) * 180 / Math.PI;
        if (deg < 0) deg += 360;
        return string.Create(CultureInfo.CurrentCulture, $"∠{deg:0}°");
    }
}
