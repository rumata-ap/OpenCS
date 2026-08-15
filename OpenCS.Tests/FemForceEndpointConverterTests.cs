using OpenCS.OpenSees.Structural;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки общей конвенции концевых усилий OpenSees.</summary>
public class FemForceEndpointConverterTests
{
    [Fact]
    public void Convert_UsesOpenSeesEndpointSignsAndCScoreLoadMapping()
    {
        var source = new FemElementEndForces(
            7, 1000, 2000, 3000, 4000, 5000, 6000,
            7000, 8000, 9000, 10000, 11000, 12000);

        var pair = FemForceEndpointConverter.Convert(
            source, FemForceEndpointSignPolicy.OpenSeesDefault);

        Assert.Equal(new FemForceEndpointValues(-1000, -2000, -3000, -4000, 5000, 6000), pair.Start);
        Assert.Equal(new FemForceEndpointValues(7000, 8000, 9000, 10000, -11000, -12000), pair.End);

        var item = FemForceEndpointConverter.ToLoadItem(pair.Start, 1, "node 10");
        Assert.Equal(-1.0, item.N, 12);
        Assert.Equal(6.0, item.Mx, 12);
        Assert.Equal(5.0, item.My, 12);
        Assert.Equal(-3.0, item.Vx, 12);
        Assert.Equal(-2.0, item.Vy, 12);
        Assert.Equal(-4.0, item.T, 12);
        Assert.Equal("node 10", item.Label);
    }
}
