using CScore;
using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверки резолва поставщика отчёта по типу расчётной задачи.</summary>
public sealed class ReportProviderRegistryTests
{
    sealed class FakeProvider(string kind) : IReportProvider
    {
        public string TaskKind => kind;
        public IReadOnlyCollection<string> SupportedKinds => [kind];
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

    [Fact]
    public void TryResolve_ReturnsProviderWithoutThrowingForUnknownKind()
    {
        var provider = new FakeProvider("strain_state");
        var registry = new ReportProviderRegistry([provider]);

        Assert.True(registry.TryResolve(new CalcTask { Kind = "strain_state" }, out var resolved));
        Assert.Same(provider, resolved);
        Assert.False(registry.TryResolve(new CalcTask { Kind = "cracking" }, out _));
    }

    [Fact]
    public void SupportedKinds_ContainsDistinctUnionOfProviderKinds()
    {
        var registry = new ReportProviderRegistry([
            new MultiKindProvider("first", "limit_moment", "limit_force"),
            new MultiKindProvider("second", "limit_force", "cracking")
        ]);

        Assert.Equal(["limit_moment", "limit_force", "cracking"], registry.SupportedKinds);
    }

    sealed class MultiKindProvider(string taskKind, params string[] kinds) : IReportProvider
    {
        public string TaskKind => taskKind;
        public IReadOnlyCollection<string> SupportedKinds => kinds;
        public ReportDocument Build(ReportContext context) => new("fake");
    }
}
