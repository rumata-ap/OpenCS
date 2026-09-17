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

    [Fact]
    public void Handler_EtaJson_ContainsRadiusOfGyrationAndFallbackFlag()
    {
        var section = ReportFixtures.BuildBeam();          // 300 × 600, бетон есть
        var task = new CalcTask
        {
            Id = 1,
            Kind = "strain_state",
            CalcType = CalcType.C,
            ParamsJson = new LimitForceParams
            {
                EtaEnabled = true,
                EtaL = 6.0,
                EtaMuX = 1.0,
                EtaMuY = 1.0,
                EtaPsiX = 0.5,
                EtaPsiY = 0.5
            }.ToJson()
        };

        var result = new StrainStateHandler().Run(
            task, section, new LoadItem { N = -500, Mx = 60, My = 0 }, new CalcSettings());

        using var json = JsonDocument.Parse(result.DataJson);
        var eta = json.RootElement.GetProperty("eta");

        // l0x = 6 м, ix = 0,6/√12 ≈ 0,1732 м — поправка применяется (в JSON радиус
        // округляется до четырёх знаков, см. StrainStateJsonHelper.FiniteRounded).
        Assert.Equal(0.1732, eta.GetProperty("ix").GetDouble(), 4);
        Assert.Equal(0.0866, eta.GetProperty("iy").GetDouble(), 4);
        Assert.True(eta.GetProperty("slenderX").GetBoolean());
        Assert.False(eta.GetProperty("radiusFallbackX").GetBoolean());
        Assert.False(eta.GetProperty("radiusFallbackY").GetBoolean());
    }
}
