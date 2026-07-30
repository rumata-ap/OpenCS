using CScore;
using CScore.PlateRebar;
using Xunit;

namespace CScore.Tests.PlateRebar;

public class PlateRebarFieldTests
{
    [Fact]
    public void From_UsesSectionRebarLayersAsBaseLayout()
    {
        var section = new PlateSection
        {
            RebarLayers = [new PlateRebarLayer { Name = "Top", Asx = 0.001 }],
        };

        var field = PlateRebarField.From(section);

        Assert.Single(field.BaseLayout);
        Assert.Equal("Top", field.BaseLayout[0].Name);
        Assert.Empty(field.Zones);
    }

    [Fact]
    public void From_UsesSectionRebarZones()
    {
        var section = new PlateSection
        {
            RebarZones = [new RebarZone { Name = "Zone A" }],
        };

        var field = PlateRebarField.From(section);

        Assert.Single(field.Zones);
        Assert.Equal("Zone A", field.Zones[0].Name);
    }

    [Fact]
    public void CloneForCalc_DeepCopiesRebarZones()
    {
        var section = new PlateSection
        {
            RebarZones = [new RebarZone { Name = "Zone A", Priority = 3 }],
        };

        var clone = section.CloneForCalc();
        clone.RebarZones[0].Priority = 99;

        Assert.Equal(3, section.RebarZones[0].Priority);
    }
}
