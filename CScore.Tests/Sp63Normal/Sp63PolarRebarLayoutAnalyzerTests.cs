using CScore;
using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>Проверки равномерной полярной раскладки для приложения Д.</summary>
public sealed class Sp63PolarRebarLayoutAnalyzerTests
{
    static Material Steel() => Sp63NormalFixtures.Rebar(2, 340_000.0, 340_000.0);

    static CrossSection Circle(int bars = 8, double centerX = 0.0,
        Func<int, double>? angle = null, Func<int, double>? area = null,
        Func<int, double>? radius = null, double sigSp = 0.0,
        Func<int, Material>? material = null)
    {
        var section = Sp63NormalFixtures.CircleSection(0.25);
        Sp63NormalFixtures.AddPolarBars(section, bars, 0.20, 3.0e-4, Steel(),
            centerX: centerX, angleShiftFraction: angle, areaFactor: area,
            radiusFactor: radius, sigSp: sigSp, materialByIndex: material);
        return section;
    }

    static Sp63CircularProfileAnalysis Analyze(CrossSection section) =>
        Sp63PolarRebarLayoutAnalyzer.Analyze(section, CalcType.C, 0.0, 0.0);

    static string SingleCode(Sp63CircularProfileAnalysis analysis)
    {
        Assert.Null(analysis.Profile);
        return Assert.Single(analysis.Messages).Code;
    }

    [Fact]
    public void EightUniformBars_AreAccepted()
    {
        var p = Assert.IsType<Sp63CircularRebarProfile>(Analyze(Circle()).Profile);

        Assert.Equal(8, p.BarCount);
        Assert.Equal(0.0024, p.TotalArea, 12);
        Assert.Equal(0.20, p.RadiusRs, 12);
        Assert.Equal(340_000.0, p.Rs);
        Assert.True(p.AngularStepDeviation < 1e-9);
        Assert.True(p.CenterOffset < 1e-9);
    }

    [Fact]
    public void SevenBars_AreAccepted() =>
        Assert.NotNull(Analyze(Circle(bars: 7)).Profile);

    [Fact]
    public void SixBars_AreRejected() =>
        Assert.Equal("circular_insufficient_bars", SingleCode(Analyze(Circle(bars: 6))));

    [Fact]
    public void OffsetBarCircle_IsRejected() =>
        Assert.Equal("circular_rebar_not_centered",
            SingleCode(Analyze(Circle(centerX: 0.01))));

    [Fact]
    public void UnequalAreas_AreRejected() =>
        // Чередование сохраняет симметрию, центр раскладки не смещается.
        Assert.Equal("circular_rebar_unequal_areas",
            SingleCode(Analyze(Circle(area: i => i % 2 == 0 ? 1.05 : 0.95))));

    [Fact]
    public void UnequalRadii_AreRejected() =>
        Assert.Equal("circular_rebar_not_on_circle",
            SingleCode(Analyze(Circle(radius: i => i % 2 == 0 ? 1.05 : 0.95))));

    [Fact]
    public void NonUniformAngularStep_IsRejected() =>
        // Противоположные стержни 0 и 4 сдвинуты одинаково: центр остаётся в начале координат.
        Assert.Equal("circular_rebar_non_uniform",
            SingleCode(Analyze(Circle(angle: i => i is 0 or 4 ? 0.1 : 0.0))));

    [Fact]
    public void PrestressedRebar_IsRejected() =>
        Assert.Equal("prestressed_rebar", SingleCode(Analyze(Circle(sigSp: 100_000.0))));

    [Fact]
    public void MixedResistance_IsRejected() =>
        Assert.Equal("mixed_rebar_resistance", SingleCode(Analyze(Circle(
            material: i => i % 2 == 0 ? Steel() : Sp63NormalFixtures.Rebar(3, 435_000.0, 435_000.0)))));
}
