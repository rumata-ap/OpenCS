using CScore;
using CScore.PlateRebar;
using Xunit;

namespace CScore.Tests.PlateRebar;

public class PlateRebarLayerFaceAngleTests
{
    [Fact]
    public void DefaultFace_IsPlusN()
    {
        var layer = new PlateRebarLayer();

        Assert.Equal(RebarFace.PlusN, layer.Face);
    }

    [Fact]
    public void DefaultAngle_IsZero()
    {
        var layer = new PlateRebarLayer();

        Assert.Equal(0.0, layer.Angle);
    }

    [Fact]
    public void Clone_CopiesFaceAndAngle()
    {
        var layer = new PlateRebarLayer { Face = RebarFace.MinusN, Angle = 15.0 };

        var clone = layer.Clone();

        Assert.Equal(RebarFace.MinusN, clone.Face);
        Assert.Equal(15.0, clone.Angle);
    }
}
