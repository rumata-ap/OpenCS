namespace CScore.Import;

/// <summary>
/// Ориентация местных осей стержня SCAD (ApiGetSystemCoordElem, группа стержней): тип ApiSystemCoord и данные —
/// угол (тип 1 — град., 2 — рад.) или точка/вектор (3–10), на которые направлена ось Y1 или Z1; точки — в метрах.
/// </summary>
public sealed record ScadRodAxes(int Type, double[] Values)
{
    public const int AngleDeg = 1, AngleRad = 2;

    /// <summary>
    /// Ось Y1 стержня p1 → p2 по правилу SCAD (справка «Местные системы координат стержня»): при нулевом угле Z1⁰
    /// направлена в верхнее полупространство, Y1⁰ = Z1⁰ × X1 — горизонтальна; у вертикального стержня Y1⁰ — вдоль
    /// глобальной Y (правило ЛИРЫ, в справке SCAD не описано). Угол разворота — против часовой стрелки с конца X1.
    /// Точка/вектор — Y1 или Z1 на точку (вектор); главные и конструктивные оси не различаются (угол FU сечения
    /// не учитывается). Null <paramref name="axes"/> — ориентация по умолчанию; null результат — стержень нулевой длины
    /// или вырожденная ориентация.
    /// </summary>
    public static double[]? LocalY(ScadRodAxes? axes, (double X, double Y, double Z) p1, (double X, double Y, double Z) p2)
    {
        double[] x = [p2.X - p1.X, p2.Y - p1.Y, p2.Z - p1.Z];
        double l = Norm(x);
        if (l < 1e-12) return null;
        x = Scale(x, 1 / l);

        double[] y0 = Math.Sqrt(x[0] * x[0] + x[1] * x[1]) < 1e-6
            ? [0, 1, 0]
            : Normalize(Cross([0, 0, 1], x)); // Z1⁰ — вверх, Y1⁰ = Z1⁰ × X1 горизонтальна
        if (axes == null || axes.Values.Length == 0) return y0;

        switch (axes.Type)
        {
            case AngleDeg or AngleRad:
            {
                double a = axes.Type == AngleDeg ? axes.Values[0] * Math.PI / 180 : axes.Values[0];
                var z0 = Cross(x, y0);
                return [Math.Cos(a) * y0[0] + Math.Sin(a) * z0[0], Math.Cos(a) * y0[1] + Math.Sin(a) * z0[1],
                    Math.Cos(a) * y0[2] + Math.Sin(a) * z0[2]];
            }
            case >= 3 and <= 10 when axes.Values.Length >= 3:
            {
                bool point = axes.Type % 2 == 1; // 3, 5, 7, 9 — точка; 4, 6, 8, 10 — вектор
                bool towardsZ = axes.Type is 5 or 6 or 9 or 10;
                double[] d = point
                    ? [axes.Values[0] - p1.X, axes.Values[1] - p1.Y, axes.Values[2] - p1.Z]
                    : [axes.Values[0], axes.Values[1], axes.Values[2]];
                var t = Sub(d, Scale(x, Dot(d, x)));
                if (Norm(t) < 1e-9) return null;
                t = Normalize(t);
                return towardsZ ? Cross(t, x) : t; // Z1 = t ⇒ Y1 = Z1 × X1
            }
            default:
                return y0;
        }
    }

    static double[] Cross(double[] a, double[] b) =>
        [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
    static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    static double Norm(double[] a) => Math.Sqrt(Dot(a, a));
    static double[] Scale(double[] a, double k) => [a[0] * k, a[1] * k, a[2] * k];
    static double[] Sub(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
    static double[] Normalize(double[] a) => Scale(a, 1 / Norm(a));
}
