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
    public void MapShell_StressTimesThickness_ThenTonToKn()
    {
        var opt = new ScadXlsImportOptions
        {
            TonToKnFactor = 10.0,
            InvertShellBendingMoments = false,
        };
        // σ=1 Т/м², h=0.2 м → N=0.2 Т/м → 2 кН/м; Mx=4 уже погонный → 40
        var item = ScadXlsForceMapper.MapShell(
            sx: 1, sy: 2, txy: 3, mx: 4, my: 5, mxy: 6, qx: 7, qy: 8,
            thicknessM: 0.2, opt);

        Assert.Equal(2, item.Nx, 12);
        Assert.Equal(4, item.Ny, 12);
        Assert.Equal(6, item.Nxy, 12);
        Assert.Equal(40, item.Mx, 12);
        Assert.Equal(50, item.My, 12);
        Assert.Equal(60, item.Mxy, 12);
        Assert.Equal(70, item.Qx, 12);
        Assert.Equal(80, item.Qy, 12);
    }

    [Fact]
    public void MapShell_InvertShellBendingMoments_FlipsMxMyMxy_NotNQ()
    {
        var opt = new ScadXlsImportOptions
        {
            TonToKnFactor = 1.0,
            InvertShellBendingMoments = true,
        };
        var item = ScadXlsForceMapper.MapShell(
            sx: 1, sy: 2, txy: 3, mx: 4, my: 5, mxy: 6, qx: 7, qy: 8,
            thicknessM: 1.0, opt);

        Assert.Equal(1, item.Nx, 12);
        Assert.Equal(2, item.Ny, 12);
        Assert.Equal(3, item.Nxy, 12);
        Assert.Equal(-4, item.Mx, 12);
        Assert.Equal(-5, item.My, 12);
        Assert.Equal(-6, item.Mxy, 12);
        Assert.Equal(7, item.Qx, 12);
        Assert.Equal(8, item.Qy, 12);
    }
}
