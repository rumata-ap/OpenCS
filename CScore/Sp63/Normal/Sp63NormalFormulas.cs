namespace CScore.Sp63.Normal;

/// <summary>Результат ограничения высоты сжатой зоны в ветви растяжения.</summary>
/// <param name="RawX">Исходное значение x по формуле (8.25), м.</param>
/// <param name="UsedX">Значение x после нормативного ограничения, м.</param>
/// <param name="WasLimited">Применено ли ограничение x ≤ ξR·h0.</param>
public readonly record struct TensionXResult(double RawX, double UsedX, bool WasLimited);

/// <summary>Чистые числовые формулы упрощённой проверки нормального сечения.</summary>
public static class Sp63NormalFormulas
{
    /// <summary>Вычисляет относительную высоту сжатой зоны ξR.</summary>
    public static double XiR(double rs, double es, double epsilonB2)
    {
        EnsurePositive(rs, nameof(rs));
        EnsurePositive(es, nameof(es));
        EnsurePositive(epsilonB2, nameof(epsilonB2));
        return 0.8 / (1.0 + (rs / es) / epsilonB2);
    }

    /// <summary>Вычисляет x для изгиба по формулам (8.4)–(8.5), м.</summary>
    public static double BendingX(double rs, double asT, double rsc,
        double asC, double rb, double b) =>
        (rs * asT - rsc * asC) / PositiveProduct(rb, b);

    /// <summary>Вычисляет предельный момент Mult по формуле (8.5), кН·м.</summary>
    public static double BendingMoment(double rb, double b, double x,
        double h0, double rsc, double asC, double aPrime) =>
        rb * b * x * (h0 - 0.5 * x) + rsc * asC * (h0 - aPrime);

    /// <summary>
    /// Вычисляет момент для симметричной ветви (8.9); при исключённой сжатой
    /// арматуре и x &lt; 2a′ принимает a′eff = x/2.
    /// </summary>
    public static double SymmetricMoment(double rs, double asT, double h0,
        double aPrime, double xWithoutCompressionRebar,
        bool compressionRebarWasExcluded)
    {
        double effectiveAprime = compressionRebarWasExcluded &&
                                 xWithoutCompressionRebar < 2.0 * aPrime
            ? xWithoutCompressionRebar / 2.0
            : aPrime;
        return rs * asT * (h0 - effectiveAprime);
    }

    /// <summary>Вычисляет x по формуле (8.12) для ξ ≤ ξR, м.</summary>
    public static double CompressionXLowXi(double n, double rs, double asT,
        double rsc, double asC, double rb, double b) =>
        (n + rs * asT - rsc * asC) / PositiveProduct(rb, b);

    /// <summary>Вычисляет x по формуле (8.13) для ξ &gt; ξR, м.</summary>
    public static double CompressionXHighXi(double n, double rs, double asT,
        double rsc, double asC, double rb, double b, double h0, double xiR)
    {
        EnsurePositive(h0, nameof(h0));
        EnsureLessThanOne(xiR, nameof(xiR));
        double numerator = n + rs * asT * (1.0 + xiR) / (1.0 - xiR)
            - rsc * asC;
        double denominator = rb * b + 2.0 * rs * asT /
            (h0 * (1.0 - xiR));
        return numerator / PositiveValue(denominator, nameof(denominator));
    }

    /// <summary>Вычисляет эксцентриситет e по формуле (8.11), м.</summary>
    public static double CompressionE(double eta, double e0, double h0,
        double aPrime) => eta * e0 + (h0 - aPrime) / 2.0;

    /// <summary>Вычисляет несущую способность при центральном растяжении (8.19), кН.</summary>
    public static double CentralTensionCapacity(double rs, double asTotal) =>
        rs * asTotal;

    /// <summary>Вычисляет исходную высоту сжатой зоны по формуле (8.25), м.</summary>
    public static double TensionOutsideX(double rs, double asT, double rsc,
        double asC, double n, double rb, double b) =>
        (rs * asT - rsc * asC - n) / PositiveProduct(rb, b);

    /// <summary>Ограничивает x значением ξR·h0 в ветви внецентричного растяжения.</summary>
    public static TensionXResult LimitTensionX(double rawX, double xiR,
        double h0)
    {
        double limit = xiR * h0;
        bool limited = rawX > limit;
        return new TensionXResult(rawX, limited ? limit : rawX, limited);
    }

    static double PositiveProduct(double left, double right)
    {
        EnsurePositive(left, nameof(left));
        EnsurePositive(right, nameof(right));
        return left * right;
    }

    static double PositiveValue(double value, string name)
    {
        EnsurePositive(value, name);
        return value;
    }

    static void EnsurePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(name, "Значение должно быть положительным.");
    }

    static void EnsureLessThanOne(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0 || value >= 1)
            throw new ArgumentOutOfRangeException(name, "Значение должно быть в интервале (0; 1).");
    }
}
