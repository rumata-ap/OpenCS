using CScore.Sp16;
using Xunit;

namespace CScore.Tests.Steel;

/// <summary>Раздел 9 СП 16: прочность 9.1 и устойчивость 9.2.2 (табл. Д.2, Д.3, Д.5, 20) — кПа, кН, м.</summary>
public class Sp16Section9Tests
{
    static readonly SteelMaterialProps C245 = new(240000, 360000, 245000, 370000, 2.06e8);
    static readonly SteelMaterialProps C440 = new(440000, 540000, 450000, 590000, 2.06e8);

    // Сварной двутавр 300×150×8×12.
    const double H = 0.300, B = 0.150, Tw = 0.008, Tf = 0.012;
    static readonly double A = 2 * B * Tf + (H - 2 * Tf) * Tw;
    static readonly double Ix = (B * Math.Pow(H, 3) - (B - Tw) * Math.Pow(H - 2 * Tf, 3)) / 12, Wx = Ix / (H / 2);
    static readonly double Iy = 2 * Tf * Math.Pow(B, 3) / 12 + (H - 2 * Tf) * Math.Pow(Tw, 3) / 12, Wy = Iy / (B / 2);

    static Sp16Member Beam(SteelDesignParams? p = null) =>
        Sp16Member.Create(new PolygonSection(TemplatePoints.IBeamPoints(H, B, Tw, Tf)), C245, p ?? new SteelDesignParams());

    static Sp16Member Tee(SteelMaterialProps mat, SteelDesignParams? p = null) =>
        Sp16Member.Create(new PolygonSection(TemplatePoints.TeePoints(0.150, 0.200, 0.010, 0.014)), mat, p ?? new SteelDesignParams());

    static double Var(Sp16CheckResult r, string name) => r.Variables.First(v => v.Key == name).Value;

    // ── Табл. Д.3 ──

    [Theory]
    [InlineData(0.5, 0.1, 0.967)]
    [InlineData(1.5, 1.0, 0.593)]
    [InlineData(9.0, 2.0, 0.093)]
    [InlineData(3.0, 4.0, 0.217)]
    [InlineData(8.0, 6.5, 0.076)]
    [InlineData(3.0, 10, 0.112)]
    [InlineData(5.5, 20, 0.051)]
    [InlineData(7.0, 5.0, 0.098)]
    public void TableD3_GridValues(double lambdaBar, double mef, double expected) =>
        Assert.Equal(expected, Sp16Tables.PhiE(lambdaBar, mef)!.Value, 9);

    [Fact]
    public void TableD3_BilinearInterpolation()
    {
        double expected = (0.967 + 0.922 + 0.925 + 0.854) / 4;
        Assert.Equal(expected, Sp16Tables.PhiE(0.75, 0.175)!.Value, 9);
    }

    [Theory]
    [InlineData(6.0, 7.0)]      // окончание таблицы — строки до λ̄ = 5,5
    [InlineData(8.5, 2.5)]      // продолжение — до λ̄ = 8
    [InlineData(9.5, 1.0)]
    [InlineData(2.0, 21)]
    public void TableD3_OutsideTable_IsNull(double lambdaBar, double mef) =>
        Assert.Null(Sp16Tables.PhiE(lambdaBar, mef));

    [Fact]
    public void TableD3_BelowFirstRowAndColumn_UsesFirst() =>
        Assert.Equal(0.967, Sp16Tables.PhiE(0.2, 0.05)!.Value, 9);

    // ── Табл. Д.2 ──

    static Sp16Section Section(IEnumerable<(double, double)> pts) => Sp16Section.FromContour(new PolygonSection(pts), null, C245);

    [Fact]
    public void EtaType5_CorrectedCoefficient_IsContinuous()
    {
        var s = Section(TemplatePoints.IBeamPoints(0.300, 0.300, 0.008, 0.012));      // Af/Aw = 1,63 ≥ 1
        var below = Sp16Tables.Eta(s, true, false, 5 - 1e-9, 3.0);
        var above = Sp16Tables.Eta(s, true, false, 5 + 1e-9, 3.0);
        Assert.Equal(5, below.Type);
        Assert.Equal(1.4 - 0.02 * 3.0, below.Eta!.Value, 6);
        Assert.Equal(above.Eta!.Value, below.Eta!.Value, 6);
        // λ̄ = 5, m = 0,1 — совпадает со столбцом λ̄ > 5 (1,3); при «0,012» из docx было бы 1,536.
        Assert.Equal(1.3, Sp16Tables.Eta(s, true, false, 0.1, 5.0).Eta!.Value, 9);
    }

