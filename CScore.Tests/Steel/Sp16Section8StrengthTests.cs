using CScore.Sp16;
using Xunit;

namespace CScore.Tests.Steel;

/// <summary>8.2 СП 16: ручные расчёты на сварном двутавре 300×150×8×12 (С245).</summary>
public class Sp16Section8StrengthTests
{
    static readonly SteelMaterialProps C245 = new(240000, 360000, 245000, 370000, 2.06e8);
    static readonly double Ix = (0.150 * Math.Pow(0.300, 3) - 0.142 * Math.Pow(0.276, 3)) / 12, Wx = Ix / 0.150;

    static Sp16Member Beam(SteelDesignParams? p = null) =>
        Sp16Member.Create(new PolygonSection(TemplatePoints.IBeamPoints(0.300, 0.150, 0.008, 0.012)), C245, p ?? new SteelDesignParams());

    [Fact]
    public void Formula41_Bending()
    {
        var res = Sp16Section8Strength.Check(Beam(), new SteelForces(0, 100, 0, 0, 0));
        var r = res.Single(x => x.Formula == "(41)");
        Assert.Equal(100 / (Wx * 240000), r.Utilization, 4);
    }

    [Fact]
    public void Formula42_Shear_QSOverIt()
    {
        var res = Sp16Section8Strength.Check(Beam(), new SteelForces(0, 0, 0, 0, 200));
        var r = res.Single(x => x.Formula == "(42)");
        double s = 0.150 * 0.012 * 0.144 + 0.008 * 0.138 * 0.138 / 2;
        Assert.Equal(200 * s / (Ix * 0.008) / (0.58 * 240000), r.Utilization, 4);
    }

    [Fact]
    public void Formula44_WebCombined()
    {
        var res = Sp16Section8Strength.Check(Beam(), new SteelForces(0, 100, 0, 0, 200));
        var r = res.Single(x => x.Formula == "(44)");
        double sx = 100 * 0.138 / Ix, tau = 200 * (0.150 * 0.012 * 0.144) / (Ix * 0.008);
        Assert.Equal(0.87 * Math.Sqrt(sx * sx + 3 * tau * tau) / 240000, r.Utilization, 4);
    }

    [Fact]
    public void Formula50_Plastic_WithBetaFromFormula52()
    {
        var res = Sp16Section8Strength.Check(Beam(new SteelDesignParams { AllowPlastic = true }), new SteelForces(0, 100, 0, 0, 200));
        var r = res.Single(x => x.Formula == "(50)");
        double af = 0.150 * 0.012, aw = 0.276 * 0.008, ratio = af / aw;
        double cx = 1.12 + (1.07 - 1.12) * (ratio - 0.5) / 0.5;
        double rs = 0.58 * 240000, tau = 200 / aw;
        double beta = 1 - 0.20 / (ratio + 0.25) * Math.Pow(tau / rs, 4);
        Assert.Equal(100 / (cx * beta * Wx * 240000), r.Utilization, 4);
    }

    [Fact]
    public void Formula50_CappedByNote2OfTableE1()
    {
        // Сплошной прямоугольник не допускается 8.2.3 → проверим ограничение на двутавре при γf = 0,9: c ≤ 1,035.
        var res = Sp16Section8Strength.Check(Beam(new SteelDesignParams { AllowPlastic = true, GammaFEq = 0.9 }), new SteelForces(0, 100, 0, 0, 0));
        var r = res.Single(x => x.Formula == "(50)");
        Assert.Equal(1.035, r.Variables.First(v => v.Key == "cx").Value, 6);
    }

    [Fact]
    public void Formula46_LocalStress()
    {
        var m = Beam(new SteelDesignParams { LocalForce = 100, BearingLength = 0.1, FlangeWeldLeg = 0.006 });
        var r = Sp16Section8Strength.LocalStress(m)!;
        Assert.Equal(100 / ((0.1 + 2 * 0.018) * 0.008) / 240000, r.Utilization, 6);
    }

    [Fact]
    public void Plastic_NotApplicable_ForChannel_FallsBackToElastic()
    {
        var ch = new ChannelProfile("20П", 0.200, 0.076, 0.0052, 0.009, 0.0095, 0.004, 23.4e-4, 0);
        var m = Sp16Member.Create(new PolygonSection(ch.ToPolygonPoints(12)), C245, new SteelDesignParams { AllowPlastic = true });
        var res = Sp16Section8Strength.Check(m, new SteelForces(0, 20, 0, 0, 0));
        Assert.Contains(res, r => r.Formula == "(50)" && r.Status == CheckStatus.NotApplicable);
        Assert.Contains(res, r => r.Formula == "(41)");
    }
}
