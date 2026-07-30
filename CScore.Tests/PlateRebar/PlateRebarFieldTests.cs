using CScore;
using CScore.PlateRebar;
using CScore.Planar;
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
        var region = new PlanarRegion();

        var field = PlateRebarField.From(section, region);

        Assert.Single(field.BaseLayout);
        Assert.Equal("Top", field.BaseLayout[0].Name);
        Assert.Empty(field.Zones);
    }

    [Fact]
    public void From_UsesRegionRebarZones()
    {
        var section = new PlateSection();
        var region = new PlanarRegion
        {
            RebarZones = [new RebarZone { Name = "Zone A" }],
        };

        var field = PlateRebarField.From(section, region);

        Assert.Single(field.Zones);
        Assert.Equal("Zone A", field.Zones[0].Name);
    }

    [Fact]
    public void RegionRebarZones_DefaultsToEmptyList()
    {
        var region = new PlanarRegion();

        Assert.Empty(region.RebarZones);
    }
}