    [Fact]
    public void EtaType5_InterpolatesAfAw()
    {
        var s = Section(TemplatePoints.IBeamPoints(H, B, Tw, Tf));
        double r = B * Tf / ((H - 2 * Tf) * Tw), m = 1.0, lb = 2.0;
        double e05 = 1.75 - 0.1 * m - 0.02 * (5 - m) * lb, e1 = 1.90 - 0.1 * m - 0.02 * (6 - m) * lb;
        var eta = Sp16Tables.Eta(s, true, false, m, lb);
        Assert.Equal(e05 + (e1 - e05) * (r - 0.5) / 0.5, eta.Eta!.Value, 9);
    }

    [Fact]
    public void EtaIBeamWeakAxis_IsType8()
    {
        var s = Section(TemplatePoints.IBeamPoints(H, B, Tw, Tf));
        double r = (H - 2 * Tf) * Tw / (2 * B * Tf), m = 2.0, lb = 1.0;           // 0,613: Af — стенка, Aw — пояса
        double e05 = 0.5 + 0.1 * m + 0.02 * (5 - m) * lb, e1 = 0.25 + 0.15 * m + 0.03 * (5 - m) * lb;
        var eta = Sp16Tables.Eta(s, false, false, m, lb);
        Assert.Equal(8, eta.Type);
        Assert.Equal(e05 + (e1 - e05) * (r - 0.5) / 0.5, eta.Eta!.Value, 9);
    }

    [Fact]
    public void EtaTee_FlangeCompressedIsType11_WebTipCompressedIsType9()
    {
        var s = Section(TemplatePoints.TeePoints(0.150, 0.200, 0.010, 0.014));      // полка сверху
        Assert.Equal(11, Sp16Tables.Eta(s, true, true, 1.0, 2.0).Type);
        Assert.Equal(9, Sp16Tables.Eta(s, true, false, 1.0, 2.0).Type);
        // Тип 11 при Af/Aw > 1 и m > 5 — в таблице «–».
        var undefined = Sp16Tables.Eta(s, true, true, 6.0, 2.0);
        Assert.Null(undefined.Eta);
        Assert.NotNull(undefined.Reason);
    }

    // ── Табл. Д.5 и табл. 20 ──

    [Fact]
    public void TableD5_GridAndInterpolation()
    {
        Assert.Equal(2.77, Sp16Tables.MefD5(-1, 3, 5.0, out _)!.Value, 9);
        Assert.Equal(3.55, Sp16Tables.MefD5(0, 1, 4.0, out _)!.Value, 9);          // опечатка «2,55» исправлена
        Assert.Equal((0.39 + 0.46) / 2, Sp16Tables.MefD5(-0.75, 2, 1.0, out _)!.Value, 9);
        Assert.Equal(3.3, Sp16Tables.MefD5(0.8, 3, 3.3, out var note)!.Value, 9);  // δ > 0,5 — mef = mef,1
        Assert.NotNull(note);
        Assert.Null(Sp16Tables.MefD5(0, 3, 25, out _));
    }

    [Fact]
    public void Table20_Moments()
    {
        Assert.Equal(80, Sp16Tables.MomentTable20(100, 60, 2, 2), 9);               // M2 = Mmax − 0,25λ̄(Mmax − M1)
        Assert.Equal(60 + 7 * 40 / 17.0, Sp16Tables.MomentTable20(100, 60, 10, 5), 9);
        double m2 = 100 - 0.25 * 2 * 40;
        Assert.Equal(m2 + 7 * (100 - m2) / 17, Sp16Tables.MomentTable20(100, 60, 10, 2), 9);
        Assert.Equal(50, Sp16Tables.MomentTable20(100, 20, 2, 5), 9);               // M1 ≥ 0,5Mmax
    }

    // ── 9.2.2, формула (109) ──

    [Fact]
    public void Formula109_WeldedIBeam_HandCalc()
    {
        var m = Beam(new SteelDesignParams { LefX = 6, LefY = 3 });
        var r = Sp16Section9.InPlaneStability(m, new SteelForces(-500, 30, 0, 0, 0)).Single();
        double lb = 6 / Math.Sqrt(Ix / A) * Math.Sqrt(240000 / 2.06e8);
        double mr = 30.0 / 500 * A / Wx, ratio = B * Tf / ((H - 2 * Tf) * Tw);
        double e05 = 1.75 - 0.1 * mr - 0.02 * (5 - mr) * lb, e1 = 1.90 - 0.1 * mr - 0.02 * (6 - mr) * lb;
        double eta = e05 + (e1 - e05) * (ratio - 0.5) / 0.5, mef = eta * mr;
        // λ̄ = 1,657 (между 1,5 и 2,0), mef = 0,954 (между 0,75 и 1,0).
        double tl = (lb - 1.5) / 0.5, tm = (mef - 0.75) / 0.25;
        double phiE = (1 - tl) * ((1 - tm) * 0.647 + tm * 0.593) + tl * ((1 - tm) * 0.587 + tm * 0.536);
        Assert.Equal(eta, Var(r, "η"), 9);
        Assert.Equal(phiE, Var(r, "φe"), 9);
        Assert.Equal(500 / (phiE * A * 240000), r.Utilization, 9);
        Assert.Equal(CheckStatus.Ok, r.Status);
    }

