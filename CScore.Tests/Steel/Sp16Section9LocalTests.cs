using CScore.Sp16;
using Xunit;

namespace CScore.Tests.Steel;

/// <summary>9.4 СП 16: устойчивость стенок (табл. 22, 9.4.3) и поясов (табл. 23) внецентренно сжатых элементов — кПа, кН, м.</summary>
public class Sp16Section9LocalTests
{
    static readonly SteelMaterialProps C245 = new(240000, 360000, 245000, 370000, 2.06e8);
    const double Ry = 240000, E = 2.06e8;
    static readonly double Sq = Math.Sqrt(Ry / E);

    // Сварной двутавр 300×150×8×12.
    const double H = 0.300, B = 0.150, Tw = 0.008, Tf = 0.012;
    static readonly double A = 2 * B * Tf + (H - 2 * Tf) * Tw;
    static readonly double Ix = (B * Math.Pow(H, 3) - (B - Tw) * Math.Pow(H - 2 * Tf, 3)) / 12, Wx = Ix / (H / 2);

    static Sp16Member Beam(SteelDesignParams p) =>
        Sp16Member.Create(new PolygonSection(TemplatePoints.IBeamPoints(H, B, Tw, Tf)), C245, p);

    static Sp16Member Box(SteelDesignParams p) =>
        Sp16Member.Create(new PolygonSection(TemplatePoints.RectPoints(0.300, 0.400),
            [TemplatePoints.RectPoints(0.300 - 2 * 0.010, 0.400 - 2 * 0.016)]), C245, p);

    static double Var(Sp16CheckResult r, string name) => r.Variables.First(v => v.Key == name).Value;
    static Sp16CheckResult WebOf(List<Sp16CheckResult> res) => res.First(r => r.Description.Contains("стенки"));
    static List<Sp16CheckResult> FlangesOf(List<Sp16CheckResult> res) => res.Where(r => r.Description.Contains("пояса")).ToList();

    [Theory]
    [InlineData(1.0, 10.2)]
    [InlineData(1.5, 17.75)]
    [InlineData(2.0, 30.0)]
    public void Table17(double alpha, double ccr) => Assert.Equal(ccr, Sp16Tables.Table17Ccr(alpha), 9);

    // ── Табл. 22 ──

    [Fact]
    public void Web_Type1_Formula125_Increased943()
    {
        var p = new SteelDesignParams { LefX = 6, LefY = 1.0 };
        var m = Beam(p);
        var r = WebOf(Sp16Section9Local.Check(m, new SteelForces(-300, 60, 0, 0, 0)));
        Assert.Contains(r.Notes, n => n.StartsWith("тип 1"));
        double lbx = m.LambdaBar(true);
        Assert.True(lbx < 2);
        // σ1, σ2 у границ стенки (hef = hw у сварного), β = 0 при Q = 0.
        double y = (H - 2 * Tf) / 2, s1 = 300 / A + 60 * y / Ix, s2 = 300 / A - 60 * y / Ix, alpha = (s1 - s2) / s1;
        double ccr = Sp16Tables.Table17Ccr(alpha);
        double l2 = Math.Min(1.42 * Math.Sqrt(ccr * Ry / (2 * s1)), 0.7 + 2.4 * alpha);
        double l1 = 1.3 + 0.15 * lbx * lbx;
        double phiE = Sp16Section9.PhiE(m, 300, 60, true).PhiE, ratio = 300 / (phiE * A * Ry);
        double expected = ratio < 0.8 ? l2 : ratio <= 1 ? l1 + 5 * (l2 - l1) * (1 - ratio) : l1;
        Assert.Equal(l1, Var(r, "λ̄uw1"), 9);
        Assert.Equal(expected, r.Allowable, 9);
        Assert.Equal("9.4.3", r.Clause);
        Assert.Equal((H - 2 * Tf) / Tw * Sq, r.Applied, 9);
    }

    [Fact]
    public void Web_Type1_SmallMx_InterpolatesWith732()
    {
        var m = Beam(new SteelDesignParams { LefX = 6, LefY = 1.0 });
        var r = WebOf(Sp16Section9Local.Check(m, new SteelForces(-500, 15, 0, 0, 0)));
        double mx = 15.0 / 500 * A / Wx, lbx = m.LambdaBar(true);
        Assert.InRange(mx, 0, 1);
        double lc = Sp16Tables.WebLimitCentral(m.S, m.GoverningPhi()!.Value.LambdaBar, out _)!.Value;
        double l1 = 1.3 + 0.15 * lbx * lbx;
        Assert.Equal(lc + (l1 - lc) * mx, r.Allowable, 9);
        Assert.Contains("прим. 1", r.Formula);
    }

