using CScore;
using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверки резолва поставщика отчёта по типу расчётной задачи.</summary>
public sealed class ReportProviderRegistryTests
{
    sealed class FakeProvider(string kind) : IReportProvider
    {
        public bool CanHandle(CalcTask task) => task.Kind == kind;
        public ReportDocument Build(ReportContext context) => new("fake");
    }

    [Fact]
    public void Resolve_ReturnsFirstMatchingProvider()
    {
        var registry = new ReportProviderRegistry([new FakeProvider("other"), new FakeProvider("strain_state")]);
        var provider = registry.Resolve(new CalcTask { Kind = "strain_state" });
        Assert.True(provider.CanHandle(new CalcTask { Kind = "strain_state" }));
    }

    [Fact]
    public void Resolve_Throws_WhenNoProviderHandlesTask()
    {
        var registry = new ReportProviderRegistry([new FakeProvider("strain_state")]);
        var ex = Assert.Throws<NotSupportedException>(
            () => registry.Resolve(new CalcTask { Kind = "shear_inclined" }));
        Assert.Contains("shear_inclined", ex.Message);
    }

    [Fact]
    public void Resolve_Throws_ForEmptyRegistry()
        => Assert.Throws<NotSupportedException>(
            () => new ReportProviderRegistry([]).Resolve(new CalcTask { Kind = "strain_state" }));
}
