using CScore;
using Xunit;

namespace CScore.Tests;

public sealed class GreenIntegratorTests
{
    [Fact]
    public void IntegrateMonomials_ReturnsRectangleMoments()
    {
        var gi = new GreenIntegrator(
            [(-1.0, -2.0), (1.0, -2.0), (1.0, 2.0), (-1.0, 2.0)]);

        var moments = gi.IntegrateMonomials((_, _) => 3.0);

        Assert.Equal(24.0, moments.A0, 8);
        Assert.Equal(0.0, moments.Ax, 8);
        Assert.Equal(0.0, moments.Ay, 8);
        Assert.Equal(8.0, moments.Axx, 6);
        Assert.Equal(0.0, moments.Axy, 8);
        Assert.Equal(32.0, moments.Ayy, 6);
    }
}
