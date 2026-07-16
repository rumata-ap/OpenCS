using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OpenCS.Views.Helpers;

/// <summary>Красный для ошибок (IsError=true), тёмно-жёлтый для предупреждений.</summary>
public sealed class DiagnosticColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Brushes.Crimson : Brushes.DarkGoldenrod;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
