using CScore.Sp16;
using Xunit;

namespace CScore.Tests.Steel;

/// <summary>8.5 СП 16: устойчивость стенок (8.5.1, 8.5.3–8.5.5, 8.5.8) и сжатых поясов (8.5.18, 8.5.19) балок (кПа, кН, м).</summary>
public class Sp16Section8LocalTests
{
    static readonly SteelMaterialProps C245 = new(240000, 360000, 245000, 370000, 2.06e8);
    const double E = 2.06e8, Ry = 240000, Rs = 0.58 * Ry;
    static readonly double Sq = Math.Sqrt(Ry / E);

    static Sp16Member Welded(double h, double bf, double tw, double tf, SteelDesignParams p) =>
        Sp16Member.Create(new PolygonSection(TemplatePoints.IBeamPoints(h, bf, tw, tf)), C245, p);

    /// <summary>Сварной двутавр 1000×300, стенка 8, пояса 16: hef = 0,968, λ̄w ≈ 4,13.</summary>
    static Sp16Member Tall(SteelDesignParams p) => Welded(1.0, 0.300, 0.008, 0.016, p);
    const double Hef = 0.968, Tw = 0.008;

    static Sp16CheckResult Only(IEnumerable<Sp16CheckResult> res, string clause) => res.Single(r => r.Clause == clause);

    [Fact]
    public void Tables_NodeValues()
    {
        Assert.Equal(30.0, Sp16Tables.Table12Ccr(0.5, false), 9);
        Assert.Equal(35.5, Sp16Tables.Table12Ccr(double.PositiveInfinity, false), 9);
        Assert.Equal(35.2, Sp16Tables.Table12Ccr(1.0, true), 9);
        Assert.Equal(20.3, Sp16Tables.Table14C1(0.25, 0.67), 9);
        Assert.Equal(2.01, Sp16Tables.Table15C2(4, 1.2), 9);
        Assert.Equal(1.56, Sp16Tables.Table15C2(0.5, 3.0), 9);
        Assert.Equal(45.2, Sp16Tables.Table16Ccr(1.2, 33), 9);
        Assert.Equal(33.5, Sp16Tables.Table16Ccr(0.85, 30), 9);
        Assert.Equal(0.197, Sp16Tables.Table18Alpha(0.5, 3.0), 9);
        Assert.Equal((0.203 + 0.186) / 2, Sp16Tables.Table18Alpha(0.55, 2.2), 9);
    }

    [Fact]
    public void Rolled30B1_StockyWeb_Exempt_851()
    {
        var profile = new SteelProfile { Kind = SteelProfileKind.IBeam, Fabrication = SteelFabrication.Rolled, H = 0.296, Bf1 = 0.140, Tf1 = 0.0085, Tw = 0.0058, R = 0.015 };
        var m = Sp16Member.Create(new PolygonSection(new IBeamProfile("30Б1", 0.296, 0.140, 0.0058, 0.0085, 0.015, 0, 41.92e-4).ToPolygonPoints()),
            C245, new SteelDesignParams { Profile = profile });
        var r = Only(Sp16Section8Local.Check(m, new SteelForces(0, -60, 0, 0, 40)), "8.5.1");
        Assert.Equal(CheckStatus.Ok, r.Status);
        Assert.Equal((0.296 - 2 * 0.0085 - 2 * 0.015) / 0.0058 * Sq, r.Applied, 9);
        Assert.Equal(3.5, r.Allowable, 9);
    }

    [Fact]
    public void SlenderWeb_NoRibs_Fails_851()
    {
        var r = Only(Sp16Section8Local.Check(Tall(new SteelDesignParams()), new SteelForces(0, -800, 0, 0, 300)), "8.5.1");
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Equal(Hef / Tw * Sq, r.Applied, 9);
        Assert.Contains(r.Notes, n => n.Contains("задайте шаг рёбер"));
    }

