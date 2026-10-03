using CScore.Sp16;

namespace CScore.Import;

/// <summary>
/// Параметры проверки по СП 16 стального КЭ импортированной схемы из его стальной группы (SCAD): поверх базовых
/// параметров проверки переопределяются γc, расчётные длины lef,x = μXoZ·l, lef,y = μYoZ·l (или заданные),
/// lef,b (шаг раскреплений из плоскости) и предельные гибкости. Применяется только в проверке по КЭ — у проверки
/// конструктивного элемента параметры свои.
/// </summary>
public static class ImportedSteelDesign
{
    /// <summary>Параметры КЭ: базовые с переопределениями группы.</summary>
    /// <param name="baseParams">Параметры проверки.</param>
    /// <param name="g">Стальная группа КЭ.</param>
    /// <param name="length">Длина для коэффициентов расчётной длины, м (<see cref="Length"/>).</param>
    public static SteelDesignParams Apply(SteelDesignParams baseParams, ScadSteelGroup g, double length)
    {
        double lefX = g.LengthXoZ ?? g.MuXoZ * length;
        double lefY = g.LengthYoZ ?? g.MuYoZ * length;
        double lefB = g.StepOutPlane ?? g.StepOutPlaneRatio * length;
        string source = $"стальная группа SCAD {ScadSteelGroupIndex.Label(g)}";
        return baseParams with
        {
            GammaC = Positive(g.GammaC) ?? baseParams.GammaC,
            LefX = Positive(lefX) ?? baseParams.LefX,
            LefY = Positive(lefY) ?? baseParams.LefY,
            LefB = Positive(lefB) ?? baseParams.LefB,
            CompressionLimit = g.CompressionLimit > 0
                ? new SlendernessLimit(g.CompressionLimit, g.CompressionLimitAlpha, source) : baseParams.CompressionLimit,
            TensionLimit = g.TensionLimit > 0 ? new SlendernessLimit(g.TensionLimit, 0, source) : baseParams.TensionLimit,
        };
    }

    /// <summary>
    /// Длина для коэффициентов расчётной длины: у группы элементов — длина КЭ, у конструктивного элемента SCAD —
    /// сумма длин КЭ группы (до сверки с SCAD). null — длина неизвестна (нет узлов КЭ).
    /// </summary>
    /// <param name="elementLength">Длина КЭ по номеру, м; null — неизвестна.</param>
    public static double? Length(ScadSteelGroup g, int elementId, Func<int, double?> elementLength)
    {
        if (!g.IsMember) return elementLength(elementId);
        double sum = 0;
        foreach (int id in g.ElementIds.Distinct())
        {
            if (elementLength(id) is not double l) return null;
            sum += l;
        }
        return sum;
    }

    static double? Positive(double v) => double.IsFinite(v) && v > 0 ? v : null;
}
