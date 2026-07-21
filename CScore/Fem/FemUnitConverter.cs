namespace CScore.Fem;

/// <summary>Преобразования пользовательских единиц КЭ-схемы в базовые единицы расчёта.</summary>
public static class FemUnitConverter
{
    const double Kilo = 1000.0;

    /// <summary>Переводит силу из кН в Н.</summary>
    public static double KiloNewtonsToNewtons(double value) => value * Kilo;

    /// <summary>Переводит силу из Н в кН.</summary>
    public static double NewtonsToKiloNewtons(double value) => value / Kilo;

    /// <summary>Переводит момент из кН·м в Н·м.</summary>
    public static double KiloNewtonMetersToNewtonMeters(double value) => value * Kilo;

    /// <summary>Переводит момент из Н·м в кН·м.</summary>
    public static double NewtonMetersToKiloNewtonMeters(double value) => value / Kilo;

    /// <summary>Переводит жёсткость GJ из кН·м² в Н·м².</summary>
    public static double KiloNewtonMetersSquaredToNewtonMetersSquared(double value) => value * Kilo;

    /// <summary>Переводит жёсткость GJ из Н·м² в кН·м².</summary>
    public static double NewtonMetersSquaredToKiloNewtonMetersSquared(double value) => value / Kilo;
}
