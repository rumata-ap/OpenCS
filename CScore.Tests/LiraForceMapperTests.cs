using CScore.Import;
using Xunit;

namespace CScore.Tests;

public class LiraForceMapperTests
{
    [Fact]
    public void MapShell_WritesSigmaNotNxNyNxy_MxMyMxyQxQyUnaffected()
    {
        // shellForce=10 now only feeds Qx/Qy (Nx/Ny/Nxy moved to SigmaX/SigmaY/TauXY, scaled by `stress` instead).
        var units = new LiraUnitScales(force: 1.0, moment: 1.0, shellForce: 10.0, shellMoment: 20.0, stress: 5.0);
        var opt = new LiraImportOptions { InvertShellBendingMoments = false };
        var src = new Dictionary<string, double>
        {
            ["NX"] = 1, ["NY"] = 2, ["TXY"] = 3,
            ["MX"] = 4, ["MY"] = 5, ["MXY"] = 6,
            ["QX"] = 7, ["QY"] = 8,
        };

        var item = LiraForceMapper.MapShell(src, units, opt);

        Assert.Equal(0, item.Nx);
        Assert.Equal(0, item.Ny);
        Assert.Equal(0, item.Nxy);
        Assert.Equal(5,  item.SigmaX!.Value, 12);   // NX * stress(5)
        Assert.Equal(10, item.SigmaY!.Value, 12);   // NY * stress(5)
        Assert.Equal(15, item.TauXY!.Value,  12);   // TXY * stress(5)
        Assert.Equal(80, item.Mx, 12);
        Assert.Equal(100, item.My, 12);
        Assert.Equal(120, item.Mxy, 12);
        Assert.Equal(70, item.Qx, 12);
        Assert.Equal(80, item.Qy, 12);
    }

    [Fact]
    public void MapShell_InvertShellBendingMoments_FlipsMxMyMxy_NotSigmaOrQ()
    {
        var units = new LiraUnitScales(force: 1.0, moment: 1.0, shellForce: 1.0, shellMoment: 1.0, stress: 1.0);
        var opt = new LiraImportOptions { InvertShellBendingMoments = true };
        var src = new Dictionary<string, double>
        {
            ["NX"] = 1, ["MX"] = 4, ["MY"] = 5, ["MXY"] = 6, ["QX"] = 7,
        };

        var item = LiraForceMapper.MapShell(src, units, opt);

        Assert.Equal(1, item.SigmaX!.Value, 12);
        Assert.Equal(-4, item.Mx, 12);
        Assert.Equal(-5, item.My, 12);
        Assert.Equal(-6, item.Mxy, 12);
        Assert.Equal(7, item.Qx, 12);
    }
}
