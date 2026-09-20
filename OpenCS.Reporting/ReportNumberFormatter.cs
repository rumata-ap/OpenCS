using System.Globalization;

namespace OpenCS.Reporting;

/// <summary>Единица измерения значения, отображаемого в отчёте.</summary>
public enum ReportUnit
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

    /// <summary>Относительная деформация.</summary>
    Strain,

    /// <summary>Количество.</summary>
    Count
}

/// <summary>Профиль округления числового значения отчёта.</summary>
public sealed record ReportNumberProfile(int DecimalPlaces)
{
    /// <summary>Создаёт профиль с указанным количеством знаков после запятой.</summary>
    public static ReportNumberProfile Decimal(int decimalPlaces)
        => new(Math.Clamp(decimalPlaces, 0, 12));
}

/// <summary>Единый форматтер чисел для формул, таблиц и подписей схем.</summary>
public static class ReportNumberFormatter
{
    /// <summary>Замена для отсутствующего или нечислового значения.</summary>
    public const string UndefinedPlaceholder = "—";

    /// <summary>Форматирует число без изменения его расчётного значения.</summary>
    public static string Format(double value, ReportUnit unit,
        ReportNumberProfile? profile = null)
    {
        _ = unit;
        if (!double.IsFinite(value))
            return UndefinedPlaceholder;

        int decimalPlaces = profile?.DecimalPlaces ?? DefaultDecimalPlaces(unit);
        double rounded = Math.Round(value, decimalPlaces, MidpointRounding.AwayFromZero);
        if (Math.Abs(rounded) < Math.Pow(10.0, -decimalPlaces) / 2.0)
            rounded = 0.0;
        return TrimZeros(rounded, decimalPlaces);
    }

    /// <summary>
    /// Форматирует коэффициент использования вверх. Статус проверки этим методом
    /// не определяется и должен быть передан отдельно из исходного результата.
    /// </summary>
    public static string FormatUtilization(double value)
    {
        if (!double.IsFinite(value))
            return UndefinedPlaceholder;

        int decimalPlaces = Math.Abs(value - 1.0) < 0.005 ? 4 : 3;
        decimal scale = Pow10(decimalPlaces);
        decimal rounded;
        try
        {
            rounded = Math.Ceiling((decimal)value * scale) / scale;
        }
        catch (OverflowException)
        {
            return Format(value, ReportUnit.Unitless,
                ReportNumberProfile.Decimal(decimalPlaces));
        }
        return TrimZeros((double)rounded, decimalPlaces);
    }

    static int DefaultDecimalPlaces(ReportUnit unit) => unit switch
    {
        ReportUnit.Meter => 3,
        ReportUnit.Millimeter => 0,
        ReportUnit.Centimeter => 1,
        ReportUnit.Kilonewton => 2,
        ReportUnit.KilonewtonMeter => 2,
        ReportUnit.Megapascal => 1,
        ReportUnit.Kilopascal => 1,
        ReportUnit.SquareCentimeter => 2,
        ReportUnit.SquareMeter => 4,
        ReportUnit.Strain => 6,
        ReportUnit.Count => 0,
        _ => 3
    };

    static decimal Pow10(int decimalPlaces)
        => decimalPlaces == 0 ? 1m : (decimal)Math.Pow(10, decimalPlaces);

    static string TrimZeros(double value, int decimalPlaces)
    {
        string text = value.ToString($"F{decimalPlaces}", CultureInfo.InvariantCulture);
        if (text.Contains('.', StringComparison.Ordinal))
            text = text.TrimEnd('0').TrimEnd('.');
        return text == "-0" ? "0" : text;
    }
}
