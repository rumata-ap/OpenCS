using System.Globalization;
using System.Windows;
using System.Windows.Data;
using CScore.Fem;

namespace OpenCS.Converters;

/// <summary>Видимость пункта меню только у схемы-фрагмента этажа (<see cref="FemSchema.ParentSchemaId"/> задан).</summary>
public sealed class FemFragmentVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is FemSchema { ParentSchemaId: not null } ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
