using System.Text.Json;
using CScore;
using Xunit;

namespace CScore.Tests;

/// <summary>Проверки геометрической записи замкнутых хомутов в MaterialArea.</summary>
public sealed class StirrupTests
{
    [Fact]
    public void NewMaterialArea_HasEmptyNonNullStirrups()
    {
        var area = new MaterialArea();

        Assert.NotNull(area.Stirrups);
        Assert.Empty(area.Stirrups);
    }

    [Fact]
    public void ValidateFor_AcceptsGroupWithTwoClosedLoops()
    {
        var area = Region();
        var group = new StirrupGroup
        {
            MaterialId = 17,
            SpacingM = 0.15,
            Elements =
            [
                Loop(-0.12, -0.20, 0.12, 0.20, 0.0000503, 0.008),
                Loop(-0.05, -0.15, 0.05, 0.15, 0.0000785, 0.010)
            ]
        };

        group.ValidateFor(area);

        Assert.Equal(2, group.Elements.Count);
        Assert.Equal(0.15, group.SpacingM, 12);
        Assert.Equal(0.0000503, group.Elements[0].BarAreaM2, 12);
        Assert.Equal(0.010, group.Elements[1].BarDiameterM, 12);
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
        var group = new StirrupGroup
        {
            MaterialId = 17,
            SpacingM = spacing,
            Elements = [Loop(-0.12, -0.20, 0.12, 0.20, barArea, diameter)]
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => group.ValidateFor(Region()));
    }

    [Fact]
    public void ValidateFor_RejectsRebarAreaAndOpenLoop()
    {
        var group = new StirrupGroup
        {
            MaterialId = 17,
            SpacingM = 0.15,
            Elements = [new StirrupElement
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
        area.Stirrups.Add(new StirrupGroup
        {
            Id = 4,
            MaterialId = 17,
            SpacingM = 0.15,
            Elements = [Loop(-0.12, -0.20, 0.12, 0.20, 0.0000503, 0.008)]
        });

        var clone = area.CloneForCalc();
        clone.Stirrups[0].Elements[0].CenterlineContour.X[0] = -0.10;
        clone.Stirrups[0].Elements.Add(Loop(-0.05, -0.15, 0.05, 0.15, 0.0000785, 0.010));

        Assert.Single(area.Stirrups[0].Elements);
        Assert.Equal(-0.12, area.Stirrups[0].Elements[0].CenterlineContour.X[0], 12);
        Assert.Equal(4, clone.Stirrups[0].Id);
    }

    [Fact]
    public void OldJsonWithoutStirrups_DeserializesToEmptyCollection()
    {
        var area = JsonSerializer.Deserialize<MaterialArea>("{\"Tag\":\"old\"}");

        Assert.NotNull(area);
        Assert.Empty(area.Stirrups);
    }

    [Fact]
    public void Validate_OpenPolylineWithTwoVertices_IsAccepted()
    {
        var element = new StirrupElement
        {
            CenterlineContour = Contour.Polyline([0.0, 0.0], [-0.2, 0.2], "срез"),
            BarAreaM2 = 0.0000503,
            BarDiameterM = 0.008
        };

        element.Validate();
        Assert.False(element.IsClosed);
    }

    [Fact]
    public void Validate_DegenerateZeroLengthPolyline_Throws()
    {
        var element = new StirrupElement
        {
            CenterlineContour = Contour.Polyline([0.05, 0.05], [0.1, 0.1], "вырожденный"),
            BarAreaM2 = 0.0000503,
            BarDiameterM = 0.008
        };

        Assert.Throws<ArgumentException>(() => element.Validate());
    }

    [Fact]
    public void ValidateFor_StirrupsAreaWithHostAreaId_Throws()
    {
        var area = new MaterialArea { Id = 5, Category = AreaCategory.Stirrups, MaterialId = 17, HostAreaId = 3 };
        var group = new StirrupGroup { MaterialId = 17, SpacingM = 0.15, Elements = [VerticalCut()] };

        var ex = Assert.Throws<ArgumentException>(() => group.ValidateFor(area));
        Assert.Contains("HostAreaId", ex.Message);
    }

    [Fact]
    public void ValidateFor_StirrupsAreaWithMismatchedMaterial_Throws()
    {
        var area = new MaterialArea { Id = 5, Category = AreaCategory.Stirrups, MaterialId = 17 };
        var group = new StirrupGroup { MaterialId = 18, SpacingM = 0.15, Elements = [VerticalCut()] };

        Assert.Throws<ArgumentException>(() => group.ValidateFor(area));
    }

    [Fact]
    public void ValidateFor_StirrupsAreaWithoutHull_IsAccepted()
    {
        var area = new MaterialArea { Id = 5, Category = AreaCategory.Stirrups, MaterialId = 17 };
        var group = new StirrupGroup { MaterialId = 17, SpacingM = 0.15, Elements = [VerticalCut()] };

        group.ValidateFor(area);
    }

    [Fact]
    public void ValidateFor_LegacyGroupOnConcreteRegion_IsStillAccepted()
    {
        var area = new MaterialArea { Id = 1, Category = AreaCategory.Region, MaterialId = 2 };
        area.Hull = new Contour([-0.1, 0.1, 0.1, -0.1, -0.1], [-0.2, -0.2, 0.2, 0.2, -0.2], "hull");
        area.SetWKT();
        var group = new StirrupGroup { MaterialId = 17, SpacingM = 0.15, Elements = [VerticalCut()] };

        group.ValidateFor(area);
    }

    static StirrupElement VerticalCut() => new()
    {
        CenterlineContour = Contour.Polyline([0.0, 0.0], [-0.2, 0.2], "срез"),
        BarAreaM2 = 0.0000503,
        BarDiameterM = 0.008
    };

    static MaterialArea Region()
    {
        var area = new MaterialArea { Category = AreaCategory.Region };
        area.Hull = Rectangle(-0.15, -0.25, 0.15, 0.25);
        area.SetWKT();
        return area;
    }

    static StirrupElement Loop(double x0, double y0, double x1, double y1, double area, double diameter) => new()
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
