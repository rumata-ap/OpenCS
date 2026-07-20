using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

public class FemLocalAxisTests
{
    static FemLinearNode N(int tag, double x, double y, double z) => new(tag, x, y, z, new bool[6]);

    [Fact]
    public void Vecxz_HorizontalBar_ReturnsGlobalZ()
    {
        var v = FemLocalAxis.Vecxz(N(1, 0, 0, 0), N(2, 3, 0, 0));
        Assert.Equal((0, 0, 1), v);
    }

    [Fact]
    public void Vecxz_VerticalBar_ReturnsGlobalX()
    {
        var v = FemLocalAxis.Vecxz(N(1, 0, 0, 0), N(2, 0, 0, 4));
        Assert.Equal((1, 0, 0), v);
    }

    [Fact]
    public void Vecxz_ZeroLength_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => FemLocalAxis.Vecxz(N(1, 1, 1, 1), N(2, 1, 1, 1)));
    }
}
