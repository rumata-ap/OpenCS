using CScore.Sp16;
using Xunit;

namespace CScore.Tests.Steel;

/// <summary>Раздел 9 СП 16: устойчивость из плоскости действия момента 9.2.4–9.2.6 (табл. 21, cmax), 9.2.8 — кПа, кН, м.</summary>
public class Sp16Section9OutOfPlaneTests
{
    static readonly SteelMaterialProps C245 = new(240000, 360000, 245000, 370000, 2.06e8);
    const double Ry = 240000, E = 2.06e8;

    // Сварной двутавр 300×150×8×12.
    const double H = 0.300, B = 0.150, Tw = 0.008, Tf = 0.012;
    static readonly double A = 2 * B * Tf + (H - 2 * Tf) * Tw;
    static readonly double Ix = (B * Math.Pow(H, 3) - (B - Tw) * Math.Pow(H - 2 * Tf, 3)) / 12, Wx = Ix / (H / 2);
    static readonly double Iy = 2 * Tf * Math.Pow(B, 3) / 12 + (H - 2 * Tf) * Math.Pow(Tw, 3) / 12;

    static Sp16Member Beam(SteelDesignParams p) =>
        Sp16Member.Create(new PolygonSection(TemplatePoints.IBeamPoints(H, B, Tw, Tf)), C245, p);

    static Sp16Member Tee(SteelDesignParams p) =>
        Sp16Member.Create(new PolygonSection(TemplatePoints.TeePoints(0.150, 0.200, 0.010, 0.014)), C245, p);

    static double Var(Sp16CheckResult r, string name) => r.Variables.First(v => v.Key == name).Value;
    static double LambdaBarY(double lefY) => lefY / Math.Sqrt(Iy / A) * Math.Sqrt(Ry / E);
    static Sp16CheckResult Check(Sp16Member m, SteelForces f) => Sp16Section9.OutOfPlaneStability(m, f).Single();

    // ── (111), (112): α, β по табл. 21 ──

    [Fact]
    public void Formula111_DoublySymmetric_MxBelow1_HandCalc()
    {
        var r = Check(Beam(new SteelDesignParams { LefX = 6, LefY = 2.5 }), new SteelForces(-500, 30, 0, 0, 0));
        double mx = 30.0 / 500 * A / Wx;                                     // 0,59
        double phiY = Sp16Stability.Phi(LambdaBarY(2.5), SectionCurve.c);   // сварной двутавр из плоскости стенки — тип c
        double c = 1 / (1 + 0.7 * mx);
        Assert.Equal(mx, Var(r, "mx"), 9);
        Assert.Equal(c, Var(r, "c"), 9);
        Assert.Equal(500 / (c * phiY * A * Ry), r.Utilization, 9);
        Assert.Equal("(111)", r.Formula);
        Assert.Contains(r.Notes, n => n.Contains("тип сечения 1"));
    }

    [Fact]
    public void Formula112_MxBetween1And5_AlphaDependsOnMx()
    {
        var r = Check(Beam(new SteelDesignParams { LefX = 6, LefY = 2.5 }), new SteelForces(-500, 100, 0, 0, 0));
        double mx = 100.0 / 500 * A / Wx;                                    // 1,97
        Assert.Equal(1 / (1 + (0.65 + 0.05 * mx) * mx), Var(r, "c"), 9);
    }

    [Fact]
    public void Formula113_LargeMx_UsesPhiBWithTwoRestraints()
    {
        var p = new SteelDesignParams { LefX = 6, LefY = 2.5 };
        var r = Check(Beam(p), new SteelForces(-50, 60, 0, 0, 0));
        double mx = 60.0 / 50 * A / Wx;                                      // 11,8
        double phiY = Sp16Stability.Phi(LambdaBarY(2.5), SectionCurve.c);
        double phiB = Sp16PhiB.Compute(Beam(p with { LtbRestraints = LtbRestraints.TwoOrMore }), false).PhiB;
        Assert.True(mx >= 10);
        Assert.Equal(phiB, Var(r, "φb"), 9);
        Assert.Equal(1 / (1 + mx * phiY / phiB), Var(r, "c (113)"), 9);         // до ограничения c ≥ 0,3
        Assert.Contains(r.Variables, v => v.Key == "c (113)");
    }

