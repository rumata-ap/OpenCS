namespace CScore.Sp63Shear;

/// <summary>
/// Формулы прочности наклонного сечения на действие момента, п. 8.1.35 СП 63.13330.
/// Усилие в хомутах здесь принимается равным qsw·C — без коэффициента φsw,
/// в отличие от расчёта на поперечную силу.
/// </summary>
public static class MomentFormulas
{
    /// <summary>Момент, воспринимаемый продольной арматурой (8.64), кН·м.</summary>
    public static double LongitudinalMoment(ShearInclinedInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.AnchorageFactor * input.Ns * ShearFormulas.Zs * input.H0;
    }

    /// <summary>Момент, воспринимаемый хомутами (8.65), кН·м.</summary>
    public static double StirrupMoment(ShearInclinedInput input, double projectionC)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Qsw <= 0.0 || input.Sw > 0.5 * input.H0) return 0.0;
        return 0.5 * input.Qsw * projectionC * projectionC;
    }

    /// <summary>Момент хомутов в упрощённом варианте 8.1.35: 0,5·qsw·h0², кН·м.</summary>
    public static double SimplifiedStirrupMoment(ShearInclinedInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Qsw <= 0.0 || input.Sw > 0.5 * input.H0) return 0.0;
        return 0.5 * input.Qsw * input.H0 * input.H0;
    }
}
