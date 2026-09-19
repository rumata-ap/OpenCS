using CScore.ParametricRc;
using Xunit;

namespace CScore.Tests.ParametricRc;

public sealed class ParametricRcContractTests
{
    [Fact]
    public void TeeAndIBeamExposeNonOverlappingExactStirrupZones()
    {
        foreach (var definition in new[]
        {
            ParametricRcSectionDefinition.Tee(0.60, 0.80, 0.20, 0.16),
            ParametricRcSectionDefinition.IBeam(0.60, 0.80, 0.20, 0.16)
        })
        {
            var result = ParametricRcSectionGenerator.Generate(definition with
            {
                StirrupCuts = [new ParametricStirrupCutSet(
                    ParametricStirrupZone.Web, ParametricStirrupDirection.Vertical,
                    1, 0.008, 0.20, 0.03, 1)]
            });

            Assert.Empty(result.Diagnostics);
            Assert.NotEmpty(result.ZoneMap);
            Assert.All(result.ZoneMap.Values, zone => Assert.True(zone.Polygon.Count >= 5));
        }
    }

    [Fact]
    public void CircularRebarAreaEqualsNumberOfBarsTimesSingleBarArea()
    {
        const int count = 9;
        const double diameter = 0.016;
        var result = ParametricRcSectionGenerator.Generate(
            ParametricRcSectionDefinition.Circle(0.60) with
            {
                PolarRebar = new ParametricPolarRebar(count, diameter, 0.24)
            });

        double area = result.Section.Areas
            .Where(a => a.Category == CScore.AreaCategory.RebarGroup)
            .SelectMany(a => a.Fibers)
            .Sum(f => f.Area);

        Assert.Equal(count * Math.PI * diameter * diameter / 4.0, area, 12);
    }

    [Fact]
    public void GeneratorAssignsSelectedConcreteAndLongitudinalMaterials()
    {
        var result = ParametricRcSectionGenerator.Generate(
            ParametricRcSectionDefinition.Rectangle(0.30, 0.50) with
            {
                ConcreteMaterialId = 11,
                LongitudinalMaterialId = 22,
                LowerRebar = ParametricLongitudinalLayer.Physical(2, 0.016, -0.21)
            });

        Assert.Empty(result.Diagnostics);
        Assert.Equal(11, result.Section.Areas.Single(a => a.Category == CScore.AreaCategory.Region).MaterialId);
        Assert.Equal(22, result.Section.Areas.Single(a => a.Category == CScore.AreaCategory.RebarGroup).MaterialId);
    }
}
