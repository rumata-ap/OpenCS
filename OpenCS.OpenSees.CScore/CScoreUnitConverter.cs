using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.CScore;

/// <summary>Преобразования числовых величин CScore в единицы SI backend.</summary>
public static class CScoreUnitConverter
{
    /// <summary>Переводит мегапаскали в паскали.</summary>
    public static double MegapascalsToPascals(double megapascals) =>
        RequireFinite(megapascals, nameof(megapascals)) * 1_000_000d;

    /// <summary>Переводит килопаскали, используемые CScore, в паскали.</summary>
    public static double KilopascalsToPascals(double kilopascals) =>
        RequireFinite(kilopascals, nameof(kilopascals)) * 1_000d;

    /// <summary>Переводит килоньютоны в ньютоны.</summary>
    public static double KiloNewtonsToNewtons(double kiloNewtons) =>
        RequireFinite(kiloNewtons, nameof(kiloNewtons)) * 1_000d;

    /// <summary>Переводит килоньютон-метры в ньютон-метры.</summary>
    public static double KiloNewtonMetersToNewtonMeters(double kiloNewtonMeters) =>
        RequireFinite(kiloNewtonMeters, nameof(kiloNewtonMeters)) * 1_000d;

    /// <summary>Преобразует координаты CScore X/Y в явные координаты OpenSees Y/Z.</summary>
    public static (double Y, double Z) ToOpenSeesCoordinates(
        double cscoreX,
        double cscoreY,
        OpenSeesCoordinateConvention convention)
    {
        ArgumentNullException.ThrowIfNull(convention);
        RequireFinite(cscoreX, nameof(cscoreX));
        RequireFinite(cscoreY, nameof(cscoreY));

        return (
            CoordinateValue(convention.YFrom, cscoreX, cscoreY),
            CoordinateValue(convention.ZFrom, cscoreX, cscoreY));
    }

    private static double CoordinateValue(
        OpenSeesCoordinateSource source,
        double cscoreX,
        double cscoreY) => source switch
        {
            OpenSeesCoordinateSource.CScoreX => cscoreX,
            OpenSeesCoordinateSource.CScoreY => cscoreY,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Неизвестный источник координаты.")
        };

    private static double RequireFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Значение должно быть конечным.");
        }

        return value;
    }
}
