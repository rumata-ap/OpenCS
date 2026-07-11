using Xunit;

namespace CScore.Tests;

public class SectionCutDiagramStyleTests
{
    [Theory]
    [InlineData(0.001, true)]
    [InlineData(-0.001, false)]
    [InlineData(0.0, false)]
    [InlineData(1e-15, false)]
    public void CurveIsPositive_MatchesSpec(double v, bool expected)
        => Assert.Equal(expected, SectionCutDiagramStyle.CurveIsPositive(v));

    [Fact]
    public void CurveStrokeRgb_Positive_IsBlueDark()
    {
        var (r, g, b) = SectionCutDiagramStyle.CurveStrokeRgb(1.0);
        Assert.Equal(0x00, r);
        Assert.Equal(0x44, g);
        Assert.Equal(0xCC, b);
    }

    [Fact]
    public void CurveStrokeRgb_NonPositive_IsRedDark()
    {
        var (r, g, b) = SectionCutDiagramStyle.CurveStrokeRgb(-1.0);
        Assert.Equal(0xCC, r);
        Assert.Equal(0x00, g);
        Assert.Equal(0x00, b);
    }

    [Fact]
    public void SplitBySign_SplitsAtZeroCrossing_SharingBoundaryIndex()
    {
        // values: 0:+ 1:+ 2:- 3:-  → куски [0,3) positive и [2,4) negative (общая вершина 2)
        var parts = SectionCutDiagramStyle.SplitBySign(new[] { 1.0, 0.5, -0.5, -1.0 });
        Assert.Equal(2, parts.Count);
        Assert.Equal(0, parts[0].Start);
        Assert.Equal(3, parts[0].EndExclusive);
        Assert.True(parts[0].Positive);
        Assert.Equal(2, parts[1].Start);
        Assert.Equal(4, parts[1].EndExclusive);
        Assert.False(parts[1].Positive);
    }

    [Fact]
    public void BuildSignedFillCurves_InsertsZeroAtSignChange()
    {
        var s = new[] { 0.0, 1.0, 2.0, 3.0 };
        var v = new[] { 1.0, 0.5, -0.5, -1.0 };
        var regions = SectionCutDiagramStyle.BuildSignedFillCurves(s, v);
        Assert.Equal(2, regions.Count);

        Assert.True(regions[0].Positive);
        Assert.Equal(0.0, regions[0].Curve[0].S, 6);
        Assert.Equal(1.0, regions[0].Curve[0].V, 6);
        Assert.Equal(0.0, regions[0].Curve[^1].V, 6);
        Assert.Equal(1.5, regions[0].Curve[^1].S, 6);

        Assert.False(regions[1].Positive);
        Assert.Equal(0.0, regions[1].Curve[0].V, 6);
        Assert.Equal(1.5, regions[1].Curve[0].S, 6);
        Assert.Equal(3.0, regions[1].Curve[^1].S, 6);
        Assert.Equal(-1.0, regions[1].Curve[^1].V, 6);
    }

    [Fact]
    public void ZeroCrossingT_MidpointForSymmetric()
        => Assert.Equal(0.5, SectionCutDiagramStyle.ZeroCrossingT(1.0, -1.0), 6);
}
