using CScore;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

public sealed class ShellLayeredSlsParamsTests
{
    [Fact]
    public void Defaults_MatchShellSimplParamsConvention()
    {
        var p = new ShellLayeredSlsParams();
        Assert.Equal(0.3, p.AcrcLimMm);
        Assert.Equal(1.0, p.Phi1);
        Assert.Equal(0.5, p.Phi2);
        Assert.True(p.AutoStressToForce);
        Assert.Equal(SigmaSCrcMethod.ReleasedConcrete8137, p.SigmaSCrc);
        Assert.Equal(WplGammaMethod.Sp63, p.WplGamma);
    }

    [Fact]
    public void RoundTrip_PreservesValues()
    {
        var p = new ShellLayeredSlsParams
        {
            Nx = 10, Ny = -5, Nxy = 2, Mx = 50, My = -20, Mxy = 3,
            AutoStressToForce = false, AcrcLimMm = 0.4, Phi1 = 1.4, Phi2 = 0.5,
            SigmaSCrcMethod = "moment", WplGammaMethod = "snip",
        };

        var json = p.ToJson();
        Assert.Contains("\"mxy\"", json);
        Assert.Contains("\"acrc_lim_mm\"", json);

        var parsed = ShellLayeredSlsParams.Parse(json);
        Assert.Equal(p.Nx, parsed.Nx);
        Assert.Equal(p.Mxy, parsed.Mxy);
        Assert.Equal(p.AcrcLimMm, parsed.AcrcLimMm);
        Assert.Equal(p.Phi1, parsed.Phi1);
        Assert.Equal(CScore.SigmaSCrcMethod.CrackingMoment8138, parsed.SigmaSCrc);
        Assert.Equal(CScore.WplGammaMethod.Snip2030184, parsed.WplGamma);
    }

    [Fact]
    public void Parse_EmptyJson_ReturnsDefaults()
    {
        var parsed = ShellLayeredSlsParams.Parse("{}");
        Assert.Equal(1.0, parsed.Phi1);
        var parsedNull = ShellLayeredSlsParams.Parse(null);
        Assert.Equal(1.0, parsedNull.Phi1);
    }
}
