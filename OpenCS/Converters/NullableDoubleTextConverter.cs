using System.Globalization;
using System.Windows.Data;

namespace OpenCS.Converters;

/// <summary>Преобразует необязательное вещественное число в текст, принимая точку и запятую.</summary>
public sealed class NullableDoubleTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double number ? number.ToString("G", culture) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string;
        if (string.IsNullOrWhiteSpace(text)) return null!;

        var normalized = text.Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
               double.IsFinite(number)
            ? number
            : Binding.DoNothing;
    }
}
