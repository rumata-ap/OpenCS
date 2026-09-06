using System.Globalization;
using System.Windows;
using OpenCS.Converters;
using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки видимости пункта экспорта по реестру провайдеров.</summary>
public sealed class ReportSupportedConverterTests
{
    [Fact]
    public void Convert_UsesRegistrySupportedKinds()
    {
        var converter = new ReportSupportedConverter
        {
            Registry = new ReportProviderRegistry([
                new StrainStateReportProvider(),
                new LimitForceReportProvider()
            ])
        };

        foreach (var kind in new[] { "strain_state", "limit_force", "limit_moment", "limit_axial" })
            Assert.Equal(Visibility.Visible, converter.Convert(kind, typeof(Visibility), null!, CultureInfo.InvariantCulture));
        foreach (var kind in new string?[] { "torsion_fem", null })
            Assert.Equal(Visibility.Collapsed, converter.Convert(kind, typeof(Visibility), null!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBack_IsUnsupported()
    {
        var converter = new ReportSupportedConverter();
        Assert.Throws<NotSupportedException>(() => _ = converter.ConvertBack(
            Visibility.Visible, typeof(string), null!, CultureInfo.InvariantCulture));
    }
}
