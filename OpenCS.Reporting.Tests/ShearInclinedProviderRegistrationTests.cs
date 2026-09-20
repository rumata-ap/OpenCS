using CScore;
using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверяет регистрацию отчёта одиночного наклонного сечения.</summary>
public sealed class ShearInclinedProviderRegistrationTests
{
    [Fact]
    public void Registry_resolves_shear_inclined_provider()
    {
        var registry = new ReportProviderRegistry([new ShearInclinedReportProvider()]);
        var provider = registry.Resolve(new CalcTask { Kind = "shear_inclined" });

        Assert.IsType<ShearInclinedReportProvider>(provider);
    }
}
