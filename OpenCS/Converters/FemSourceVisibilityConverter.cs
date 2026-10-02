using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OpenCS.Converters;

/// <summary>
/// Видимость пункта меню по программе-источнику расчётной схемы. Значения: [0] — AppViewModel,
/// [1] — цель (FemSchema, группа КЭ или конструктивный элемент); параметр — источник ("lira", "scad" …).
/// Пункт виден, только если схема цели пришла из этой программы.
/// </summary>
public sealed class FemSourceVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length >= 2 && values[0] is AppViewModel app && parameter is string source
            && string.Equals(app.FemSourceTypeOf(values[1]), source, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
