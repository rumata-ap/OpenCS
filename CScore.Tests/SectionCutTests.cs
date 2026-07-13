using System;
using Xunit;

namespace CScore.Tests;

public class SectionCutTests
{
    [Fact]
    public void Build_HorizontalFreeCutThroughRectangle_ReturnsSingleContinuousSegment()
    {
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.4, 0.6);
        var k = new Kurvature { e0 = -0.0005, ky = 0.0, kz = 0.0 };

        var result = SectionCutBuilder.Build(
            section, k, CalcType.C, CutMode.Free,
            p1: (-0.3, 0.0), p2: (0.3, 0.0),
            rebarThresholdM: 0.02);

        Assert.NotNull(result);
        Assert.Single(result!.Segments);
        Assert.Equal(0, result.Segments[0].AreaIndex);
        Assert.Equal(-0.2, result.Start.X, 3);
        Assert.Equal(0.2, result.End.X, 3);

        foreach (var sample in result.Segments[0].Points)
            Assert.Equal(-0.0005, sample.Eps!.Value, 6);
    }

    [Fact]
    public void Build_CutThroughTopRebarRow_ReturnsTwoRebarMarkers()
    {
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.4, 0.6);
        var k = new Kurvature { e0 = -0.0005, ky = 0.0, kz = 0.0 };
        double ry = 0.3 - 0.05; // из BuildReinforcedRectangle: height/2 - cover

        var result = SectionCutBuilder.Build(
            section, k, CalcType.C, CutMode.Free,
            p1: (-0.3, ry), p2: (0.3, ry),
            rebarThresholdM: 0.001);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Rebars.Count);
    }

    [Fact]
    public void Build_CutThroughHollowRectangle_ReturnsGapSegmentInsideHole()
    {
        var section = SectionCutFixtures.BuildHollowRectangle(0.6, 0.2);
        var k = new Kurvature { e0 = -0.0003, ky = 0.0, kz = 0.0 };

        var result = SectionCutBuilder.Build(
            section, k, CalcType.C, CutMode.Free,
            p1: (-0.4, 0.0), p2: (0.4, 0.0),
            rebarThresholdM: 0.01);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Segments.Count);
        Assert.Equal(0, result.Segments[0].AreaIndex);
        Assert.Null(result.Segments[1].AreaIndex);
        Assert.Equal(0, result.Segments[2].AreaIndex);
    }

    [Fact]
    public void Build_CutThroughTwoMaterialStack_ShowsStressJumpAtBoundary()
    {
        var section = SectionCutFixtures.BuildTwoMaterialStack(0.3, 0.2);
        var k = new Kurvature { e0 = -0.0002, ky = 0.0, kz = 0.0 }; // упругая зона обоих материалов

        var result = SectionCutBuilder.Build(
            section, k, CalcType.C, CutMode.Free,
            p1: (0.0, -0.1), p2: (0.0, 0.5),
            rebarThresholdM: 0.01);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Segments.Count);
        double sigLower = result.Segments[0].Points[^1].Sig!.Value;
        double sigUpper = result.Segments[1].Points[0].Sig!.Value;
        Assert.NotEqual(sigLower, sigUpper, 3);
    }

    [Fact]
    public void Build_GradientSnapWithZeroGradient_FallsBackToHorizontal()
    {
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.4, 0.6);
        var k = new Kurvature { e0 = -0.0005, ky = 0.0, kz = 0.0 };

        var result = SectionCutBuilder.Build(
            section, k, CalcType.C, CutMode.GradientSnap,
            p1: (0.0, 0.0), p2: null,
            rebarThresholdM: 0.01);

        Assert.NotNull(result);
        Assert.Equal(result!.Start.Y, result.End.Y, 6);
        Assert.NotEqual(result.Start.X, result.End.X, 3);
    }

    [Fact]
    public void Build_GradientSnapWithVerticalGradient_ProducesVerticalCut()
    {
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.4, 0.6);
        var k = new Kurvature { e0 = 0.0, ky = 1.0, kz = 0.0 };

        var result = SectionCutBuilder.Build(
            section, k, CalcType.C, CutMode.GradientSnap,
            p1: (0.0, 0.0), p2: null,
            rebarThresholdM: 0.01);

        Assert.NotNull(result);
        Assert.Equal(result!.Start.X, result.End.X, 6);
        Assert.NotEqual(result.Start.Y, result.End.Y, 3);
    }

    [Fact]
    public void Build_TwoStageSection_UsesDifferentKurvaturePerStage()
    {
        var section = SectionCutFixtures.BuildTwoStageSection(0.3, 0.2);
        var baseK = new Kurvature { e0 = 0.0, ky = 0.0, kz = 0.0 };

        var result = SectionCutBuilder.Build(
            section, baseK, CalcType.C, CutMode.Free,
            p1: (0.0, -0.1), p2: (0.0, 0.5),
            rebarThresholdM: 0.01);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Segments.Count);
        var stage1Sample = result.Segments[0].Points[0];
        var stage2Sample = result.Segments[1].Points[0];
        Assert.Equal(-0.0001, stage1Sample.Eps!.Value, 6);
        Assert.Equal(0.0, stage2Sample.Eps!.Value, 6);
    }

    [Fact]
    public void Build_LineOutsideSection_ReturnsNull()
    {
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.4, 0.6);
        var k = new Kurvature { e0 = -0.0005 };

        var result = SectionCutBuilder.Build(
            section, k, CalcType.C, CutMode.Free,
            p1: (5.0, 5.0), p2: (5.0, 6.0), // вертикальная прямая x=5 — сечение шириной 0.4 м в неё не попадает даже продлённая
            rebarThresholdM: 0.01);

        Assert.Null(result);
    }

    [Fact]
    public void Build_CutNearRebarRow_ReturnsNearbyRebarsOutsideThreshold()
    {
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.4, 0.6);
        var k = new Kurvature { e0 = -0.0005, ky = 0.0, kz = 0.0 };
        double ry = 0.3 - 0.05;

        var result = SectionCutBuilder.Build(
            section, k, CalcType.C, CutMode.Free,
            p1: (-0.3, ry + 0.002), p2: (0.3, ry + 0.002),
            rebarThresholdM: 0.001);

        Assert.NotNull(result);
        Assert.Empty(result!.Rebars);
        Assert.Equal(2, result.NearbyRebars.Count);
    }

    [Fact]
    public void Build_FreeCutWithCoincidentPoints_Throws()
    {
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.4, 0.6);
        var k = new Kurvature { e0 = -0.0005 };

        Assert.Throws<ArgumentException>(() => SectionCutBuilder.Build(
            section, k, CalcType.C, CutMode.Free,
            p1: (0.0, 0.0), p2: (0.0, 0.0),
            rebarThresholdM: 0.01));
    }
}
