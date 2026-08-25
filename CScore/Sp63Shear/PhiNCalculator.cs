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
    /// <summary>Предельное значение φn при сжатии.</summary>
    public const double MaxCompression = 2.25;

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
        double aRed = geometry.Ab * nu + geometry.AsTotal;
        if (aRed <= 0.0 || !double.IsFinite(aRed))
            return new PhiNResult(1.0, false,
                "φn = 1: не удалось вычислить приведённую площадь сечения.");

        double sigma = Math.Abs(n) / aRed;

        if (compression)
        {
            double value = Math.Min(1.0 + sigma / geometry.Rb, MaxCompression);
            return new PhiNResult(value, true,
                $"φn = 1 + σcp/Rb = {value:F3} по п. 8.1.34 (сжатие, σcp = {sigma:F1} кПа).");
        }

        double tensionValue = 1.0 - sigma / geometry.Rbt;
        return new PhiNResult(tensionValue, false,
            $"φn = 1 − σtp/Rbt = {tensionValue:F3} по п. 8.1.34 (растяжение, σtp = {sigma:F1} кПа); "
            + "к условию (8.55) не применяется.");
    }
}
