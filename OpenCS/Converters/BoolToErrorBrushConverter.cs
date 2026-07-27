using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OpenCS.Converters;

public class BoolToErrorBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Brushes.Firebrick : Brushes.DarkGoldenrod;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
