namespace CScore.CalculationTrace;

/// <summary>Машинная единица числового значения расчётной трассировки.</summary>
public enum CalculationUnit
{
    /// <summary>Безразмерная величина.</summary>
    Unitless,

    /// <summary>Сантиметр.</summary>
    Centimeter,

    /// <summary>Миллиметр.</summary>
    Millimeter,

    /// <summary>Метр.</summary>
    Meter,

    /// <summary>Килоньютон.</summary>
    Kilonewton,

    /// <summary>Килоньютон-метр.</summary>
    KilonewtonMeter,

    /// <summary>Мегапаскаль.</summary>
    Megapascal,

    /// <summary>Килопаскаль.</summary>
    Kilopascal,

    /// <summary>Квадратный сантиметр.</summary>
    SquareCentimeter,

    /// <summary>Квадратный метр.</summary>
    SquareMeter,

    /// <summary>Килограмм-сила на квадратный сантиметр.</summary>
    KilogramForcePerSquareCentimeter,

    /// <summary>Процент деформации или относительная деформация.</summary>
    Strain,

    /// <summary>Целое количество.</summary>
    Count
}
