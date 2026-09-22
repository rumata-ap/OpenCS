using System.Text.Json;
using CScore;
using Xunit;

namespace CScore.Tests;

public sealed class MaterialResistanceModelTests
{
    [Fact]
    public void RebarNdmDiagramUsesRsSymmetricallyAndDoesNotUseRsc()
    {
        var chars = new MaterialChars(CalcType.C)
        {
            Type = MatType.ReSteelF,
            Fc = -300_000,
            Rsc = 300_000,
            Ft = 500_000,
            E = 200_000_000,
            Ec2 = -0.0035,
            Et2 = 0.025,
        };

        var diagram = chars.D2L();

        Assert.Equal(-500_000, diagram.SigValue(-0.0025), 6);
        Assert.Equal(500_000, diagram.SigValue(0.0025), 6);
    }

    [Fact]
    public void ConditionalRebarNdmDiagramUsesRsOnBothBranches()
    {
        var chars = new MaterialChars(CalcType.C)
        {
            Type = MatType.ReSteelU,
            Fc = -300_000,
            Rsc = 300_000,
            Ft = 500_000,
            E = 200_000_000,
            Et2 = 0.015,
        };

        var diagram = chars.D3L();

        Assert.Equal(-450_000, diagram.SigValue(-0.00225), 6);
        Assert.Equal(450_000, diagram.SigValue(0.00225), 6);
    }

    [Fact]
    public void FormulaResistancePrefersTabularRscOverDiagramFc()
    {
        var chars = new MaterialChars { Fc = -500_000, Ft = 700_000, Rsc = 300_000 };

        Assert.Equal(300_000, chars.GetRscOrLegacyFc());
    }

    [Fact]
    public void ShellUlsUsesTabularRscForCompressionRebar()
    {
        var concrete = new MaterialChars { Fc = -14_500 };
        var rebar = new MaterialChars
        {
            Fc = -700_000,
            Ft = 700_000,
            Rsc = 300_000,
            E = 200_000_000,
        };
        const double h0 = 0.26, aPrime = 0.04, asT = 0.003, asC = 0.001;
        double x = Math.Min((700_000 * asT - 300_000 * asC) / 14_500,
            0.8 / (1.0 + 700_000.0 / (200_000_000.0 * 0.0035)) * h0);
        double expected = 14_500 * x * (h0 - 0.5 * x) + 300_000 * asC * (h0 - aPrime);

        var result = ShellSimplSolver.ComputeStripUls(
            M_des: 100, N_des: 0, h: 0.3, h0: h0, a_prime: aPrime,
            As_t: asT, As_c: asC, concrete: concrete, rebar: rebar);

        Assert.Equal(expected, result.M_ult, 8);
    }

    [Fact]
    public void OldSavedMaterialWithoutRscFallsBackToLegacyFc()
    {
        var oldJson = "{\"Fc\":-400000,\"Ft\":500000,\"E\":200000000}";
        var chars = JsonSerializer.Deserialize<MaterialChars>(oldJson)!;

        Assert.Equal(0, chars.Rsc);
        Assert.Equal(400_000, chars.GetRscOrLegacyFc());
    }
}
