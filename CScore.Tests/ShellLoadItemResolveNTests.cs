using CScore;
using Xunit;

namespace CScore.Tests;

public class ShellLoadItemResolveNTests
{
    [Fact]
    public void ResolveN_NoSigma_ReturnsStoredForcesUnchanged()
    {
        var item = new ShellLoadItem { Nx = 10, Ny = 20, Nxy = 30 };
        var (nx, ny, nxy) = item.ResolveN(0.2);
        Assert.Equal(10, nx);
        Assert.Equal(20, ny);
        Assert.Equal(30, nxy);
    }

    [Fact]
    public void ResolveN_WithSigma_MultipliesByThickness()
    {
        // кПа·м = кН/м
        var item = new ShellLoadItem { SigmaX = 100, SigmaY = 200, TauXY = 50, Nx = 999, Ny = 999, Nxy = 999 };
        var (nx, ny, nxy) = item.ResolveN(0.2);
        Assert.Equal(20, nx, 12);
        Assert.Equal(40, ny, 12);
        Assert.Equal(10, nxy, 12);
    }

    [Fact]
    public void ResolveN_PartialSigma_TreatsMissingComponentAsZero()
    {
        var item = new ShellLoadItem { SigmaX = 100, SigmaY = null, TauXY = null };
        var (nx, ny, nxy) = item.ResolveN(0.5);
        Assert.Equal(50, nx, 12);
        Assert.Equal(0, ny, 12);
        Assert.Equal(0, nxy, 12);
    }
}
