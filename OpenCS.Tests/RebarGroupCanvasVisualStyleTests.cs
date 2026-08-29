using CScore;
using OpenCS.Converters;
using OpenCS.Views;

using System.Windows.Media;

using Xunit;

namespace OpenCS.Tests;

public class RebarGroupCanvasVisualStyleTests
{
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
