using CScore;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Построение геометрии элементов поперечного армирования.</summary>
public sealed class StirrupGeometryBuilderTests
{
    static MaterialArea Beam()
    {
        var area = new MaterialArea { Id = 3, Category = AreaCategory.Region, MaterialId = 2 };
        area.Hull = new Contour([-0.15, 0.15, 0.15, -0.15, -0.15], [-0.25, -0.25, 0.25, 0.25, -0.25], "hull");
        area.SetWKT();
        return area;
    }

    [Fact]
    public void BuildOffsetLoop_ProducesClosedElementInsideHull()
    {
        var element = StirrupGeometryBuilder.BuildOffsetLoop(Beam(), 0.03, 0.008, out var error);

        Assert.Null(error);
        Assert.NotNull(element);
        Assert.True(element!.IsClosed);
        Assert.Equal(StirrupElementKind.OffsetLoop, element.Source!.Kind);
        Assert.Equal(3, element.Source.AnchorAreaId);
        Assert.Equal(0.12, element.CenterlineContour.X.Max(), 9);
    }

    [Fact]
    public void BuildOffsetLoop_WithExcessiveOffset_ReturnsError()
    {
        var element = StirrupGeometryBuilder.BuildOffsetLoop(Beam(), 0.20, 0.008, out var error);

        Assert.Null(element);
        Assert.NotNull(error);
    }

    [Fact]
    public void BuildCuts_Vertical_SpansOffsetLineHeight()
    {
        var cuts = StirrupGeometryBuilder.BuildCuts(Beam(), StirrupCutDirection.Vertical, 0.0, 0.03, 0.008, out var error);

        Assert.Null(error);
        var cut = Assert.Single(cuts);
        Assert.False(cut.IsClosed);
        Assert.Equal(-0.22, cut.CenterlineContour.Y.Min(), 9);
        Assert.Equal(0.22, cut.CenterlineContour.Y.Max(), 9);
        Assert.Equal(StirrupCutDirection.Vertical, cut.Source!.Direction);
    }

    [Fact]
    public void BuildCuts_Horizontal_SpansOffsetLineWidth()
    {
        var cuts = StirrupGeometryBuilder.BuildCuts(Beam(), StirrupCutDirection.Horizontal, 0.0, 0.03, 0.008, out _);

        var cut = Assert.Single(cuts);
        Assert.Equal(-0.12, cut.CenterlineContour.X.Min(), 9);
        Assert.Equal(0.12, cut.CenterlineContour.X.Max(), 9);
    }

    [Fact]
    public void BuildCuts_OutsideContour_ReturnsEmptyWithError()
    {
        var cuts = StirrupGeometryBuilder.BuildCuts(Beam(), StirrupCutDirection.Vertical, 0.5, 0.03, 0.008, out var error);

        Assert.Empty(cuts);
        Assert.NotNull(error);
    }

    [Fact]
    public void BuildCuts_ThroughNotchOfConcaveContour_ReturnsTwoElements()
    {
        var area = new MaterialArea { Id = 4, Category = AreaCategory.Region, MaterialId = 2 };
        area.Hull = new Contour(
            [-0.15, 0.15, 0.15, 0.05, 0.05, -0.05, -0.05, -0.15, -0.15],
            [-0.25, -0.25, 0.25, 0.25, 0.0, 0.0, 0.25, 0.25, -0.25], "hull");
        area.SetWKT();

        var cuts = StirrupGeometryBuilder.BuildCuts(area, StirrupCutDirection.Horizontal, 0.1, 0.02, 0.008, out var error);

        Assert.Null(error);
        Assert.Equal(2, cuts.Count);
    }

    [Fact]
    public void Translate_ShiftsGeometryAndRecordsSource()
    {
        var source = StirrupGeometryBuilder.BuildOffsetLoop(Beam(), 0.03, 0.008, out _)!;

        var copy = StirrupGeometryBuilder.Translate(source, 0.05, 0.0, baseIndex: 0);

        Assert.Equal(source.CenterlineContour.X[0] + 0.05, copy.CenterlineContour.X[0], 9);
        Assert.Equal(0.05, copy.Source!.Dx!.Value, 9);
        Assert.Equal(0, copy.Source.BaseIndex);
    }

    [Fact]
    public void BuildOffsetLoop_AnchorWithHoles_IsRejected()
    {
        var area = Beam();
        area.Contours.Add(new Contour(
            [-0.05, 0.05, 0.05, -0.05, -0.05], [-0.05, -0.05, 0.05, 0.05, -0.05], "hole")
            { Type = ContourType.Hole });

        var element = StirrupGeometryBuilder.BuildOffsetLoop(area, 0.03, 0.008, out var error);

        Assert.Null(element);
        Assert.NotNull(error);
    }

    [Fact]
    public void BuildCuts_AnchorWithHoles_IsRejected()
    {
        var area = Beam();
        area.Contours.Add(new Contour(
            [-0.05, 0.05, 0.05, -0.05, -0.05], [-0.05, -0.05, 0.05, 0.05, -0.05], "hole")
            { Type = ContourType.Hole });

        var cuts = StirrupGeometryBuilder.BuildCuts(area, StirrupCutDirection.Vertical, 0.0, 0.03, 0.008, out var error);

        Assert.Empty(cuts);
        Assert.NotNull(error);
    }

    [Fact]
    public void BarArea_MatchesCircleFormula()
    {
        Assert.Equal(Math.PI * 0.008 * 0.008 / 4.0, StirrupGeometryBuilder.BarArea(0.008), 12);
    }
}