    [Fact]
    public void Formula109_LinearEndMoments_UsesTableD5()
    {
        var m = Beam(new SteelDesignParams { LefX = 6, LefY = 3, MomentShape = MomentShape.LinearEndMoments, EndMomentRatio = -1 });
        var r = Sp16Section9.InPlaneStability(m, new SteelForces(-500, 30, 0, 0, 0)).Single();
        double mef1 = Var(r, "mef,1"), lb = Var(r, "λ̄");
        double mef = Sp16Tables.MefD5(-1, lb, mef1, out _)!.Value;
        Assert.True(mef < mef1);
        Assert.Equal(Sp16Tables.PhiE(lb, mef)!.Value, Var(r, "φe"), 9);
    }

    [Fact]
    public void Formula109_TeePinned_UsesTable20()
    {
        var m = Tee(C245, new SteelDesignParams { LefX = 3, LefY = 3, MomentShape = MomentShape.PinnedTransverse, MiddleThirdMomentRatio = 0.8 });
        var r = Sp16Section9.InPlaneStability(m, new SteelForces(-200, -10, 0, 0, 0)).Single();
        double lb = Var(r, "λ̄"), mmax = Var(r, "mmax");
        Assert.Equal(Sp16Tables.MomentTable20(10, 8, mmax, lb), Var(r, "M (табл. 20)"), 9);
        Assert.Contains(r.Notes, n => n.Contains("тип сечения 11"));                 // Mx < 0 — сжата полка (сверху)
    }

    [Fact]
    public void Formula109_ChannelInWebPlane_NotApplicable()
    {
        var ch = new ChannelProfile("20П", 0.200, 0.076, 0.0052, 0.009, 0.0095, 0.004, 23.4e-4, 0);
        var m = Sp16Member.Create(new PolygonSection(ch.ToPolygonPoints()), C245, new SteelDesignParams());
        var r = Sp16Section9.InPlaneStability(m, new SteelForces(-100, 10, 0, 0, 0)).Single();
        Assert.Equal(CheckStatus.NotApplicable, r.Status);
    }

    [Fact]
    public void Formula109_TwoMoments_DelegatesTo929()
    {
        var res = Sp16Section9.InPlaneStability(Beam(), new SteelForces(-100, 10, 2, 0, 0));
        Assert.Equal("(116)", res[0].Formula);
    }

    // ── 9.1 прочность ──

    [Fact]
    public void Formula106_BiaxialHandCalc()
    {
        var res = Sp16Section9.Strength(Beam(), new SteelForces(-300, 40, 5, 0, 0));
        var r = res.Single(x => x.Formula == "(106)");
        double sigma = -300 / A - 40 * (H / 2) / Ix - 5 * (B / 2) / Iy;
        Assert.Equal(Math.Abs(sigma) / 240000, r.Utilization, 6);
    }

    [Fact]
    public void Formula105_Plastic_HandCalc()
    {
        var res = Sp16Section9.Strength(Beam(new SteelDesignParams { AllowPlastic = true }), new SteelForces(-300, 40, 0, 0, 0));
        var r = res.Single(x => x.Formula == "(105)");
        double ratio = B * Tf / ((H - 2 * Tf) * Tw);
        double cx = 1.12 + (1.07 - 1.12) * (ratio - 0.5) / 0.5;
        double expected = Math.Pow(300 / (A * 240000), 1.5) + 40 / (cx * Wx * 240000);
        Assert.Equal(expected, r.Utilization, 6);
    }

    [Fact]
    public void Formula105_HighShear_FallsBackTo106()
    {
        var res = Sp16Section9.Strength(Beam(new SteelDesignParams { AllowPlastic = true }), new SteelForces(-300, 40, 0, 0, 400));
        Assert.Contains(res, x => x.Formula == "(105)" && x.Status == CheckStatus.NotApplicable);
        Assert.Contains(res, x => x.Formula == "(106)");
    }

    [Fact]
    public void Formula107_HighStrengthTee()
    {
        var m = Tee(C440, new SteelDesignParams { LefX = 3, LefY = 3 });
        var res = Sp16Section9.Strength(m, new SteelForces(-200, 20, 0, 0, 0));
        var r = res.Single(x => x.Formula == "(107)");
        var s = m.S;
        double lb = m.LambdaBar(true), delta = 1 - 0.1 * 200 * lb * lb / (s.A * 440000);
        double expected = 1.3 / 540000 * Math.Abs(200 / s.A - 20 / (delta * s.Wx(true)));   // Mx > 0 — растянута полка (сверху)
        Assert.Equal(expected, r.Utilization, 9);
        Assert.DoesNotContain(Sp16Section9.Strength(Beam(), new SteelForces(-200, 20, 0, 0, 0)), x => x.Formula == "(107)");
    }
}
