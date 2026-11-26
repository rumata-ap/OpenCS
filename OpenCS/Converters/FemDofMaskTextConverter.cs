using System.Globalization;
using System.Windows.Data;
using CScore.Fem;

namespace OpenCS.Converters;

/// <summary>Маска закреплений (биты 0–5) → «X Y Z UX UY UZ» для таблиц; без закреплений — пустая строка.</summary>
public sealed class FemDofMaskTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int mask && (mask & FemBoundaryDofs.All) != 0 ? FemBoundaryDofs.Describe(mask & FemBoundaryDofs.All) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
