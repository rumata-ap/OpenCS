using System.Text.Json;
using CScore;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

public sealed class StrainStateDiagnosticsJsonTests
{
    [Fact]
    public void Handler_PersistsReportDiagnosticsContract()
    {
        var task = new CalcTask { Id = 1, Kind = "strain_state", CalcType = CalcType.C };
        var result = new StrainStateHandler().Run(
            task, new CrossSection(), new LoadItem(), new CalcSettings());

        using var json = JsonDocument.Parse(result.DataJson);
        var root = json.RootElement;

        Assert.True(root.TryGetProperty("formula_version", out _));
        Assert.True(root.TryGetProperty("stiffness", out var stiffness));
        Assert.True(stiffness.TryGetProperty("d11", out _));
        Assert.True(root.TryGetProperty("jacobian", out var jacobian));
        Assert.Equal(3, jacobian.GetProperty("rows").GetArrayLength());
        Assert.Equal(3, jacobian.GetProperty("columns").GetArrayLength());
        Assert.True(root.TryGetProperty("equilibrium", out _));
        Assert.True(root.TryGetProperty("extrema", out _));
    }
}
