using CScore;
using OpenCS.Converters;
using OpenCS.Views;
using OpenCS.Views.Helpers;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки построения read-only геометрии поперечного сечения.</summary>
public class CrossSectionPlotBuilderTests
{
    [Fact]
    public void Build_OrdinarySection_IncludesHullAndPointRebarAndBounds()
    {
        var area = new MaterialArea
        {
            Hull = Rectangle(0, 0, 2, 1),
            Fibers = [Fiber.CreatePoint(0.02, 0.4, 0.3)]
        };
        var data = CrossSectionPlotBuilder.Build(new CrossSection { Areas = [area] });

        Assert.Contains(data.Elements, element => element is PolygonElement);
        Assert.Contains(data.Elements, element => element is CircleElement);
        Assert.Equal(0, data.XMin);
        Assert.Equal(2, data.XMax);
        Assert.Equal(0, data.YMin);
        Assert.Equal(1, data.YMax);
    }

    [Fact]
    public void Build_TwoStageSection_IncludesBothStagesInOnePreview()
    {
        var section = new TwoStageSection
        {
            Stage1 = new CrossSection { Areas = [new MaterialArea { Hull = Rectangle(-2, -1, -1, 0) }] },
            Areas = [new MaterialArea { Hull = Rectangle(1, 2, 2, 3) }]
        };

        var data = CrossSectionPlotBuilder.Build(section);

        Assert.Equal(2, data.Elements.OfType<PolygonElement>().Count());
        Assert.Equal(-2, data.XMin);
        Assert.Equal(2, data.XMax);
        Assert.Equal(-1, data.YMin);
        Assert.Equal(3, data.YMax);
    }

    [Fact]
    public void Build_PointRebar_UsesMaterialTypeColor()
    {
        var physical = new Material { Type = MatType.ReSteelF };
        var conditional = new Material { Type = MatType.ReSteelU };
        var section = new CrossSection
        {
            Areas =
            [
                new MaterialArea
                {
                    Material = physical,
                    Fibers = [Fiber.CreatePoint(0.02, 0.1, 0.1)]
                },
                new MaterialArea
                {
                    Material = conditional,
                    Fibers = [Fiber.CreatePoint(0.02, 0.2, 0.1)]
                }
            ]
        };

        var circles = CrossSectionPlotBuilder.Build(section).Elements
            .OfType<CircleElement>()
            .Where(c => c.Fill is System.Windows.Media.SolidColorBrush)
            .ToArray();

        Assert.Equal(2, circles.Length);
        Assert.Equal(MatTypeToBrushConverter.GetBrush(MatType.ReSteelF).Color,
            ((System.Windows.Media.SolidColorBrush)circles[0].Fill!).Color);
        Assert.Equal(MatTypeToBrushConverter.GetBrush(MatType.ReSteelU).Color,
            ((System.Windows.Media.SolidColorBrush)circles[1].Fill!).Color);
        Assert.NotEqual(((System.Windows.Media.SolidColorBrush)circles[0].Fill!).Color,
            ((System.Windows.Media.SolidColorBrush)circles[1].Fill!).Color);
    }

    [Theory]
    [InlineData(120.0)]
    [InlineData(-120.0)]
    public void Build_PointRebar_WithNonZeroPrestress_AddsOuterContour(double sigSp)
    {
        var area = new MaterialArea
        {
            Material = new Material { Type = MatType.ReSteelU },
            SigSp = sigSp,
            Fibers = [Fiber.CreatePoint(0.02, 0.1, 0.1)]
        };

        var circles = CrossSectionPlotBuilder.Build(new CrossSection { Areas = [area] })
            .Elements.OfType<CircleElement>().ToArray();

        Assert.Equal(2, circles.Length);
        Assert.Null(circles[0].Fill);
        Assert.NotNull(circles[1].Fill);
        Assert.True(circles[0].Radius > circles[1].Radius);
    }

    [Fact]
    public void Build_PointRebar_WithZeroPrestress_HasNoOuterContour()
    {
        var area = new MaterialArea
        {
            Material = new Material { Type = MatType.ReSteelU },
            SigSp = 0,
            Fibers = [Fiber.CreatePoint(0.02, 0.1, 0.1)]
        };

        var circles = CrossSectionPlotBuilder.Build(new CrossSection { Areas = [area] })
            .Elements.OfType<CircleElement>().ToArray();

        Assert.Single(circles);
        Assert.NotNull(circles[0].Fill);
    }

    static Contour Rectangle(double xMin, double yMin, double xMax, double yMax)
        => new(
            [xMin, xMax, xMax, xMin, xMin],
            [yMin, yMin, yMax, yMax, yMin],
            "rectangle");
}
