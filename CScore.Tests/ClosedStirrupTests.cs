using System.Text.Json;
using CScore;
using Xunit;

namespace CScore.Tests;

/// <summary>Проверки геометрической записи замкнутых хомутов в MaterialArea.</summary>
public sealed class ClosedStirrupTests
{
    [Fact]
    public void NewMaterialArea_HasEmptyNonNullClosedStirrups()
    {
        var area = new MaterialArea();

        Assert.NotNull(area.ClosedStirrups);
        Assert.Empty(area.ClosedStirrups);
    }

    [Fact]
    public void ValidateFor_AcceptsGroupWithTwoClosedLoops()
    {
        var area = Region();
        var group = new ClosedStirrupGroup
        {
            MaterialId = 17,
            SpacingM = 0.15,
            Loops =
            [
                Loop(-0.12, -0.20, 0.12, 0.20, 0.0000503, 0.008),
                Loop(-0.05, -0.15, 0.05, 0.15, 0.0000785, 0.010)
            ]
        };

        group.ValidateFor(area);

        Assert.Equal(2, group.Loops.Count);
        Assert.Equal(0.15, group.SpacingM, 12);
        Assert.Equal(0.0000503, group.Loops[0].BarAreaM2, 12);
        Assert.Equal(0.010, group.Loops[1].BarDiameterM, 12);
    }

    [Theory]
    [InlineData(0.0, 0.0000503, 0.008)]
    [InlineData(-0.15, 0.0000503, 0.008)]
    [InlineData(double.NaN, 0.0000503, 0.008)]
    [InlineData(0.15, 0.0, 0.008)]
    [InlineData(0.15, -0.0000503, 0.008)]
    [InlineData(0.15, 0.0000503, double.PositiveInfinity)]
    public void ValidateFor_RejectsNonpositiveOrNonfiniteProperties(double spacing, double barArea, double diameter)
    {
        var group = new ClosedStirrupGroup
        {
            MaterialId = 17,
            SpacingM = spacing,
            Loops = [Loop(-0.12, -0.20, 0.12, 0.20, barArea, diameter)]
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => group.ValidateFor(Region()));
    }

    [Fact]
    public void ValidateFor_RejectsRebarAreaAndOpenLoop()
    {
        var group = new ClosedStirrupGroup
        {
            MaterialId = 17,
            SpacingM = 0.15,
            Loops = [new ClosedStirrupLoop
            {
                BarAreaM2 = 0.0000503,
                BarDiameterM = 0.008,
                CenterlineContour = new Contour
                {
                    X = [-0.12, 0.12, 0.12, -0.12],
                    Y = [-0.20, -0.20, 0.20, 0.20]
                }
            }]
        };

        Assert.Throws<ArgumentException>(() => group.ValidateFor(new MaterialArea { Category = AreaCategory.RebarGroup }));
        Assert.Throws<ArgumentException>(() => group.ValidateFor(Region()));
    }

    [Fact]
    public void CloneForCalc_DeepClonesClosedStirrupGeometry()
    {
        var area = Region();
        area.ClosedStirrups.Add(new ClosedStirrupGroup
        {
            Id = 4,
            MaterialId = 17,
            SpacingM = 0.15,
            Loops = [Loop(-0.12, -0.20, 0.12, 0.20, 0.0000503, 0.008)]
        });

        var clone = area.CloneForCalc();
        clone.ClosedStirrups[0].Loops[0].CenterlineContour.X[0] = -0.10;
        clone.ClosedStirrups[0].Loops.Add(Loop(-0.05, -0.15, 0.05, 0.15, 0.0000785, 0.010));

        Assert.Single(area.ClosedStirrups[0].Loops);
        Assert.Equal(-0.12, area.ClosedStirrups[0].Loops[0].CenterlineContour.X[0], 12);
        Assert.Equal(4, clone.ClosedStirrups[0].Id);
    }

    [Fact]
    public void OldJsonWithoutClosedStirrups_DeserializesToEmptyCollection()
    {
        var area = JsonSerializer.Deserialize<MaterialArea>("{\"Tag\":\"old\"}");

        Assert.NotNull(area);
        Assert.Empty(area.ClosedStirrups);
    }

    static MaterialArea Region()
    {
        var area = new MaterialArea { Category = AreaCategory.Region };
        area.Hull = Rectangle(-0.15, -0.25, 0.15, 0.25);
        area.SetWKT();
        return area;
    }

    static ClosedStirrupLoop Loop(double x0, double y0, double x1, double y1, double area, double diameter) => new()
    {
        CenterlineContour = Rectangle(x0, y0, x1, y1),
        BarAreaM2 = area,
        BarDiameterM = diameter
    };

    static Contour Rectangle(double x0, double y0, double x1, double y1) => new(
        [x0, x1, x1, x0, x0],
        [y0, y0, y1, y1, y0],
        "loop");
}
