using CScore;
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

    static Contour Rectangle(double xMin, double yMin, double xMax, double yMax)
        => new(
            [xMin, xMax, xMax, xMin, xMin],
            [yMin, yMin, yMax, yMax, yMin],
            "rectangle");
}
