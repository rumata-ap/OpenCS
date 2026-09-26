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

    /// <summary>
    /// Регрессия 26.09.2026 (сечение 1500×250, 15Ø16, a = 40): стержни ставились на ±0,35·b
    /// без учёта защитного слоя и не совпадали с предпросмотром. Теперь крайние — в (a, a).
    /// </summary>
    [Fact]
    public void PhysicalBars_spanFullWidthWithSideOffsetEqualToLayerOffset()
    {
        var lower = ParametricLongitudinalLayer.Physical(15, 0.016, -0.125 + 0.040);
        var definition = ParametricRcSectionDefinition.Rectangle(1.50, 0.25) with { LowerRebar = lower };

        var result = ParametricRcSectionGenerator.Generate(definition);

        Assert.Empty(result.Diagnostics);
        var xs = result.Section.Areas.SelectMany(a => a.Fibers)
            .Where(f => f.TypeFiber == FiberType.point).Select(f => f.X).OrderBy(x => x).ToList();
        Assert.Equal(15, xs.Count);
        Assert.Equal(-0.71, xs[0], 9);
        Assert.Equal(0.71, xs[^1], 9);
        for (int i = 1; i < xs.Count; i++)
            Assert.Equal(1.42 / 14, xs[i] - xs[i - 1], 9);
        Assert.Equal(xs, ParametricRcSectionGenerator.GetPhysicalBarPositionsX(definition, lower));
    }

    [Fact]
    public void PhysicalBars_inTeeWebStayInsideWeb()
    {
        // Контур тавра центрирован по центру тяжести — низ стенки берём из самого контура.
        double bottom = TemplatePoints.TeePoints(0.80, 0.60, 0.20, 0.12).Min(p => p.Y);
        var lower = ParametricLongitudinalLayer.Physical(3, 0.016, bottom + 0.04);
        var definition = ParametricRcSectionDefinition.Tee(0.80, 0.60, 0.20, 0.12) with { LowerRebar = lower };

        var xs = ParametricRcSectionGenerator.GetPhysicalBarPositionsX(definition, lower);

        Assert.Equal([-0.06, 0.0, 0.06], xs.Select(x => Math.Round(x, 9)));
    }

    [Fact]
    public void PhysicalBars_singleBarIsCentred()
    {
        var lower = ParametricLongitudinalLayer.Physical(1, 0.016, -0.21);
        var definition = ParametricRcSectionDefinition.Rectangle(0.30, 0.50) with { LowerRebar = lower };

        Assert.Equal(0.0, Assert.Single(ParametricRcSectionGenerator.GetPhysicalBarPositionsX(definition, lower)), 12);
    }

    [Fact]
    public void PhysicalBars_tooManyForWidthIsRejected()
    {
        var definition = ParametricRcSectionDefinition.Rectangle(0.20, 0.50) with
        {
            LowerRebar = ParametricLongitudinalLayer.Physical(10, 0.020, -0.21)
        };

        var result = ParametricRcSectionGenerator.Generate(definition);

        Assert.Contains(result.Diagnostics, d => d.Contains("не помещаются"));
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
        Assert.Equal(result.Section.Areas[0].Id, layer.HostAreaId);
    }

    [Fact]
    public void GenerateIdealizedLayerForMy_placesLayerOnXCoordinate()
    {
        var result = ParametricRcSectionGenerator.Generate(
            ParametricRcSectionDefinition.Rectangle(0.30, 0.50) with
            { LowerRebar = ParametricLongitudinalLayer.Idealized(0.0012, 0.020, -0.11, IdealizedRebarAxis.My) });

        var fiber = Assert.Single(result.Section.Areas, a => a.RebarRepresentation == RebarRepresentation.IdealizedLayer).Fibers.Single();
        Assert.Equal(-0.11, fiber.X, 12);
        Assert.Equal(0, fiber.Y, 12);
    }

    [Fact]
    public void GenerateCircle_rejectsCartesianLongitudinalLayer()
    {
        var result = ParametricRcSectionGenerator.Generate(
            ParametricRcSectionDefinition.Circle(0.60) with
            {
                PolarRebar = new ParametricPolarRebar(7, 0.016, 0.24),
                LowerRebar = ParametricLongitudinalLayer.Idealized(0.001, 0.016, -0.2, IdealizedRebarAxis.Mx)
            });

        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void GenerateRectangle_createsOpenTransverseCutsInSeparateStirrupArea()
    {
        var result = ParametricRcSectionGenerator.Generate(
            ParametricRcSectionDefinition.Rectangle(0.30, 0.50) with
            {
                StirrupCuts = [new ParametricStirrupCutSet(ParametricStirrupZone.Body,
                    ParametricStirrupDirection.Vertical, 2, 0.008, 0.20, 0.03, 17)]
            });

        Assert.Empty(result.Diagnostics);
        var area = Assert.Single(result.Section.Areas, a => a.Category == AreaCategory.Stirrups);
        Assert.Equal(17, area.MaterialId);
        var group = Assert.Single(area.Stirrups);
        Assert.Equal(2, group.Elements.Count);
        Assert.All(group.Elements, e =>
        {
            Assert.True(e.CenterlineContour.IsPolyline);
            Assert.False(string.IsNullOrWhiteSpace(e.CenterlineContour.WKT));
            Assert.Equal(StirrupElementKind.Cut, e.Source!.Kind);
        });
    }
}