    [Fact]
    public void Web_Type2_WhenOutOfPlaneGoverns()
    {
        // Большая гибкость из плоскости: cφy ≤ φe — тип 2, (127).
        var m = Beam(new SteelDesignParams { LefX = 3, LefY = 6 });
        var r = WebOf(Sp16Section9Local.Check(m, new SteelForces(-200, 60, 0, 0, 0)));
        Assert.Contains(r.Notes, n => n.StartsWith("тип 2"));
        double alpha = Var(r, "α"), s1 = Var(r, "σ1");
        Assert.True(alpha >= 1);
        double ccr = Sp16Tables.Table17Ccr(alpha);
        Assert.Equal(Math.Min(1.42 * Math.Sqrt(ccr * Ry / (2 * s1)), 0.7 + 2.4 * alpha), r.Allowable, 9);
        Assert.Equal("табл. 22 (127)", r.Formula);
    }

    [Fact]
    public void Web_Type5_WeakAxisBending()
    {
        var m = Beam(new SteelDesignParams { LefX = 6, LefY = 2 });
        var r = WebOf(Sp16Section9Local.Check(m, new SteelForces(-300, 0, 5, 0, 0)));
        double my = 5.0 / 300 * A / m.S.WyMin;
        Assert.True(my >= 1);
        Assert.Equal(Math.Min(2 * Math.Sqrt(A * Ry / 300), 5.5), r.Allowable, 9);
        Assert.Equal("табл. 22 (130)", r.Formula);
    }

    [Fact]
    public void Web_Tee_FlangeCompressed_Type4_StemCompressed_NotApplicable()
    {
        var m = Sp16Member.Create(new PolygonSection(TemplatePoints.TeePoints(0.200, 0.150, 0.010, 0.014)), C245,
            new SteelDesignParams { LefX = 3, LefY = 3 });
        var r = WebOf(Sp16Section9Local.Check(m, new SteelForces(-200, -5, 0, 0, 0)));   // Mx < 0 сжимает полку (сверху)
        double lbx = Math.Clamp(m.LambdaBar(true), 0.8, 4), ratio = Math.Clamp(0.200 / m.S.Hef, 1, 2);
        Assert.Equal((0.4 + 0.07 * lbx) * (1 + 0.25 * Math.Sqrt(2 - ratio)), r.Allowable, 9);
        Assert.Equal("табл. 22 (129)", r.Formula);
        var r2 = WebOf(Sp16Section9Local.Check(m, new SteelForces(-200, 5, 0, 0, 0)));
        Assert.Equal(CheckStatus.NotApplicable, r2.Status);
    }

    [Fact]
    public void Web_Channel_Type3()
    {
        var ch = new ChannelProfile("20П", 0.200, 0.076, 0.0052, 0.009, 0.0095, 0.004, 23.4e-4, 0);
        var m = Sp16Member.Create(new PolygonSection(ch.ToPolygonPoints()), C245, new SteelDesignParams { LefX = 3, LefY = 1 });
        var r = WebOf(Sp16Section9Local.Check(m, new SteelForces(-100, 15, 0, 0, 0)));
        Assert.Contains(r.Notes, n => n.StartsWith("тип 3"));
        double alpha = Var(r, "α"), s1 = Var(r, "σ1");
        Assert.True(alpha >= 1);
        double ccr = Sp16Tables.Table17Ccr(alpha);
        double l2 = Math.Min(1.42 * Math.Sqrt(ccr * Ry / (2 * s1)), 0.7 + 2.4 * alpha);
        Assert.Equal(Math.Min(0.75 * l2, 0.52 + 1.8 * alpha), r.Allowable, 9);
    }

    [Fact]
    public void Web_LargeMx_AsBendingElement()
    {
        var r = WebOf(Sp16Section9Local.Check(Beam(new SteelDesignParams()), new SteelForces(-10, 60, 0, 0, 0)));
        Assert.Equal(CheckStatus.NotApplicable, r.Status);
    }

