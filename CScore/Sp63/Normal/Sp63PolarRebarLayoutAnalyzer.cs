namespace CScore.Sp63.Normal;

/// <summary>
/// Проверяет, что продольная арматура равномерно распределена по окружности,
/// как требуют пп. Д.1 и Д.2 приложения Д СП 63.13330.2018.
/// </summary>
public static class Sp63PolarRebarLayoutAnalyzer
{
    /// <summary>Минимальное число стержней (примечание 1 приложения Д).</summary>
    public const int MinBarCount = 7;
    /// <summary>Допуск смещения центра раскладки в долях rs.</summary>
    public const double CenterTolerance = 0.02;
    /// <summary>Допуск различия площадей стержней.</summary>
    public const double AreaTolerance = 0.01;
    /// <summary>Допуск отклонения радиуса стержней от rs.</summary>
    public const double RadiusTolerance = 0.02;
    /// <summary>Допуск отклонения углового шага от 2π/n.</summary>
    public const double AngularStepTolerance = 0.05;

    /// <summary>Строит профиль полярной раскладки или возвращает причину неприменимости.</summary>
    public static Sp63CircularProfileAnalysis Analyze(CrossSection section, CalcType calc,
        double concreteCenterX, double concreteCenterY)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (!Sp63RebarBarCollector.TryCollectBars(section, calc, out var bars, out var message))
            return new Sp63CircularProfileAnalysis(null, [message!]);
        if (bars.Count < MinBarCount)
            return Fail("circular_insufficient_bars", "Д, примечание 1",
                "Sp63Normal_CircularInsufficientBars");

        double total = bars.Sum(bar => bar.Area);
        double cx = bars.Sum(bar => bar.X * bar.Area) / total;
        double cy = bars.Sum(bar => bar.Y * bar.Area) / total;
        var radii = bars
            .Select(bar => Math.Sqrt((bar.X - cx) * (bar.X - cx) + (bar.Y - cy) * (bar.Y - cy)))
            .ToList();
        double rs = radii.Average();
        if (!(rs > 0.0))
            return Fail("circular_rebar_not_on_circle", "Д", "Sp63Normal_CircularRebarNotOnCircle");

        double centerOffset = Math.Sqrt((cx - concreteCenterX) * (cx - concreteCenterX) +
                                        (cy - concreteCenterY) * (cy - concreteCenterY)) / rs;
        if (centerOffset > CenterTolerance)
            return Fail("circular_rebar_not_centered", "Д", "Sp63Normal_CircularRebarNotCentered");

        double meanArea = total / bars.Count;
        double areaDeviation = bars.Max(bar => Math.Abs(bar.Area - meanArea)) / meanArea;
        if (areaDeviation > AreaTolerance)
            return Fail("circular_rebar_unequal_areas", "Д", "Sp63Normal_CircularRebarUnequalAreas");

        double radiusDeviation = radii.Max(r => Math.Abs(r - rs)) / rs;
        if (radiusDeviation > RadiusTolerance)
            return Fail("circular_rebar_not_on_circle", "Д", "Sp63Normal_CircularRebarNotOnCircle");

        var angles = bars.Select(bar => Math.Atan2(bar.Y - cy, bar.X - cx)).OrderBy(a => a).ToList();
        double step = 2.0 * Math.PI / bars.Count;
        double stepDeviation = 0.0;
        for (int i = 0; i < angles.Count; i++)
        {
            // Замыкающий шаг — от последнего стержня к первому через 2π.
            double next = i + 1 < angles.Count ? angles[i + 1] : angles[0] + 2.0 * Math.PI;
            stepDeviation = Math.Max(stepDeviation, Math.Abs(next - angles[i] - step) / step);
        }
        if (stepDeviation > AngularStepTolerance)
            return Fail("circular_rebar_non_uniform", "Д", "Sp63Normal_CircularRebarNonUniform");

        var profile = new Sp63CircularRebarProfile(bars.Count, total, rs, bars[0].Rs,
            bars[0].Rsc, areaDeviation, radiusDeviation, stepDeviation, centerOffset,
            bars.Select(bar => (bar.X, bar.Y, bar.Area, bar.Diameter)).ToList());
        return new Sp63CircularProfileAnalysis(profile, []);
    }

    static Sp63CircularProfileAnalysis Fail(string code, string reference, string text) =>
        new(null, [new Sp63NormalMessage(code, Sp63NormalMessageKind.Applicability,
            reference, text)]);
}
