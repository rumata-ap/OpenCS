using System.Globalization;

namespace CScore.Batch;

/// <summary>Числа для текста причины — по-русски (десятичная запятая), как тело отчёта.</summary>
public static class BatchVerdictText
{
    static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>Значащие цифры (деформации, усилия, невязки).</summary>
    public static string N(double v, int sig = 4) => v.ToString("G" + sig, Ru);

    /// <summary>Три знака после запятой (Кисп, мм); +∞ — «∞».</summary>
    public static string F3(double v) => double.IsPositiveInfinity(v) ? "∞" : v.ToString("F3", Ru);
}
