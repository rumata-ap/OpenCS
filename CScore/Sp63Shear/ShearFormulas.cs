namespace CScore.Sp63Shear;

/// <summary>
/// Формулы прочности наклонных сечений по поперечной силе, пп. 8.1.32–8.1.33 СП 63.13330.
/// </summary>
public static class ShearFormulas
{
    /// <summary>Коэффициент φb1 условия (8.55).</summary>
    public const double PhiB1 = 0.3;

    /// <summary>Коэффициент φb2 формулы (8.57).</summary>
    public const double PhiB2 = 1.5;

    /// <summary>Коэффициент φsw формулы (8.58).</summary>
    public const double PhiSw = 0.75;

    /// <summary>Относительное плечо внутренней пары сил zs = 0,9·h0.</summary>
    public const double Zs = 0.9;

    /// <summary>Несущая способность бетонной полосы между наклонными сечениями (8.55), кН.</summary>
    public static double StripCapacity(ShearInclinedInput input, double phiN, bool applyToStrip)
    {
        ArgumentNullException.ThrowIfNull(input);
        double factor = applyToStrip ? phiN : 1.0;
        return factor * PhiB1 * input.Rb * input.B * input.H0;
    }

    /// <summary>Поперечная сила, воспринимаемая бетоном (8.57), с отсечками, кН.</summary>
    public static double ConcreteShear(ShearInclinedInput input, double projectionC, double phiN)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (projectionC <= 0.0) return 0.0;

        double baseValue = input.Rbt * input.B * input.H0;
        double value = PhiB2 * baseValue * input.H0 / projectionC;
        double clamped = Math.Clamp(value, 0.5 * baseValue, 2.5 * baseValue);
        return phiN * clamped;
    }

    /// <summary>
    /// Поперечная сила, воспринимаемая хомутами (8.58), кН. При qsw ниже порога
    /// 0,25·Rbt·b применяется формула с корнем; при шаге свыше 0,5·h0 усилие не учитывается.
    /// </summary>
    public static double StirrupShear(
        ShearInclinedInput input, double projectionC, double phiN, out string? note)
    {
        ArgumentNullException.ThrowIfNull(input);
        note = null;
        if (input.Qsw <= 0.0) return 0.0;

        if (input.Sw > 0.5 * input.H0)
        {
            note = "Шаг хомутов превышает 0,5·h0 — поперечная арматура в расчёте не учтена (8.1.33).";
            return 0.0;
        }

        double threshold = 0.25 * input.Rbt * input.B;
        if (input.Qsw < threshold)
        {
            note = "qsw < 0,25·Rbt·b — усилие хомутов принято по формуле с корнем (8.1.33).";
            // Ограничивается только сверху: нижняя отсечка 0,5·Rbt·b·h0 — это гарантированный
            // вклад бетона в (8.57), и применение её к слагаемому хомутов подняло бы
            // несущую способность за счёт чужого члена.
            double upperLimit = 2.5 * input.Rbt * input.B * input.H0;
            double value = Math.Sqrt(PhiB2 * PhiSw * input.Rbt * input.B * input.Qsw) * input.H0;
            return phiN * Math.Min(value, upperLimit);
        }

        return PhiSw * input.Qsw * projectionC;
    }

    /// <summary>Минимальная поперечная сила, воспринимаемая бетоном (8.61), кН.</summary>
    public static double MinConcreteShear(
        ShearInclinedInput input, double phiN, double supportDistance)
    {
        ArgumentNullException.ThrowIfNull(input);
        double value = phiN * 0.5 * input.Rbt * input.B * input.H0;
        if (supportDistance > 0.0 && supportDistance < input.H0)
            value *= Math.Min(supportDistance, 2.0 * input.H0) / input.H0;
        return value;
    }

    /// <summary>
    /// Минимальная поперечная сила, воспринимаемая хомутами (8.62), кН.
    /// Хомуты, не удовлетворяющие условию qsw ≥ 0,25·Rbt·b, в упрощённое условие не входят:
    /// допущение с корнем относится только к Qsw в (8.56).
    /// </summary>
    public static double MinStirrupShear(
        ShearInclinedInput input, double supportDistance, out string? note)
    {
        ArgumentNullException.ThrowIfNull(input);
        note = null;
        if (input.Qsw <= 0.0) return 0.0;

        if (input.Sw > 0.5 * input.H0)
        {
            note = "Шаг хомутов превышает 0,5·h0 — в упрощённом условии хомуты не учтены.";
            return 0.0;
        }

        if (input.Qsw < 0.25 * input.Rbt * input.B)
        {
            note = "qsw < 0,25·Rbt·b — в упрощённом условии хомуты не учтены "
                 + "(допущение с корнем применимо только к Qsw в (8.56)).";
            return 0.0;
        }

        double value = PhiSw * input.Qsw * input.H0;
        if (supportDistance > 0.0 && supportDistance < input.H0)
            value *= supportDistance / input.H0;
        return value;
    }
}