    [Fact]
    public void TensionOrNoMoment_NoChecks()
    {
        Assert.Empty(Sp16Section9Local.Check(Beam(new SteelDesignParams()), new SteelForces(100, 10, 0, 0, 0)));
        Assert.Empty(Sp16Section9Local.Check(Beam(new SteelDesignParams()), new SteelForces(-100, 0, 0, 0, 0)));
    }

    // ── Табл. 23 ──

    [Fact]
    public void Flange_Type1_Formula132()
    {
        var m = Beam(new SteelDesignParams { LefX = 6, LefY = 1.0 });
        var r = FlangesOf(Sp16Section9Local.Check(m, new SteelForces(-300, 60, 0, 0, 0))).Single();
        double mx = 60.0 / 300 * A / Wx;
        Assert.InRange(mx, 0, 5);
        double lufc = 0.36 + 0.10 * Math.Clamp(m.GoverningPhi()!.Value.LambdaBar, 0.8, 4);
        double lbx = Math.Clamp(m.LambdaBar(true), 0.8, 4);
        Assert.Equal(lufc - 0.01 * (1.5 + 0.7 * lbx) * mx, r.Allowable, 9);
        Assert.Equal((B - Tw) / 2 / Tf * Sq, r.Applied, 9);
        Assert.Equal("табл. 23 (132)", r.Formula);
    }

    [Fact]
    public void Flange_Type1_Interpolation_5To20()
    {
        var m = Beam(new SteelDesignParams { LefX = 6, LefY = 1.0 });
        var r = FlangesOf(Sp16Section9Local.Check(m, new SteelForces(-60, 60, 0, 0, 0))).Single();
        double mx = 60.0 / 60 * A / Wx;
        Assert.InRange(mx, 5, 20);
        double lufc = 0.36 + 0.10 * Math.Clamp(m.GoverningPhi()!.Value.LambdaBar, 0.8, 4);
        double l5 = lufc - 0.01 * (1.5 + 0.7 * Math.Clamp(m.LambdaBar(true), 0.8, 4)) * 5;
        double sc = Math.Min(60 / A + 60 / Wx, Ry), l20 = 0.5 * Math.Sqrt(Ry / sc);
        Assert.Equal(l5 + (l20 - l5) * (mx - 5) / 15, r.Allowable, 9);
    }

    [Fact]
    public void Flange_Type4_WeakAxis_Formula135()
    {
        var m = Beam(new SteelDesignParams { LefX = 6, LefY = 2 });
        var r = FlangesOf(Sp16Section9Local.Check(m, new SteelForces(-300, 0, 5, 0, 0))).Single();
        Assert.Equal(0.36 + 0.10 * Math.Clamp(m.LambdaBar(false), 0.8, 4), r.Allowable, 9);
        Assert.Equal("табл. 23 (135)", r.Formula);
    }

    [Fact]
    public void Box_WebType1_And_Plate133()
    {
        var m = Box(new SteelDesignParams { LefX = 8, LefY = 8 });
        var res = Sp16Section9Local.Check(m, new SteelForces(-2000, 300, 0, 0, 0));
        var web = WebOf(res);
        Assert.Contains(web.Notes, n => n.StartsWith("тип 1"));
        var plate = FlangesOf(res).Single();
        double mx = 300.0 / 2000 * m.S.A / m.S.WxMin;
        double lufc = Sp16Tables.WebLimitCentral(m.S, m.LambdaBar(false), out _)!.Value;
        double lbx = Math.Clamp(m.LambdaBar(true), 0.8, 4);
        Assert.Equal(lufc - 0.01 * (5.3 + 1.3 * lbx) * mx, plate.Allowable, 9);
        Assert.Equal((0.300 - 2 * 0.010) / 0.016 * Sq, plate.Applied, 9);
        Assert.Equal("табл. 23 (133)", plate.Formula);
    }

    [Fact]
    public void Channel_Flange134()
    {
        var ch = new ChannelProfile("20П", 0.200, 0.076, 0.0052, 0.009, 0.0095, 0.004, 23.4e-4, 0);
        var m = Sp16Member.Create(new PolygonSection(ch.ToPolygonPoints()), C245, new SteelDesignParams { LefX = 3, LefY = 1 });
        var r = FlangesOf(Sp16Section9Local.Check(m, new SteelForces(-100, 15, 0, 0, 0))).Single();
        Assert.Equal(0.36 + 0.10 * Math.Clamp(m.LambdaBar(true), 0.8, 4), r.Allowable, 9);
        Assert.Equal("табл. 23 (134)", r.Formula);
    }
}