    [Fact]
    public void OneSidedWelds_WithLocalLoad_Limits()
    {
        var one = Only(Sp16Section8Local.Check(Tall(new SteelDesignParams { OneSidedFlangeWelds = true }), new SteelForces(0, -800, 0, 0, 0)), "8.5.1");
        Assert.Equal(3.2, one.Allowable, 9);
        var loc = Only(Sp16Section8Local.Check(Tall(new SteelDesignParams { LocalForce = 100, BearingLength = 0.2 }), new SteelForces(0, -800, 0, 0, 0)), "8.5.1");
        Assert.Equal(2.5, loc.Allowable, 9);
    }

    [Fact]
    public void Ribs_NoLocalLoad_Formula80()
    {
        var m = Tall(new SteelDesignParams { RibSpacing = 2.0 });
        var r = Only(Sp16Section8Local.Check(m, new SteelForces(0, -800, 0, 0, 300)), "8.5.3");
        double lw = Hef / Tw * Sq;
        double sigma = 800 * (Hef / 2) / m.S.Ix;                                                    // (78)
        double tau = 300 / (Tw * Hef);                                                               // (79), hw = hef
        double delta = 0.8 * (0.300 / Hef) * Math.Pow(0.016 / Tw, 3);                               // (84), β = 0,8
        double ccr = 31.5 + (33.3 - 31.5) * (delta - 1.0);                                           // табл. 12, 1 < δ < 2
        double sigmaCr = ccr * Ry / (lw * lw);                                                       // (81)
        double mu = 2.0 / Hef;
        double tauCr = 10.3 * (1 + 0.76 / (mu * mu)) * Rs / (lw * lw);                               // (83), d = hef
        double expected = Math.Sqrt(Math.Pow(sigma / sigmaCr, 2) + Math.Pow(tau / tauCr, 2));
        Assert.Equal("(80)", r.Formula);
        Assert.Equal(expected, r.Utilization, 9);
    }

    [Fact]
    public void Ribs_LocalLoad_ShortPanel_OneCheck()
    {
        var p = new SteelDesignParams { RibSpacing = 0.7, LocalForce = 200, BearingLength = 0.2, FlangeWeldLeg = 0.006 };
        var m = Tall(p);
        var r = Only(Sp16Section8Local.Check(m, new SteelForces(0, -800, 0, 0, 300)), "8.5.3");
        double lw = Hef / Tw * Sq, ah = 0.7 / Hef;
        double lef = 0.2 + 2 * (0.016 + 0.006);
        double sloc = 200 / (lef * Tw);                                                              // (47)
        double rho = 1.04 * lef / Hef;
        double delta = 0.8 * (0.300 / Hef) * Math.Pow(0.016 / Tw, 3);
        double c1 = Sp16Tables.Table14C1(rho, ah), c2 = Sp16Tables.Table15C2(delta, ah);
        double sigma = 800 * (Hef / 2) / m.S.Ix, tau = 300 / (Tw * Hef);
        double sigmaCr = Sp16Tables.Table12Ccr(delta, false) * Ry / (lw * lw);
        double slocCr = c1 * c2 * Ry / (lw * lw);                                                    // (82)
        double lambdaD = 0.7 / Tw * Sq, mu = Hef / 0.7;                                             // d = a < hef
        double tauCr = 10.3 * (1 + 0.76 / (mu * mu)) * Rs / (lambdaD * lambdaD);
        double expected = Math.Sqrt(Math.Pow(sigma / sigmaCr + sloc / slocCr, 2) + Math.Pow(tau / tauCr, 2));
        Assert.Equal(expected, r.Utilization, 9);
    }

    [Fact]
    public void Ribs_LocalLoad_LongPanel_TwoChecks()
    {
        var p = new SteelDesignParams { RibSpacing = 2.5, LocalForce = 200, BearingLength = 0.2, FlangeWeldLeg = 0.006 };
        var res = Sp16Section8Local.Check(Tall(p), new SteelForces(0, -800, 0, 0, 300)).Where(r => r.Clause == "8.5.3").ToList();
        Assert.Equal(2, res.Count);
        Assert.Equal(0.67 * Hef, res[0].Variables.Single(v => v.Key == "a1").Value, 9);             // a/hef > 1,33
        Assert.Equal(84.7, res[1].Variables.Single(v => v.Key == "ccr").Value, 9);                  // a/hef > 2 → 2
    }

