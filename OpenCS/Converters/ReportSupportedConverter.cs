using System.Globalization;
using System.Windows;
using System.Windows.Data;
using OpenCS.Reporting;

namespace OpenCS.Converters;

/// <summary>Преобразует вид расчётной задачи в видимость пункта экспорта отчёта.</summary>
public sealed class ReportSupportedConverter : IValueConverter
{
    /// <summary>Реестр для unit-тестов; в приложении по умолчанию берётся из DataContext окна.</summary>
    public ReportProviderRegistry? Registry { get; set; }

    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string taskKind) return Visibility.Collapsed;
        var registry = Registry
            ?? (Application.Current?.MainWindow?.DataContext as AppViewModel)?.ReportProviders;
        return registry?.SupportedKinds.Contains(taskKind, StringComparer.Ordinal) == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Обратное преобразование видимости отчёта не поддерживается.");
}
