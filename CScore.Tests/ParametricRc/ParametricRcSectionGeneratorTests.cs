using CScore;
using CScore.ParametricRc;
using Xunit;

namespace CScore.Tests.ParametricRc;

public sealed class ParametricRcSectionGeneratorTests
{
    [Fact]
    public void GenerateRectangle_createsClosedConcreteContourAndPhysicalBars()
    {
        var definition = ParametricRcSectionDefinition.Rectangle(0.30, 0.50) with
        {
            LowerRebar = ParametricLongitudinalLayer.Physical(3, 0.016, -0.21),
            UpperRebar = ParametricLongitudinalLayer.Physical(2, 0.014, 0.21)
        };

        var result = ParametricRcSectionGenerator.Generate(definition);

        Assert.Empty(result.Diagnostics);
        var concrete = Assert.Single(result.Section.Areas, a => a.Category == AreaCategory.Region);
        Assert.NotNull(concrete.Hull);
        Assert.True(concrete.Hull!.IsClosed);
        Assert.False(string.IsNullOrWhiteSpace(concrete.WKT));
        Assert.Equal(5, concrete.Hull.X.Count);
        Assert.Equal(5, result.Section.Areas.SelectMany(a => a.Fibers)
            .Count(f => f.TypeFiber == FiberType.point));
    }

    [Fact]
    public void GenerateAnnulus_creates32SegmentClosedHoleAndSevenUniformBars()
    {
        var definition = ParametricRcSectionDefinition.Annulus(0.60, 0.32) with
        {
            PolarRebar = new ParametricPolarRebar(7, 0.016, 0.24)
        };

        var result = ParametricRcSectionGenerator.Generate(definition);

        Assert.Empty(result.Diagnostics);
        var concrete = Assert.Single(result.Section.Areas, a => a.Category == AreaCategory.Region);
        Assert.True(concrete.Hull!.IsClosed);
        Assert.Equal(33, concrete.Hull.X.Count);
        var hole = Assert.Single(concrete.Holes);
        Assert.True(hole.IsClosed);
        Assert.Equal(33, hole.X.Count);
        Assert.Equal(7, result.Section.Areas.SelectMany(a => a.Fibers)
            .Count(f => f.TypeFiber == FiberType.point));
    }

    [Fact]
    public void GenerateIdealizedLayer_createsOnePointFiberWithDeclaredAxis()
    {
        var definition = ParametricRcSectionDefinition.Rectangle(0.30, 0.50) with
        {
            LowerRebar = ParametricLongitudinalLayer.Idealized(0.0012, 0.020, -0.21,
                IdealizedRebarAxis.Mx)
        };

        var result = ParametricRcSectionGenerator.Generate(definition);

        var layer = Assert.Single(result.Section.Areas,
            a => a.RebarRepresentation == RebarRepresentation.IdealizedLayer);
        var fiber = Assert.Single(layer.Fibers);
        Assert.Equal(0.0012, fiber.Area, 12);
        Assert.Equal(IdealizedRebarAxis.Mx, layer.IdealizedAxis);
    }
}
