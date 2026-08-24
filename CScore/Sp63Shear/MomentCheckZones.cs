namespace CScore.Sp63Shear;

/// <summary>
/// Определяет, попадает ли стоянка в зону, где п. 8.1.35 предписывает проверку по моменту:
/// концевые участки элемента и окрестности обрывов продольной арматуры.
/// </summary>
public static class MomentCheckZones
{
    /// <summary>Полуширина окрестности обрыва арматуры в долях рабочей высоты.</summary>
    public const double CutoffHalfWidthFactor = 2.0;

    /// <summary>Проверяет попадание стоянки в предписанную нормой зону.</summary>
    public static bool IsInZone(double station, ShearInclinedInput input, IForceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(profile);

        double zone = input.MomentZoneLength > 0.0
            ? input.MomentZoneLength
            : 2.0 * input.H0;

        var (min, max) = profile.StationRange;
        if (max - min <= 1e-9) return true;                       // единственная стоянка

        if (station - min <= zone || max - station <= zone) return true;

        double halfWidth = CutoffHalfWidthFactor * input.H0;
        foreach (double cutoff in input.BarCutoffs)
            if (Math.Abs(station - cutoff) <= halfWidth) return true;

        return false;
    }
}