    [Fact]
    public void Formula114_Interpolation()
    {
        var p = new SteelDesignParams { LefX = 6, LefY = 2.5 };
        var r = Check(Beam(p), new SteelForces(-100, 60, 0, 0, 0));
        double mx = 60.0 / 100 * A / Wx;                                     // 5,9
        double phiY = Sp16Stability.Phi(LambdaBarY(2.5), SectionCurve.c);
        double phiB = Sp16PhiB.Compute(Beam(p with { LtbRestraints = LtbRestraints.TwoOrMore }), false).PhiB;
        double c5 = 1 / (1 + (0.65 + 0.05 * 5) * 5), c10 = 1 / (1 + 10 * phiY / phiB);
        Assert.InRange(mx, 5, 10);
        Assert.Equal(c5 * (2 - 0.2 * mx) + c10 * (0.2 * mx - 1), Var(r, "c (114)"), 9);
    }

    [Fact]
    public void Tee_StemCompressedIsType4_FlangeCompressedIsType2()
    {
        var p = new SteelDesignParams { LefX = 3, LefY = 2 };
        var m = Tee(p);
        // Mx > 0 растягивает полку (сверху) — сжат конец стенки: тип 4, I2 = 0 ⇒ α = 1.
        var r4 = Check(m, new SteelForces(-200, 5, 0, 0, 0));
        double mx4 = 5.0 / 200 * m.S.A / m.S.Wx(false);
        Assert.True(mx4 <= 1);
        Assert.Equal(1 / (1 + mx4), Var(r4, "c"), 9);
        Assert.Contains(r4.Notes, n => n.Contains("тип сечения 4"));
        // Mx < 0 — сжата полка: тип 2, α = 0,7.
        var r2 = Check(m, new SteelForces(-200, -5, 0, 0, 0));
        double mx2 = 5.0 / 200 * m.S.A / m.S.Wx(true);
        Assert.Equal(1 / (1 + 0.7 * mx2), Var(r2, "c"), 9);
        Assert.Contains(r2.Notes, n => n.Contains("тип сечения 2"));
    }

    [Fact]
    public void MinimumC_Is03()
    {
        // mx ≥ 10 при большом φy/φb даёт c < 0,3.
        var r = Check(Beam(new SteelDesignParams { LefX = 6, LefY = 2.5 }), new SteelForces(-10, 60, 0, 0, 0));
        Assert.True(Var(r, "c (113)") < 0.3);
        Assert.Equal(0.3, Var(r, "c"), 12);
    }

    // ── 9.2.6: расчётный момент ──

    [Theory]
    [InlineData(MomentShape.AsGiven, 0.3, 1.0, 0.5)]          // не менее половины наибольшего
    [InlineData(MomentShape.AsGiven, 0.8, 1.0, 0.8)]
    [InlineData(MomentShape.LinearEndMoments, 1.0, 0.0, 2.0 / 3)]
    [InlineData(MomentShape.LinearEndMoments, 1.0, -1.0, 0.5)]
    public void DesignMoment_MiddleThird(MomentShape shape, double middle, double delta, double expectedRatio)
    {
        var p = new SteelDesignParams { LefX = 6, LefY = 2.5, MomentShape = shape, MiddleThirdMomentRatio = middle, EndMomentRatio = delta };
        var r = Check(Beam(p), new SteelForces(-500, 30, 0, 0, 0));
        Assert.Equal(expectedRatio * 30, Var(r, "Mx (9.2.6)"), 9);
    }

    [Fact]
    public void DesignMoment_CantileverColumn_FullMoment()
    {
        var p = new SteelDesignParams { LefX = 6, LefY = 2.5, CantileverColumn = true, MiddleThirdMomentRatio = 0.3 };
        Assert.Equal(30, Var(Check(Beam(p), new SteelForces(-500, 30, 0, 0, 0)), "Mx (9.2.6)"), 9);
    }

