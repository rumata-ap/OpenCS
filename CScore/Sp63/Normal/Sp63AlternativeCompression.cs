using System.Text.RegularExpressions;

namespace CScore.Sp63.Normal;

/// <summary>
/// Альтернативный (упрощённый) расчёт внецентренно сжатых прямоугольных сечений
/// по п. 8.1.16 СП 63.13330.2018: N ≤ Nult = φ·(Rb·A + Rsc·As,tot) (8.16)–(8.17)
/// при e0 ≤ h/30 и l0/h ≤ 20. Справочная проверка, в вердикт прочности не входит.
/// </summary>
public static partial class Sp63AlternativeCompression
{
    /// <summary>Узлы l0/h таблицы 8.1.</summary>
    static readonly double[] SlendernessNodes = [6.0, 10.0, 15.0, 20.0];

    /// <summary>Строки таблицы 8.1: B20–B55, B60, B80 (длительное действие нагрузки).</summary>
    static readonly double[] RowB20ToB55 = [0.92, 0.90, 0.83, 0.70];
    static readonly double[] RowB60 = [0.91, 0.89, 0.80, 0.65];
    static readonly double[] RowB80 = [0.90, 0.88, 0.79, 0.64];

    /// <summary>Предельное отношение l0/h по п. 8.1.16.</summary>
    public const double MaxSlenderness = 20.0;

    /// <summary>
    /// Коэффициент φ при длительном действии нагрузки по таблице 8.1 с линейной
    /// интерполяцией по l0/h; при l0/h &lt; 6 принимается значение для l0/h = 6.
    /// Возвращает null для классов бетона, отсутствующих в таблице
    /// (ниже B20, B65–B75, выше B80), и для l0/h &gt; 20.
    /// </summary>
    public static double? PhiLongTerm(int concreteClass, double l0OverH)
    {
        if (!double.IsFinite(l0OverH) || l0OverH < 0 || l0OverH > MaxSlenderness)
            return null;
        double[]? row = concreteClass switch
        {
            >= 20 and <= 55 => RowB20ToB55,
            60 => RowB60,
            80 => RowB80,
            _ => null
        };
        return row is null ? null : Interpolate(row, l0OverH);
    }

    /// <summary>
    /// Коэффициент φ при кратковременном действии нагрузки: линейно по l0/h между
    /// φ = 0,9 при l0/h = 10 и φ = 0,85 при l0/h = 20; при l0/h &lt; 10 — 0,9
    /// (без экстраполяции выше табличного значения). Для l0/h &gt; 20 — null.
    /// </summary>
    public static double? PhiShortTerm(double l0OverH)
    {
        if (!double.IsFinite(l0OverH) || l0OverH < 0 || l0OverH > MaxSlenderness)
            return null;
        if (l0OverH <= 10.0) return 0.9;
        return 0.9 - 0.05 * (l0OverH - 10.0) / 10.0;
    }

    /// <summary>Предельная продольная сила по формуле (8.17), в единицах Rb·A.</summary>
    public static double UltimateForce(double phi, double rb, double concreteArea,
        double rsc, double totalRebarArea) =>
        phi * (rb * concreteArea + rsc * totalRebarArea);

    /// <summary>
    /// Извлекает класс бетона по прочности на сжатие из метки материала
    /// («B25», «В30», «Бетон B20» — латинская или кириллическая «B»). null — не распознан.
    /// </summary>
    public static int? ParseConcreteClass(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var match = ClassRegex().Match(tag);
        return match.Success && int.TryParse(match.Groups[1].Value, out int value)
            ? value
            : null;
    }

    static double Interpolate(double[] row, double l0OverH)
    {
        if (l0OverH <= SlendernessNodes[0]) return row[0];
        for (int i = 1; i < SlendernessNodes.Length; i++)
        {
            if (l0OverH > SlendernessNodes[i]) continue;
            double t = (l0OverH - SlendernessNodes[i - 1]) /
                       (SlendernessNodes[i] - SlendernessNodes[i - 1]);
            return row[i - 1] + t * (row[i] - row[i - 1]);
        }
        return row[^1];
    }

    [GeneratedRegex(@"(?<![A-Za-zА-Яа-я])[BВ]\s*(\d{1,3})(?:[.,]\d+)?(?!\d)")]
    private static partial Regex ClassRegex();
}
