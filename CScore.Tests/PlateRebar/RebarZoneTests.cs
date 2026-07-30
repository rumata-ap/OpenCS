using CScore;
using CScore.PlateRebar;
using Xunit;

namespace CScore.Tests.PlateRebar;

public class RebarZoneTests
{
    [Fact]
    public void Clone_ProducesIndependentDeepCopy()
    {
        var zone = new RebarZone
        {
            Name = "Zone A",
            Polygon = [new RebarZonePoint { U = 0, V = 0 }, new RebarZonePoint { U = 1, V = 0 }, new RebarZonePoint { U = 1, V = 1 }],
            Face = RebarFace.PlusN,
            Priority = 5,
            Operation = RebarZoneOperation.Add,
            Layout = new PlateRebarLayer { Name = "L1", Asx = 0.001, Zsx = 0.09 },
        };

        var clone = zone.Clone();
        clone.Name = "Changed";
        clone.Polygon[0].U = 99;
        clone.Priority = 1;
        clone.Layout.Asx = 0.5;

        Assert.Equal("Zone A", zone.Name);
        Assert.Equal(0, zone.Polygon[0].U);
        Assert.Equal(5, zone.Priority);
        Assert.Equal(0.001, zone.Layout.Asx);
    }

    [Fact]
    public void Clone_PreservesFaceAndOperation()
    {
        var zone = new RebarZone { Face = RebarFace.MinusN, Operation = RebarZoneOperation.Replace };

        var clone = zone.Clone();

        Assert.Equal(RebarFace.MinusN, clone.Face);
        Assert.Equal(RebarZoneOperation.Replace, clone.Operation);
    }
}
