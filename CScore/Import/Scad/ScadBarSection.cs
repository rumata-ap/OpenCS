using System.Globalization;

namespace CScore.Import;

/// <summary>
/// Геометрические характеристики параметрического сечения стержня SCAD в местных осях (м): площадь, моменты инерции
/// относительно Y1 (изгиб в плоскости X1Z1) и Z1, момент инерции кручения.
/// </summary>
public sealed record ScadBarSection(string Keyword, double A, double Iy, double Iz, double J)
{
    /// <summary>
    /// Разбор строки жёсткости «Sk E p1 p2 …» (размеры в единицах сечений проекта, справка SCAD «Параметрические
    /// сечения»): S0 — брус b ‖ Y1, h ‖ Z1; S3 — двутавр b (стенка), h (полная высота), b1, h1 (нижняя полка), b2, h2
    /// (верхняя полка); S6 — труба D, d. Кручение — по тонкостенной формуле Σ b·t³/3 (S3), Сен-Венан (S0), полярный
    /// момент (S6). Null — другой вид сечения или неполные данные.
    /// </summary>
    public static ScadBarSection? Parse(string? text, double sectionUnitM)
    {
        if (string.IsNullOrWhiteSpace(text) || sectionUnitM <= 0) return null;
        var parts = text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        double P(int i) => i < parts.Length && double.TryParse(parts[i].Replace(',', '.'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out double v) ? v * sectionUnitM : double.NaN;

        switch (parts[0])
        {
            case "S0":
            {
                double b = P(2), h = P(3);
                if (!(b > 0 && h > 0)) return null;
                double lo = Math.Min(b, h), hi = Math.Max(b, h);
                double j = lo * lo * lo * hi * (1.0 / 3 - 0.21 * lo / hi * (1 - Math.Pow(lo / hi, 4) / 12));
                return new ScadBarSection("S0", b * h, b * h * h * h / 12, h * b * b * b / 12, j);
            }
            case "S3":
            {
                double b = P(2), h = P(3), b1 = P(4), h1 = P(5), b2 = P(6), h2 = P(7);
                double hw = h - h1 - h2;
                if (!(b > 0 && b1 > 0 && h1 > 0 && b2 > 0 && h2 > 0 && hw > 0)) return null;
                (double A, double Zc, double I0)[] rects =
                [
                    (b1 * h1, h1 / 2, b1 * h1 * h1 * h1 / 12),
                    (b * hw, h1 + hw / 2, b * hw * hw * hw / 12),
                    (b2 * h2, h - h2 / 2, b2 * h2 * h2 * h2 / 12),
                ];
                double a = rects.Sum(r => r.A), zc = rects.Sum(r => r.A * r.Zc) / a;
                double iy = rects.Sum(r => r.I0 + r.A * (r.Zc - zc) * (r.Zc - zc));
                double iz = (h1 * b1 * b1 * b1 + hw * b * b * b + h2 * b2 * b2 * b2) / 12;
                double j = (b1 * h1 * h1 * h1 + hw * b * b * b + b2 * h2 * h2 * h2) / 3;
                return new ScadBarSection("S3", a, iy, iz, j);
            }
            case "S6":
            {
                double d = P(2), di = P(3);
                if (!(d > 0) || !(di >= 0) || di >= d) return null;
                double i = Math.PI * (Math.Pow(d, 4) - Math.Pow(di, 4)) / 64;
                return new ScadBarSection("S6", Math.PI * (d * d - di * di) / 4, i, i, 2 * i);
            }
            default:
                return null;
        }
    }
}
