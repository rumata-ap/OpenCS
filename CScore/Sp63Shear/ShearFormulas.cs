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

    /// <summary>
    /// Поперечная сила, воспринимаемая бетоном (8.57), с отсечками, кН.
    /// При малом qsw применяется специальная формула Qb = 4·φb2·h0²·qsw/C.
    /// </summary>
    public static double ConcreteShear(
        ShearInclinedInput input, double projectionC, double phiN, double? appliedShear = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (projectionC <= 0.0) return 0.0;

        double baseValue = input.Rbt * input.B * input.H0;
        double numerator = PhiB2 * baseValue * input.H0;
        double threshold = 0.25 * input.Rbt * input.B;
        if (input.Qsw > 0.0 && input.Qsw < threshold && StirrupsMeetSpacing(input, appliedShear))
            numerator = 4.0 * PhiB2 * input.H0 * input.H0 * input.Qsw;

        double value = numerator / projectionC;
        double clamped = Math.Clamp(value, 0.5 * baseValue, 2.5 * baseValue);
        return phiN * clamped;
    }

    /// <summary>
    /// Поперечная сила, воспринимаемая хомутами (8.58), кН.
    /// При qsw ниже порога специальная формула относится к Qb, поэтому Qsw
    /// по-прежнему определяется как φsw·qsw·C.
    /// </summary>
    public static double StirrupShear(
        ShearInclinedInput input, double projectionC, double phiN, out string? note,
        double? appliedShear = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        note = null;
        if (input.Qsw <= 0.0) return 0.0;

        if (!StirrupsMeetSpacing(input, appliedShear))
        {
            double maxSpacing = MaxStirrupSpacing(input, appliedShear!.Value);
            note = $"Шаг хомутов превышает s_w,max = {maxSpacing:F3} м — "
                 + "поперечная арматура в расчёте не учтена (8.1.33).";
            return 0.0;
        }

        double threshold = 0.25 * input.Rbt * input.B;
        if (input.Qsw < threshold)
        {
            note = "qsw < 0,25·Rbt·b — для Qb применена специальная формула "
                 + "4·φb2·h0²·qsw/C (8.1.33).";
        }

        return PhiSw * input.Qsw * projectionC;
    }

    /// <summary>Минимальная поперечная сила, воспринимаемая бетоном (8.61), кН.</summary>
    public static double MinConcreteShear(
        ShearInclinedInput input, double phiN, double supportDistance)
    {
        ArgumentNullException.ThrowIfNull(input);
        double baseValue = 0.5 * input.Rbt * input.B * input.H0;
        double value = baseValue;
        if (supportDistance > 0.0 && supportDistance < 2.5 * input.H0)
            value *= 2.5 * input.H0 / supportDistance;

        value = Math.Min(value, 2.5 * input.Rbt * input.B * input.H0);
        return phiN * value;
    }

    /// <summary>
    /// Минимальная поперечная сила, воспринимаемая хомутами (8.62), кН.
    /// Хомуты, не удовлетворяющие условию qsw ≥ 0,25·Rbt·b, в упрощённое условие не входят:
    /// специальная формула относится только к Qb в (8.56).
    /// </summary>
    public static double MinStirrupShear(
        ShearInclinedInput input, double supportDistance, out string? note,
        double? appliedShear = null, double phiN = 1.0)
    {
        ArgumentNullException.ThrowIfNull(input);
        note = null;
        if (input.Qsw <= 0.0) return 0.0;

        if (!StirrupsMeetSpacing(input, appliedShear))
        {
            double maxSpacing = MaxStirrupSpacing(input, appliedShear!.Value);
            note = $"Шаг хомутов превышает s_w,max = {maxSpacing:F3} м — "
                 + "в упрощённом условии хомуты не учтены.";
            return 0.0;
        }

        if (input.Qsw < 0.25 * input.Rbt * input.B)
        {
            note = "qsw < 0,25·Rbt·b — в упрощённом условии хомуты не учтены "
                 + "(специальная формула применяется только к Qb в (8.56)).";
            return 0.0;
        }

        double value = input.Qsw * input.H0;
        if (supportDistance > 0.0 && supportDistance < input.H0)
            value *= supportDistance / input.H0;
        return phiN * value;
    }

    /// <summary>Проверяет ограничение шага хомутов из п. 8.1.33 для заданной Q.</summary>
    static bool StirrupsMeetSpacing(ShearInclinedInput input, double? appliedShear)
    {
        if (appliedShear is not double q || Math.Abs(q) <= 1e-12)
            return true;

        return input.Sw <= MaxStirrupSpacing(input, q) + 1e-12;
    }

    /// <summary>Максимальный шаг хомутов: sw,max = Rbt·b·h0²/Q.</summary>
    static double MaxStirrupSpacing(ShearInclinedInput input, double appliedShear) =>
        input.Rbt * input.B * input.H0 * input.H0 / Math.Abs(appliedShear);
}
