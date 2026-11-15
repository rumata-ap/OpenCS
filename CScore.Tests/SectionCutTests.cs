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
    public void Build_UnnumberedRebar_HasNullNum()
    {
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.4, 0.6);
        var bars = section.Areas[1].Fibers;
        bars[2].Num = 0; // без номера (как у стержней параметрического сечения)
        bars[3].Num = 7;
        double ry = 0.3 - 0.05;

        var result = SectionCutBuilder.Build(
            section, new Kurvature { e0 = -0.0005 }, CalcType.C, CutMode.Free,
            p1: (-0.3, ry), p2: (0.3, ry),
            rebarThresholdM: 0.001);

        Assert.NotNull(result);
        Assert.Contains(result!.Rebars, r => r.Num == null);
        Assert.Contains(result.Rebars, r => r.Num == 7);
    }

    /// <summary>Источник σ, зависящий от точки (как поле γbt огневой проверки).</summary>
    sealed class PointStressSource : ISectionCutStressSource
    {
        public double? ConcreteSigMpa(MaterialArea area, double x, double y, double eps, CalcType calcType) => -10.0 * (1.0 + x);
        public double? RebarSigMpa(Fiber fiber, double eps, CalcType calcType) => 123.0;
    }

    [Fact]
    public void Build_WithStressSource_TakesSigmaFromSourceInsteadOfDiagram()
    {
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.4, 0.6);
        var k = new Kurvature { e0 = -0.0005, ky = 0.0, kz = 0.0 };
        double ry = 0.3 - 0.05;

        var result = SectionCutBuilder.Build(
            section, k, CalcType.C, CutMode.Free,
            p1: (-0.3, ry), p2: (0.3, ry),
            rebarThresholdM: 0.001, stressSource: new PointStressSource());

        Assert.NotNull(result);
        foreach (var sample in result!.Segments[0].Points)
        {
            Assert.Equal(-0.0005, sample.Eps!.Value, 6);
            Assert.Equal(-10.0 * (1.0 + sample.X), sample.Sig!.Value, 9);
        }
        Assert.All(result.Rebars, r => Assert.Equal(123.0, r.Sig, 9));
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

    /// <summary>
    /// Ориентация разреза по градиенту не зависит от знака кривизны: вертикальный разрез
    /// всегда идёт сверху вниз — иначе при смене знака момента эпюра не переворачивалась
    /// (начало A всегда на сжатой грани).
    /// </summary>
    [Theory]
    [InlineData(0.001)]
    [InlineData(-0.001)]
    public void Build_GradientSnapVertical_StartsAtTopRegardlessOfSign(double ky)
    {
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.4, 0.6);
        var k = new Kurvature { e0 = 0.0, ky = ky, kz = 0.0 };

        var result = SectionCutBuilder.Build(
            section, k, CalcType.C, CutMode.GradientSnap,
            p1: (0.0, 0.0), p2: null, rebarThresholdM: 0.01);

        Assert.NotNull(result);
        Assert.Equal(0.3, result!.Start.Y, 6);
        Assert.Equal(-0.3, result.End.Y, 6);
        // Сжатая грань сверху при ky < 0 (ε = e0 + ky·y) и снизу при ky > 0.
        var pts = result.Segments.SelectMany(s => s.Points).ToList();
        Assert.Equal(Math.Sign(ky) > 0, pts[0].Eps > pts[^1].Eps);
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(-0.001)]
    public void Build_GradientSnapHorizontal_StartsAtLeftRegardlessOfSign(double kz)
    {
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.4, 0.6);
        var k = new Kurvature { e0 = 0.0, ky = 0.0, kz = kz };

        var result = SectionCutBuilder.Build(
            section, k, CalcType.C, CutMode.GradientSnap,
            p1: (0.0, 0.0), p2: null, rebarThresholdM: 0.01);

        Assert.NotNull(result);
        Assert.Equal(-0.2, result!.Start.X, 6);
        Assert.Equal(0.2, result.End.X, 6);
    }

    /// <summary>
    /// За εbt2 растянутый бетон σ = 0: на эпюре — ступень у точки εbt2, а не наклонная
    /// линия от последнего отсчёта с σ = Rbt до грани.
    /// </summary>
    [Fact]
    public void Build_TensionBeyondEpsBt2_ShowsVerticalStepNotSlope()
    {
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.4, 0.6);
        // ε на нижней грани y = −0.3: 0.000153 > εbt2 = 0.00015; εbt2 — у y ≈ −0.2941.
        var k = new Kurvature { e0 = 0.0, ky = -0.00051, kz = 0.0 };

        var result = SectionCutBuilder.Build(
            section, k, CalcType.C, CutMode.GradientSnap,
            p1: (0.0, 0.0), p2: null, rebarThresholdM: 0.001);

        Assert.NotNull(result);
        var pts = result!.Segments.Single(s => s.AreaIndex == 0).Points;
        double sBreak = (0.3 + 0.00015 / 0.00051) ; // s от верхней грани до точки εbt2
        var before = pts.Last(p => p.S < sBreak - 1e-9);
        var after = pts.First(p => p.S > sBreak + 1e-9);
        Assert.True(after.S - before.S < 1e-5, $"ступень шириной {after.S - before.S} м");
        Assert.Equal(1.2, before.Sig!.Value, 6);
        Assert.Equal(0.0, after.Sig!.Value, 6);
        Assert.Equal(0.0, pts[^1].Sig!.Value, 6);
    }

    [Fact]
    public void Build_StagedSection_UsesRelativePlanePerPart()
    {
        var section = SectionCutFixtures.BuildStagedSection(0.3, 0.2);
        // Полная плоскость равна плоскости включения части 1: часть 0 видит её целиком, часть 1 — ноль.
        var baseK = SectionCutFixtures.StagedActivationPlane;

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