    // ── cmax (Д.1), (Д.2) ──

    [Fact]
    public void CMax_DoublySymmetric_HandCalc_AndCap()
    {
        const double lefY = 4.0;
        var r = Check(Beam(new SteelDesignParams { LefX = 6, LefY = lefY }), new SteelForces(-300, 20, 0, 0, 0));
        double lambdaY = lefY / Math.Sqrt(Iy / A), lb = LambdaBarY(lefY);
        Assert.True(lb > 3.14);
        double h = H - Tf, it = 1.29 / 3 * (2 * B * Math.Pow(Tf, 3) + (H - 2 * Tf) * Math.Pow(Tw, 3));
        double ex = 20.0 / -300;                                             // Mx > 0 — сила смещена вниз
        double rho = (Ix + Iy) / (A * h * h), mu = 8 * 0.25 + 0.156 * it * lambdaY * lambdaY / (A * h * h), delta = 4 * rho / mu;
        double cmax = 2 / (1 + delta + Math.Sqrt(Math.Pow(1 - delta, 2) + 16 / mu * Math.Pow(ex / h, 2)));
        Assert.Equal(cmax, Var(r, "cmax"), 9);

        double phiY = Sp16Stability.Phi(lb, SectionCurve.c), phiC = Sp16Stability.Phi(3.14, SectionCurve.c);
        double mx = 20.0 / 300 * A / Wx;
        double c112 = Math.Min(1, Math.Sqrt(phiC / phiY) / (1 + 0.7 * mx));
        Assert.Equal(Math.Min(Math.Max(c112, 0.3), cmax), Var(r, "c"), 9);
    }

    [Fact]
    public void CMax_MonoIBeam_Type2_HandCalc_SignMatters()
    {
        const double hh = 0.400, b1 = 0.200, t1 = 0.012, b2 = 0.120, t2 = 0.010, tw = 0.008;
        var s = Sp16Section.FromContour(new PolygonSection(
        [
            (-b2 / 2, 0), (b2 / 2, 0), (b2 / 2, t2), (tw / 2, t2), (tw / 2, hh - t1), (b1 / 2, hh - t1), (b1 / 2, hh),
            (-b1 / 2, hh), (-b1 / 2, hh - t1), (-tw / 2, hh - t1), (-tw / 2, t2), (-b2 / 2, t2),
        ]), new SteelProfile { Kind = SteelProfileKind.IBeam, H = hh, Bf1 = b1, Tf1 = t1, Bf2 = b2, Tf2 = t2, Tw = tw }, C245);
        double i1 = t1 * Math.Pow(b1, 3) / 12, i2 = t2 * Math.Pow(b2, 3) / 12;
        double h1 = s.YTop - t1 / 2, h2 = s.YBottom - t2 / 2, h = h1 + h2;
        double omega = i1 * i2 / (s.Iy * s.Iy), alpha = (i1 * h1 - i2 * h2) / (s.Iy * h);
        double n = i1 / (i1 + i2), r = b1 / h;
        double beta = (2 * n - 1) * (0.47 - 0.035 * r * (1 + r - 0.072 * r * r));
        double it = 1.25 / 3 * (b1 * Math.Pow(t1, 3) + b2 * Math.Pow(t2, 3) + (hh - t1 - t2) * Math.Pow(tw, 3));
        const double lambdaY = 150, ex = 0.05;
        double rho = (s.Ix + s.Iy) / (s.A * h * h) + alpha * alpha;
        double mu = 8 * omega + 0.156 * it * lambdaY * lambdaY / (s.A * h * h), delta = 4 * rho / mu;
        double bb = 1 + 2 * beta / rho * ex / h;
        double cmax = 2 / (1 + delta * bb + Math.Sqrt(Math.Pow(1 - delta * bb, 2) + 16 / mu * Math.Pow(alpha - ex / h, 2)));

        var up = Sp16Section9.CMax(s, lambdaY, ex);
        Assert.Equal(2, up.Type);
        Assert.Equal(cmax, up.CMax!.Value, 9);
        Assert.NotEqual(cmax, Sp16Section9.CMax(s, lambdaY, -ex).CMax!.Value, 6);
    }

