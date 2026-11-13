using System.Globalization;

namespace CScore.Batch;

/// <summary>Числа для текста причины — по-русски (десятичная запятая), как тело отчёта.</summary>
public static class BatchVerdictText
{
    static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>
    /// Значащие цифры (деформации, усилия, невязки) без экспоненты: 2850 вместо «2,85E+03», 0,0041 вместо «4,1E-03».
    /// Экспонента остаётся только у чисел вне 1e-12…1e15 — там обычная запись нечитаема.
    /// </summary>
    public static string N(double v, int sig = 4) => N(v, sig, Ru);

    /// <summary>
    /// То же в заданной культуре: отчёты одиночных задач пишут с точкой (InvariantCulture),
    /// пакетные причины — с запятой.
    /// </summary>
    public static string N(double v, int sig, IFormatProvider culture)
    {
        if (!double.IsFinite(v) || v == 0) return v.ToString("G" + sig, culture);
        double abs = Math.Abs(v);
        if (abs < 1e-12 || abs >= 1e15) return v.ToString("G" + sig, culture);
        int digits = sig - 1 - (int)Math.Floor(Math.Log10(abs));
        double rounded = digits >= 0
            ? Math.Round(v, Math.Min(digits, 15), MidpointRounding.AwayFromZero)
            : Math.Round(v / Math.Pow(10, -digits), MidpointRounding.AwayFromZero) * Math.Pow(10, -digits);
        return rounded.ToString("0.###############", culture);
    }

    /// <summary>Три знака после запятой (Кисп, мм); +∞ — «∞».</summary>
    public static string F3(double v) => double.IsPositiveInfinity(v) ? "∞" : v.ToString("F3", Ru);
}
