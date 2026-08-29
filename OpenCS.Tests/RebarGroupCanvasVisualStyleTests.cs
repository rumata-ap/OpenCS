using CScore;
using OpenCS.Converters;
using OpenCS.ViewModels;
using OpenCS.Views;

using System.Windows.Media;

using Xunit;

namespace OpenCS.Tests;

public class RebarGroupCanvasVisualStyleTests
{
    [Theory]
    [InlineData(nameof(BarItem.X))]
    [InlineData(nameof(BarItem.Y))]
    [InlineData(nameof(BarItem.Diameter))]
    [InlineData(nameof(BarItem.IsSelected))]
    [InlineData(nameof(BarItem.XMm))]
    [InlineData(nameof(BarItem.YMm))]
    [InlineData(nameof(BarItem.DiameterMm))]
    public void IsBarVisualPropertyChanged_RecognizesBarVisualProperties(string propertyName)
    {
        Assert.True(RebarGroupCanvas.IsBarVisualPropertyChanged(propertyName));
    }

    [Fact]
    public void IsBarVisualPropertyChanged_IgnoresNonVisualProperties()
    {
        Assert.False(RebarGroupCanvas.IsBarVisualPropertyChanged(nameof(BarItem.Index)));
        Assert.False(RebarGroupCanvas.IsBarVisualPropertyChanged(null));
    }

    [Fact]
    public void GetCoverSegmentIndex_ReturnsFocusedEdgeIndex()
    {
        var edges = new[] { new EdgeItem(), new EdgeItem(), new EdgeItem() };

        Assert.Equal(1, RebarGroupCanvas.GetCoverSegmentIndex(edges, 3, edges[1]));
    }

    [Fact]
    public void GetCoverSegmentIndex_ReturnsMinusOneForInvalidGeometryOrUnknownEdge()
    {
        var edges = new[] { new EdgeItem(), new EdgeItem(), new EdgeItem() };

        Assert.Equal(-1, RebarGroupCanvas.GetCoverSegmentIndex(edges, 2, edges[1]));
        Assert.Equal(-1, RebarGroupCanvas.GetCoverSegmentIndex(edges, 3, new EdgeItem()));
        Assert.Equal(-1, RebarGroupCanvas.GetCoverSegmentIndex(edges, 3, null));
    }

    [Fact]
    public void ResolveBarVisualStyle_UsesDifferentMaterialColors()
    {
        var physical = RebarGroupCanvas.ResolveBarVisualStyle(
            new Material { Type = MatType.ReSteelF }, 0, false, false);
        var conditional = RebarGroupCanvas.ResolveBarVisualStyle(
            new Material { Type = MatType.ReSteelU }, 0, false, false);

        var physicalColor = ((SolidColorBrush)physical.Fill).Color;
        var conditionalColor = ((SolidColorBrush)conditional.Fill).Color;

        Assert.Equal(MatTypeToBrushConverter.GetBrush(MatType.ReSteelF).Color, physicalColor);
        Assert.Equal(MatTypeToBrushConverter.GetBrush(MatType.ReSteelU).Color, conditionalColor);
        Assert.NotEqual(physicalColor, conditionalColor);
    }

    [Theory]
    [InlineData(120.0)]
    [InlineData(-120.0)]
    public void ResolveBarVisualStyle_WithNonZeroPrestress_ShowsOuterContour(double sigSp)
    {
        var style = RebarGroupCanvas.ResolveBarVisualStyle(
            new Material { Type = MatType.ReSteelU }, sigSp, false, false);

        Assert.True(style.ShowPrestressContour);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ResolveBarVisualStyle_WithZeroOrNonFinitePrestress_HidesOuterContour(double sigSp)
    {
        var style = RebarGroupCanvas.ResolveBarVisualStyle(
            new Material { Type = MatType.ReSteelU }, sigSp, false, false);

        Assert.False(style.ShowPrestressContour);
    }

    [Fact]
    public void ResolveBarVisualStyle_SelectedBarKeepsSelectionFill()
    {
        var style = RebarGroupCanvas.ResolveBarVisualStyle(
            new Material { Type = MatType.ReSteelF }, 120, true, false);

        Assert.Equal(Color.FromRgb(37, 99, 235), ((SolidColorBrush)style.Fill).Color);
        Assert.True(style.ShowPrestressContour);
    }

    [Fact]
    public void ResolveBarVisualStyle_FirstFillBarKeepsFillModeFill()
    {
        var style = RebarGroupCanvas.ResolveBarVisualStyle(
            new Material { Type = MatType.ReSteelF }, 120, false, true);

        Assert.Equal(Color.FromRgb(14, 165, 233), ((SolidColorBrush)style.Fill).Color);
        Assert.True(style.ShowPrestressContour);
    }
}