    [Fact]
    public void LocalLoadOnTensionFlange_SeparateChecks()
    {
        var p = new SteelDesignParams { RibSpacing = 0.7, LocalForce = 200, BearingLength = 0.2 };
        var res = Sp16Section8Local.Check(Tall(p), new SteelForces(0, 800, 0, 0, 300)).Where(r => r.Clause == "8.5.3").ToList();
        Assert.Equal(2, res.Count);
        Assert.Contains("σ и τ", res[0].Description);
        Assert.Contains("σloc и τ", res[1].Description);
    }

    [Fact]
    public void VerySlenderWeb_NeedsLongitudinalRib()
    {
        var m = Welded(1.0, 0.300, 0.005, 0.016, new SteelDesignParams { RibSpacing = 1.0 });
        double mx = 0.9 * Ry * m.S.Ix / (0.968 / 2);
        var r = Only(Sp16Section8Local.Check(m, new SteelForces(0, -mx, 0, 0, 0)), "8.5.3");
        Assert.Equal(CheckStatus.Fail, r.Status);
        Assert.Equal(6 * Math.Sqrt(1 / 0.9), r.Allowable, 9);
    }

    [Fact]
    public void CompressedFlange_Formula97()
    {
        var m = Tall(new SteelDesignParams());
        var r = Only(Sp16Section8Local.Check(m, new SteelForces(0, -800, 0, 0, 0)), "8.5.18");
        double lf = (0.300 - Tw) / 2 / 0.016 * Sq;
        double sc = 800 / m.S.Wx(true);
        Assert.Equal("(97)", r.Formula);
        Assert.Equal(lf, r.Applied, 9);
        Assert.Equal(0.5 * Math.Sqrt(Ry / sc), r.Allowable, 9);
    }

    [Fact]
    public void BoxFlange_Formula98()
    {
        var m = Sp16Member.Create(new PolygonSection(TemplatePoints.RectPoints(0.300, 0.400),
            [TemplatePoints.RectPoints(0.300 - 2 * 0.010, 0.400 - 2 * 0.016)]), C245, new SteelDesignParams());
        var r = Only(Sp16Section8Local.Check(m, new SteelForces(0, -100, 0, 0, 0)), "8.5.18");
        Assert.Equal("(98)", r.Formula);
        Assert.Equal(0.280 / 0.016 * Sq, r.Applied, 9);
        Assert.Equal(1.5 * Math.Sqrt(Ry / (100 / m.S.Wx(true))), r.Allowable, 9);
    }

    [Fact]
    public void Plastic_Web86_And_Flange99()
    {
        var m = Welded(0.600, 0.250, 0.008, 0.014, new SteelDesignParams { AllowPlastic = true });
        var res = Sp16Section8Local.Check(m, new SteelForces(0, -400, 0, 0, 200));
        double hw = 0.600 - 2 * 0.014, lw = hw / 0.008 * Sq;
        double aw = hw * 0.008, tauR = 200 / aw / Rs;
        double alpha = Sp16Tables.Table18Alpha(tauR, lw);
        double cap = Ry * hw * hw * 0.008 * (0.250 * 0.014 / aw + alpha);
        var w = Only(res, "8.5.8");
        Assert.Equal("(86)", w.Formula);
        Assert.Equal(400 / cap, w.Utilization, 9);
        var fl = Only(res, "8.5.19");
        Assert.Equal("(99)", fl.Formula);
        Assert.Equal(0.17 + 0.06 * lw, fl.Allowable, 9);
    }

    [Fact]
    public void Channel_FlangeNotApplicable()
    {
        var pts = new List<(double X, double Y)> { (0, 0), (0.08, 0), (0.08, 0.01), (0.006, 0.01), (0.006, 0.19), (0.08, 0.19), (0.08, 0.2), (0, 0.2) };
        var m = Sp16Member.Create(new PolygonSection(pts), C245, new SteelDesignParams());
        var r = Only(Sp16Section8Local.Check(m, new SteelForces(0, -10, 0, 0, 0)), "8.5.18");
        Assert.Equal(CheckStatus.NotApplicable, r.Status);
    }
}
