namespace CScore.Sp63Shear;

/// <summary>Результат вычисления коэффициента φn.</summary>
/// <param name="Value">Значение коэффициента.</param>
/// <param name="AppliesToStrip">Применяется ли φn к условию (8.55) по бетонной полосе.</param>
/// <param name="Explanation">Обоснование для отчёта.</param>
public readonly record struct PhiNResult(double Value, bool AppliesToStrip, string Explanation);

/// <summary>
/// Вычисляет коэффициент φn по п. 8.1.34 СП 63.13330, учитывающий влияние продольной силы
/// на прочность по наклонным сечениям.
/// </summary>
public static class PhiNCalculator
{
    /// <summary>Наибольшее значение φn при сжатии (полка 0,25·Rb ≤ σcp ≤ 0,5·Rb).</summary>
    public const double MaxCompression = 1.25;

    /// <summary>
    /// φn по σcp при сжатии (п. 8.1.34 в ред. Изм. № 2): 1 + σcp/Rb при σcp ≤ 0,25·Rb;
    /// 1,25 при 0,25·Rb &lt; σcp ≤ 0,5·Rb; 2,5·(1 − σcp/Rb) ≥ 0 при σcp &gt; 0,5·Rb.
    /// </summary>
    public static double CompressionValue(double sigmaCp, double rb)
    {
        double ratio = sigmaCp / rb;
        if (ratio <= 0.25) return 1.0 + ratio;
        if (ratio <= 0.5) return MaxCompression;
        return Math.Max(2.5 * (1.0 - ratio), 0.0);
    }

    /// <summary>φn по σtp при растяжении (п. 8.1.34 в ред. Изм. № 2): 1 − σtp/(2·Rbt) ≥ 0.</summary>
    public static double TensionValue(double sigmaTp, double rbt) =>
        Math.Max(1.0 - sigmaTp / (2.0 * rbt), 0.0);

    /// <summary>Вычисляет φn для заданных типа элемента и продольной силы (сжатие — «минус»).</summary>
    public static PhiNResult Compute(ElementKind kind, double n, InclinedSectionGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        if (kind == ElementKind.BendingUnstressed)
            return new PhiNResult(1.0, false,
                "φn = 1 по п. 8.1.34: изгибаемый элемент без предварительного напряжения.");

        if (Math.Abs(n) < 1e-12)
            return new PhiNResult(1.0, false, "φn = 1: продольная сила отсутствует.");

        bool compression = n < 0.0;
        double nu = compression
            ? geometry.Rb / (geometry.Eb0 * geometry.Eb)
            : geometry.Rbt / (geometry.Ebt0 * geometry.Eb);
        double aRed = geometry.Ab + geometry.Es / geometry.Eb / nu * geometry.AsTotal;
        if (aRed <= 0.0 || !double.IsFinite(aRed))
            return new PhiNResult(1.0, false,
                "φn = 1: не удалось вычислить приведённую площадь сечения.");

        double sigma = Math.Abs(n) / aRed;

        if (compression)
        {
            double value = CompressionValue(sigma, geometry.Rb);
            double ratio = sigma / geometry.Rb;
            string branch = ratio <= 0.25 ? "1 + σcp/Rb"
                : ratio <= 0.5 ? "1,25 (0,25·Rb < σcp ≤ 0,5·Rb)"
                : "2,5·(1 − σcp/Rb) ≥ 0";
            return new PhiNResult(value, true,
                $"φn = {branch} = {value:F3} по п. 8.1.34 (сжатие, σcp = {sigma:F1} кПа).");
        }

        double tensionValue = TensionValue(sigma, geometry.Rbt);
        return new PhiNResult(tensionValue, false,
            $"φn = 1 − σtp/(2·Rbt) ≥ 0 = {tensionValue:F3} по п. 8.1.34 (растяжение, σtp = {sigma:F1} кПа); "
            + "к условию (8.55) не применяется.");
    }
}
