using System;

namespace CScore;

/// <summary>Расчёт ширины трещины по осреднённой деформации арматуры.</summary>
public static class CrackWidth8232
{
    /// <summary>
    /// Вычисляет ширину раскрытия по п. 8.2.15 с заменой σs/Es на εs,avg,
    /// полученную из деформационной модели п. 8.2.32. Коэффициент ψs здесь
    /// намеренно не применяется: его влияние уже учтено при решении равновесия.
    /// </summary>
    /// <param name="averageStrain">Осреднённая деформация растянутой арматуры.</param>
    /// <param name="crackSpacing">Базовое расстояние между трещинами, м.</param>
    /// <param name="phi1">Коэффициент продолжительности действия нагрузки.</param>
    /// <param name="phi2">Коэффициент профиля арматуры.</param>
    /// <param name="phi3">Коэффициент характера нагружения.</param>
    /// <returns>Ширина раскрытия трещины, м.</returns>
    public static double FromAverageStrain(
        double averageStrain,
        double crackSpacing,
        double phi1 = 1.0,
        double phi2 = 0.5,
        double phi3 = 1.0)
    {
        if (!double.IsFinite(averageStrain) || averageStrain < 0.0)
            throw new ArgumentOutOfRangeException(nameof(averageStrain));
        if (!double.IsFinite(crackSpacing) || crackSpacing < 0.0)
            throw new ArgumentOutOfRangeException(nameof(crackSpacing));
        if (!double.IsFinite(phi1) || !double.IsFinite(phi2) || !double.IsFinite(phi3)
            || phi1 < 0.0 || phi2 < 0.0 || phi3 < 0.0)
            throw new ArgumentOutOfRangeException(nameof(phi1));

        return phi1 * phi2 * phi3 * averageStrain * crackSpacing;
    }
}
