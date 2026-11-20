using System.Globalization;
using System.Windows.Data;
using CScore;

namespace OpenCS.Utilites;

/// <summary>
/// Подпись значения перечисления в выпадающем списке: тип материала и влажность среды — словами через Loc,
/// а не именами из кода («ReSteelF», «от40_до70»).
/// </summary>
public sealed class EnumTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        Dampness.ниже_40 => Loc.S("HumidityBelow40"),
        Dampness.от40_до70 => Loc.S("Humidity40_75"),
        Dampness.свыше_70 => Loc.S("HumidityAbove75"),
        Dampness.any => Loc.S("HumidityAny"),
        MatType type => Loc.S("MatType_" + type),
        _ => value?.ToString() ?? ""
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
