using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OpenCS.Converters;

/// <summary>null → Visible, не-null → Collapsed. Для плейсхолдера «ничего не выбрано».</summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value == null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>null → Collapsed, не-null → Visible. Для панели редактирования выбранного элемента.</summary>
public class NullToCollapsedVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value == null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
