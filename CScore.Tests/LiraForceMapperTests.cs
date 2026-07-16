using CScore.Import;
using Xunit;

namespace CScore.Tests;

public class LiraForceMapperTests
{
    [Fact]
    public void MapShell_AppliesUnitScale_NoInversion_NxNyNxyQxQyUnaffected()
    {
        var units = new LiraUnitScales(force: 1.0, moment: 1.0, shellForce: 10.0, shellMoment: 20.0);
        var opt = new LiraImportOptions { InvertShellBendingMoments = false };
        var src = new Dictionary<string, double>
        {
            ["NX"] = 1, ["NY"] = 2, ["TXY"] = 3,
            ["MX"] = 4, ["MY"] = 5, ["MXY"] = 6,
            ["QX"] = 7, ["QY"] = 8,
        };

        var item = LiraForceMapper.MapShell(src, units, opt);

        Assert.Equal(10, item.Nx, 12);
        Assert.Equal(20, item.Ny, 12);
        Assert.Equal(30, item.Nxy, 12);
        Assert.Equal(80, item.Mx, 12);
        Assert.Equal(100, item.My, 12);
        Assert.Equal(120, item.Mxy, 12);
        Assert.Equal(70, item.Qx, 12);
        Assert.Equal(80, item.Qy, 12);
    }

    [Fact]
    public void MapShell_InvertShellBendingMoments_FlipsMxMyMxy_NotNQ()
    {
        var units = new LiraUnitScales(force: 1.0, moment: 1.0, shellForce: 1.0, shellMoment: 1.0);
        var opt = new LiraImportOptions { InvertShellBendingMoments = true };
        var src = new Dictionary<string, double>
        {
            ["NX"] = 1, ["MX"] = 4, ["MY"] = 5, ["MXY"] = 6, ["QX"] = 7,
        };

        var item = LiraForceMapper.MapShell(src, units, opt);

        Assert.Equal(1, item.Nx, 12);
        Assert.Equal(-4, item.Mx, 12);
        Assert.Equal(-5, item.My, 12);
        Assert.Equal(-6, item.Mxy, 12);
        Assert.Equal(7, item.Qx, 12);
    }
}
