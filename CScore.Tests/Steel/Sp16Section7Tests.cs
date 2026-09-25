using CScore.Sp16;
using Xunit;

namespace CScore.Tests.Steel;

/// <summary>Раздел 7 и 10.4 СП 16: ручные расчёты на сварном двутавре (кПа, кН, м).</summary>
public class Sp16Section7Tests
{
    static readonly SteelMaterialProps C245 = new(240000, 360000, 245000, 370000, 2.06e8);

    static Sp16Member IBeam(double h, double b, double tw, double tf, SteelDesignParams p) =>
        Sp16Member.Create(new PolygonSection(TemplatePoints.IBeamPoints(h, b, tw, tf)), C245, p);

    [Fact]
    public void Strength_Formula5_CentralCompression()
    {
        var m = IBeam(0.300, 0.150, 0.008, 0.012, new SteelDesignParams { GammaC = 0.95 });
        var r = Sp16Section7.Strength(m, new SteelForces(-600, 0, 0, 0, 0));
        double a = 2 * 0.150 * 0.012 + 0.276 * 0.008;
        Assert.Equal(600 / (a * 240000 * 0.95), r.Utilization, 9);
        Assert.Equal("(5)", r.Formula);
    }

    [Fact]
    public void Strength_Formula5_HighStrengthSteel_UsesRuOverGammaU()
    {
        var mat = new SteelMaterialProps(440000, 520000, 460000, 540000, 2.06e8);
        var m = Sp16Member.Create(new PolygonSection(TemplatePoints.RectPoints(0.1, 0.02)), mat, new SteelDesignParams());
        var r = Sp16Section7.Strength(m, new SteelForces(500, 0, 0, 0, 0));
        Assert.Equal(500 / (0.002 * 520000 / 1.3), r.Utilization, 9);
    }

    [Fact]
    public void Stability_Formula7_AboutBothAxes()
    {
        var m = IBeam(0.300, 0.150, 0.008, 0.012, new SteelDesignParams { LefX = 6, LefY = 3 });
        var res = Sp16Section7.Stability(m, new SteelForces(-600, 0, 0, 0, 0));
        Assert.Equal(2, res.Count);
        double a = 0.005808;
        double iy = Math.Sqrt((2 * 0.012 * Math.Pow(0.150, 3) / 12 + 0.276 * Math.Pow(0.008, 3) / 12) / a);
        double lby = 3 / iy * Math.Sqrt(240000 / 2.06e8);
        Assert.Equal(3.0011, lby, 3);
        // Тип c для двутавра относительно y; табл. Д.1 при λ̄ = 3,0: φ = 0,562.
        double phiY = res[1].Variables.First(v => v.Key == "φ").Value;
        Assert.InRange(phiY, 0.560, 0.563);
        Assert.Equal(600 / (phiY * a * 240000), res[1].Utilization, 9);
        Assert.Contains("тип сечения b", res[0].Notes[0]);
    }

    [Fact]
    public void LocalStability_WebAndFlange_Tables9And10()
    {
        var m = IBeam(0.300, 0.150, 0.008, 0.012, new SteelDesignParams { LefX = 6, LefY = 3 });
        var res = Sp16Section7.LocalStability(m, new SteelForces(-600, 0, 0, 0, 0));
        var web = res.Single(r => r.Clause == "7.3.2");
        double sq = Math.Sqrt(240000 / 2.06e8);
        Assert.Equal(0.276 / 0.008 * sq, web.Applied, 9);
        double lb = web.Variables.First(v => v.Key == "λ̄").Value;
        Assert.Equal(Math.Min(1.20 + 0.35 * lb, 2.3), web.Allowable, 9);
        var fl = res.Single(r => r.Clause == "7.3.8");
        Assert.Equal(0.071 / 0.012 * sq, fl.Applied, 9);
        Assert.Equal(0.36 + 0.10 * lb, fl.Allowable, 9);
        Assert.All(res, r => Assert.Equal(CheckStatus.Ok, r.Status));
    }

    [Fact]
    public void ReducedArea_Formula31_34_WhenWebExceedsLimit()
    {
        var m = IBeam(0.600, 0.200, 0.008, 0.016, new SteelDesignParams { LefX = 2, LefY = 2 });
        var (ad, note) = Sp16Section7.EffectiveArea(m, 1000);
        double sq = Math.Sqrt(240000 / 2.06e8);
        double a = 2 * 0.2 * 0.016 + 0.568 * 0.008;
        double iy = Math.Sqrt((2 * 0.016 * Math.Pow(0.2, 3) / 12 + 0.568 * Math.Pow(0.008, 3) / 12) / a);
        double lb = 2 / iy * sq;
        double luw = 1.30 + 0.15 * lb * lb;
        double lbw = 0.568 / 0.008 * sq;
        Assert.InRange(lbw, luw, 2 * luw);
        double hd = 0.008 * (luw - (lbw / luw - 1) * (luw - 1.2 - 0.15 * lb)) / sq;
        Assert.Equal(a - (0.568 - hd) * 0.008, ad, 9);
        Assert.NotNull(note);
    }

    [Fact]
    public void Tension_Strength_AndSlenderness_Table33()
    {
        var m = IBeam(0.300, 0.150, 0.008, 0.012, new SteelDesignParams
        {
            LefX = 6, LefY = 6, TensionCategory = TensionMemberCategory.TrussChord, TensionLoad = TensionLoadKind.Static,
        });
        var f = new SteelForces(500, 0, 0, 0, 0);
        Assert.Equal(500 / (0.005808 * 240000), Sp16Section7.Strength(m, f).Utilization, 9);
        var sl = Sp16Slenderness.Check(m, f);
        Assert.Equal(2, sl.Count);
        Assert.All(sl, r => Assert.Equal(400, r.Allowable, 9));
    }

    [Fact]
    public void Slenderness_Table32_MainColumn_AlphaNotLessThanHalf()
    {
        var m = IBeam(0.300, 0.150, 0.008, 0.012, new SteelDesignParams { LefX = 6, LefY = 3 });
        var small = Sp16Slenderness.Check(m, new SteelForces(-10, 0, 0, 0, 0));
        Assert.All(small, r => Assert.Equal(180 - 60 * 0.5, r.Allowable, 9));
        var big = Sp16Slenderness.Check(m, new SteelForces(-700, 0, 0, 0, 0));
        double alpha = big[0].Variables.First(v => v.Key == "α").Value;
        Assert.True(alpha > 0.5);
        Assert.Equal(180 - 60 * alpha, big[0].Allowable, 9);
    }

    [Fact]
    public void Legacy_ParamsJson_IsMigrated()
    {
        var p = SteelDesignParams.Parse("{\"DesignLengthX\":3.0,\"DesignLengthY\":2.0,\"MuX\":2.0,\"MuY\":1.0,\"BetaM\":1.0,\"GammaM\":1.025,\"DesignLengthBit\":1.5}");
        Assert.Equal(6.0, p.LefX, 9);
        Assert.Equal(2.0, p.LefY, 9);
        Assert.Equal(1.5, p.LefB, 9);
        Assert.Equal(1.0, p.GammaC, 9);
        Assert.True(p.MigratedFromLegacy);
        var round = SteelDesignParams.Parse(p.ToJson());
        Assert.False(round.MigratedFromLegacy);
        Assert.Equal(6.0, round.LefX, 9);
    }
}
