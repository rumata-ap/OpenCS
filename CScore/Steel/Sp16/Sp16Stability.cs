namespace CScore.Sp16;

/// <summary>Коэффициенты устойчивости по СП 16.13330.2017 (п. 7.1.3, изм. № 6).</summary>
public static class Sp16Stability
{
    /// <summary>Условная гибкость λ̄ = λ·√(Ry/E) (к формуле (9)).</summary>
    public static double LambdaBar(double lambda, double ry, double e) => lambda * Math.Sqrt(ry / e);

    /// <summary>Коэффициенты α, β табл. 7.</summary>
    public static (double Alpha, double Beta) Table7(SectionCurve curve) => curve switch
    {
        SectionCurve.a => (0.03, 0.06),
        SectionCurve.b => (0.04, 0.09),
        _ => (0.04, 0.14),
    };

    /// <summary>
    /// Коэффициент устойчивости при центральном сжатии φ по формулам (8), (9):
    /// φ = 0,5(δ − √(δ² − 39,48λ̄²))/λ̄², δ = 9,87(1 − α + βλ̄) + λ̄²; φ = 1 при λ̄ &lt; 0,6 для типов a и b;
    /// φ ≤ 7,6/λ̄² при λ̄ &gt; 3,8; 4,4; 5,8 для типов a, b, c.
    /// </summary>
    public static double Phi(double lambdaBar, SectionCurve curve)
    {
        if (lambdaBar < 0.6 && curve != SectionCurve.c) return 1.0;
        if (lambdaBar < 1e-6) return 1.0;
        var (alpha, beta) = Table7(curve);
        double l2 = lambdaBar * lambdaBar;
        double delta = 9.87 * (1 - alpha + beta * lambdaBar) + l2;
        double phi = 0.5 * (delta - Math.Sqrt(delta * delta - 39.48 * l2)) / l2;
        double limit = curve switch { SectionCurve.a => 3.8, SectionCurve.b => 4.4, _ => 5.8 };
        // Табл. Д.1 применяет ограничение уже на границе (λ̄ = 3,8 для типа a: 7,6/3,8² = 0,526).
        if (lambdaBar >= limit - 1e-9) phi = Math.Min(phi, 7.6 / l2);
        return Math.Min(phi, 1.0);
    }

    /// <summary>
    /// Коэффициент учёта распределения остаточных напряжений γres для формулы (7а) (изм. № 6):
    /// С255 (Ry = 245 МПа): 1 при λ̄x ≤ 3, 0,1λ̄x + 0,7 при λ̄x &gt; 3; С390 (Ry = 390 МПа): 1 при λ̄x ≤ 5,
    /// 0,2λ̄x + 0,9 при λ̄x &gt; 5; промежуточные Ry — интерполяция; Ry &gt; 390 — как 390, Ry &lt; 245 — как 245.
    /// </summary>
    /// <remarks>
    /// Ветка С390 при λ̄x &gt; 5 даёт скачок γres с 1,0 до 1,9 (опечатка; в тексте Кодекса сноска
    /// на письмо ФАУ «ФЦС» от 10.04.2025 № Исх-2490, текст которого недоступен). Эту ветку не
    /// применяем: γres = 1 и <paramref name="note"/> с предупреждением. Интерполяция по Ry
    /// выполняется между значением для С255 и значением 1 (С390 при λ̄x ≤ 5).
    /// </remarks>
    public static double GammaRes(double lambdaBarX, double ryKpa, out string? note)
    {
        note = null;
        double ryMpa = ryKpa / 1000.0;
        double g245 = lambdaBarX <= 3 ? 1.0 : 0.1 * lambdaBarX + 0.7;
        if (lambdaBarX > 5 && ryMpa > 245)
        {
            note = "γres для С390 при λ̄x > 5 по формуле (7а) даёт скачок 1→1,9 (опечатка нормы); принято γres = 1";
            return 1.0;
        }
        double g390 = 1.0;
        if (ryMpa <= 245) return g245;
        if (ryMpa >= 390) return g390;
        double t = (ryMpa - 245) / (390 - 245);
        return g245 + t * (g390 - g245);
    }
}