    [Fact]
    public void CMax_Tee_Type3()
    {
        var s = Sp16Section.FromContour(new PolygonSection(TemplatePoints.TeePoints(0.150, 0.200, 0.010, 0.014)), null, C245);
        var res = Sp16Section9.CMax(s, 150, 0.03);
        Assert.Equal(3, res.Type);
        double h = s.YTop - 0.007 + s.YBottom;
        Assert.Equal((s.YTop - 0.007) / h, res.Vars.First(v => v.Name == "α (Д.6)").Value, 9);
        Assert.Equal(0, res.Vars.First(v => v.Name == "ω").Value, 12);
        Assert.InRange(res.CMax!.Value, 0, 1);
    }

    [Fact]
    public void Channel_Type3_NoCMaxForEccentricCompression()
    {
        var ch = new ChannelProfile("20П", 0.200, 0.076, 0.0052, 0.009, 0.0095, 0.004, 23.4e-4, 0);
        var m = Sp16Member.Create(new PolygonSection(ch.ToPolygonPoints()), C245, new SteelDesignParams { LefX = 3, LefY = 3 });
        var r = Check(m, new SteelForces(-100, 5, 0, 0, 0));
        Assert.Equal("(111)", r.Formula);
        Assert.True(Var(r, "λ̄y") > 3.14);
        Assert.Contains(r.Notes, x => x.Contains("тип сечения 3"));
        Assert.Contains(r.Notes, x => x.Contains("cmax не определён"));
    }

    // ── 9.2.8, формула (115) ──

    [Fact]
    public void Formula115_WeakAxisBending_HandCalc()
    {
        var r = Check(Beam(new SteelDesignParams { LefX = 12, LefY = 1 }), new SteelForces(-300, 0, 5, 0, 0));
        double lbx = 12 / Math.Sqrt(Ix / A) * Math.Sqrt(Ry / E);
        double phiX = Sp16Stability.Phi(lbx, SectionCurve.b);
        Assert.Equal("(115)", r.Formula);
        Assert.Equal(300 / (phiX * A * Ry), r.Utilization, 9);
    }

    [Fact]
    public void Formula115_NotRequired_WhenLambdaOutNotGreater()
    {
        var r = Check(Beam(new SteelDesignParams { LefX = 6, LefY = 3 }), new SteelForces(-300, 0, 5, 0, 0));
        Assert.Equal(CheckStatus.NotApplicable, r.Status);
        Assert.Equal("(115)", r.Formula);
    }

    // ── Неприменимость ──

    [Fact]
    public void TwoMoments_DeferredTo929() =>
        Assert.Equal(CheckStatus.NotApplicable, Check(Beam(new SteelDesignParams()), new SteelForces(-100, 10, 2, 0, 0)).Status);

    [Fact]
    public void TensionOrNoMoment_NoChecks()
    {
        Assert.Empty(Sp16Section9.OutOfPlaneStability(Beam(new SteelDesignParams()), new SteelForces(100, 10, 0, 0, 0)));
        Assert.Empty(Sp16Section9.OutOfPlaneStability(Beam(new SteelDesignParams()), new SteelForces(-100, 0, 0, 0, 0)));
    }

    [Fact]
    public void RectStrongAxis_NotInTable21()
    {
        var m = Sp16Member.Create(new PolygonSection(TemplatePoints.RectPoints(0.02, 0.2)), C245, new SteelDesignParams());
        var r = Check(m, new SteelForces(-100, 2, 0, 0, 0));
        Assert.Equal(CheckStatus.NotApplicable, r.Status);
        Assert.Equal("(111)", r.Formula);
    }
}
