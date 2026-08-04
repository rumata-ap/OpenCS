using CScore.Import;
using Xunit;

namespace CScore.Tests;

public class ScadXlsForceMapperTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("LS+SD", true)]
    [InlineData("ls+sd", true)]
    [InlineData("M1", false)]
    [InlineData("SD", false)]
    public void IsAcceptedForm_FiltersDynamics(string? form, bool expected)
        => Assert.Equal(expected, ScadXlsForceMapper.IsAcceptedForm(form));

    [Fact]
    public void MapBar_AppliesTonToKn_AndAxisMapping()
    {
        var opt = new ScadXlsImportOptions
        {
            TonToKnFactor = 10.0,
            InvertBarBendingMoments = false,
        };
        var item = ScadXlsForceMapper.MapBar(
            n: 1, mk: 2, my: 3, qz: 4, mz: 5, qy: 6, opt);

        Assert.Equal(10, item.N);
        Assert.Equal(20, item.T);
        Assert.Equal(30, item.My);
        Assert.Equal(40, item.Vx);
        Assert.Equal(50, item.Mx);
        Assert.Equal(60, item.Vy);
    }

    [Fact]
    public void MapBar_InvertMoments_FlipsMxMy()
    {
        var opt = new ScadXlsImportOptions
        {
            TonToKnFactor = 1.0,
            InvertBarBendingMoments = true,
        };
        var item = ScadXlsForceMapper.MapBar(0, 0, my: 3, qz: 0, mz: 5, qy: 0, opt);
        Assert.Equal(-3, item.My);
        Assert.Equal(-5, item.Mx);
    }

    [Fact]
    public void MapShell_WritesSigmaNotNxNyNxy_MxMyMxyQxQyStillTonToKn()
    {
        var opt = new ScadXlsImportOptions
        {
            TonToKnFactor = 10.0,
            InvertShellBendingMoments = false,
        };
        var item = ScadXlsForceMapper.MapShell(
            sx: 1, sy: 2, txy: 3, mx: 4, my: 5, mxy: 6, qx: 7, qy: 8, opt);

        Assert.Equal(0, item.Nx);
        Assert.Equal(0, item.Ny);
        Assert.Equal(0, item.Nxy);
        Assert.Equal(10, item.SigmaX!.Value, 12);
        Assert.Equal(20, item.SigmaY!.Value, 12);
        Assert.Equal(30, item.TauXY!.Value, 12);
        Assert.Equal(40, item.Mx, 12);
        Assert.Equal(50, item.My, 12);
        Assert.Equal(60, item.Mxy, 12);
        Assert.Equal(70, item.Qx, 12);
        Assert.Equal(80, item.Qy, 12);
    }

    [Fact]
    public void MapShell_InvertShellBendingMoments_FlipsMxMyMxy_NotSigmaOrQ()
    {
        var opt = new ScadXlsImportOptions
        {
            TonToKnFactor = 1.0,
            InvertShellBendingMoments = true,
        };
        var item = ScadXlsForceMapper.MapShell(
            sx: 1, sy: 2, txy: 3, mx: 4, my: 5, mxy: 6, qx: 7, qy: 8, opt);

        Assert.Equal(1, item.SigmaX!.Value, 12);
        Assert.Equal(2, item.SigmaY!.Value, 12);
        Assert.Equal(3, item.TauXY!.Value, 12);
        Assert.Equal(-4, item.Mx, 12);
        Assert.Equal(-5, item.My, 12);
        Assert.Equal(-6, item.Mxy, 12);
        Assert.Equal(7, item.Qx, 12);
        Assert.Equal(8, item.Qy, 12);
    }

    [Fact]
    public void MapShell_LengthUnitNotMeters_CorrectsSigmaAndQBySquareAndLinearLength()
    {
        // LengthM=0.01 (см): σ делится на 0.01² = 0.0001 → ×100 больше; Q делится на 0.01 → ×100 больше.
        // Mx/My/Mxy не корректируются (длина в числителе и знаменателе сокращается).
        var opt = new ScadXlsImportOptions
        {
            TonToKnFactor = 1.0,
            InvertShellBendingMoments = false,
            LengthM = 0.01,
        };
        var item = ScadXlsForceMapper.MapShell(
            sx: 1, sy: 1, txy: 1, mx: 1, my: 1, mxy: 1, qx: 1, qy: 1, opt);

        Assert.Equal(10000, item.SigmaX!.Value, 6);
        Assert.Equal(10000, item.SigmaY!.Value, 6);
        Assert.Equal(10000, item.TauXY!.Value, 6);
        Assert.Equal(1, item.Mx, 12);
        Assert.Equal(1, item.My, 12);
        Assert.Equal(1, item.Mxy, 12);
        Assert.Equal(100, item.Qx, 6);
        Assert.Equal(100, item.Qy, 6);
    }

    [Fact]
    public void MapShell_DefaultLengthM_IsMeters_NoCorrection()
    {
        var opt = new ScadXlsImportOptions { TonToKnFactor = 1.0 };
        Assert.Equal(1.0, opt.LengthM);
        var item = ScadXlsForceMapper.MapShell(
            sx: 1, sy: 1, txy: 1, mx: 1, my: 1, mxy: 1, qx: 1, qy: 1, opt);
        Assert.Equal(1, item.SigmaX!.Value, 12);
        Assert.Equal(1, item.Qx, 12);
    }
}
