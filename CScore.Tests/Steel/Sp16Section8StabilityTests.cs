using CScore.Sp16;
using Xunit;

namespace CScore.Tests.Steel;

/// <summary>8.4 и приложение Ж СП 16: φb и общая устойчивость балок (кПа, кН, м).</summary>
public class Sp16Section8StabilityTests
{
    static readonly SteelMaterialProps C245 = new(240000, 360000, 245000, 370000, 2.06e8);
    const double E = 2.06e8, Ry = 240000;

    static readonly SteelProfile Rolled30B1 = new()
    {
        Kind = SteelProfileKind.IBeam, Fabrication = SteelFabrication.Rolled,
        H = 0.296, Bf1 = 0.140, Tf1 = 0.0085, Tw = 0.0058, R = 0.015,
    };

    static Sp16Member Rolled(SteelDesignParams p) =>
        Sp16Member.Create(new PolygonSection(new IBeamProfile("30Б1", 0.296, 0.140, 0.0058, 0.0085, 0.015, 0, 41.92e-4).ToPolygonPoints()),
            C245, p with { Profile = Rolled30B1 });

    static Sp16Member Welded(SteelDesignParams p) =>
        Sp16Member.Create(new PolygonSection(TemplatePoints.IBeamPoints(0.300, 0.150, 0.008, 0.012)), C245, p);

    static double PhiBFromPhi1(double phi1) => phi1 <= 0.85 ? phi1 : Math.Min(1, 0.68 + 0.21 * phi1);

    [Fact]
    public void RolledIBeam_Uniform_NoRestraints_CompressedFlange()
    {
        var m = Rolled(new SteelDesignParams { LefB = 6, LtbLoad = LtbLoadKind.Uniform });
        var s = m.S;
        double alpha = s.It / s.Iy * Math.Pow(6 / 0.296, 2);                                     // (Ж.4), k = 1
        double psi = 1.13 * (Math.Sqrt(0.95 * alpha + 6.09 * 0.46 * 0.46 + 5.78) - 2.47 * 0.46);
        double phi1 = psi * s.Iy / s.Ix * Math.Pow(0.296 / 6, 2) * E / Ry;
        var r = Sp16PhiB.Compute(m, topCompressed: false);
        Assert.Null(r.NotApplicable);
        Assert.Equal(PhiBFromPhi1(phi1), r.PhiB, 9);
        Assert.InRange(r.PhiB, 0.38, 0.48);                                                     // порядок величины для 30Б1, l = 6 м
    }

    [Fact]
    public void RolledIBeam_TensionFlangeLoad_GivesHigherPhiB()
    {
        var comp = Sp16PhiB.Compute(Rolled(new SteelDesignParams { LefB = 6 }), false).PhiB;
        var tens = Sp16PhiB.Compute(Rolled(new SteelDesignParams { LefB = 6, LtbLoadOnTensionFlange = true }), false).PhiB;
        Assert.True(tens > comp);
    }

    [Fact]
    public void RolledIBeam_TwoOrMoreRestraints_K154()
    {
        var m = Rolled(new SteelDesignParams { LefB = 2, LtbRestraints = LtbRestraints.TwoOrMore });
        var s = m.S;
        double alpha = 1.54 * s.It / s.Iy * Math.Pow(2 / 0.296, 2);
        double psi = 2.25 + 0.07 * alpha;
        double phi1 = psi * s.Iy / s.Ix * Math.Pow(0.296 / 2, 2) * E / Ry;
        Assert.Equal(PhiBFromPhi1(phi1), Sp16PhiB.Compute(m, false).PhiB, 9);
    }

    [Fact]
    public void WeldedIBeam_AlphaByZh5_PureBending()
    {
        var m = Welded(new SteelDesignParams { LefB = 4, LtbLoad = LtbLoadKind.PureBending });
        var s = m.S;
        double hm = 0.300, bf = 0.150, tf = 0.012, tw = 0.008;
        double alpha = 4 * Math.Pow(4 * tf / (hm * bf), 2) * (1 + 0.5 * hm * Math.Pow(tw, 3) / (bf * Math.Pow(tf, 3)));
        double psi = Math.Sqrt(0.95 * alpha + 5.78);
        double h = 0.300 - 0.012;
        double phi1 = psi * s.Iy / s.Ix * Math.Pow(h / 4, 2) * E / Ry;
        Assert.Equal(PhiBFromPhi1(phi1), Sp16PhiB.Compute(m, false).PhiB, 9);
    }

    [Fact]
    public void Cantilever_ConcentratedEnd_TensionFlange_TableZh2()
    {
        var m = Rolled(new SteelDesignParams { LefB = 2, Cantilever = true, LtbLoad = LtbLoadKind.ConcentratedEnd, LtbLoadOnTensionFlange = true });
        var s = m.S;
        double alpha = 1.54 * s.It / s.Iy * Math.Pow(2 / 0.296, 2);
        double psi = alpha <= 28 ? 1.0 + 0.16 * alpha : 4.0 + 0.05 * alpha;
        double phi1 = psi * s.Iy / s.Ix * Math.Pow(0.296 / 2, 2) * E / Ry;
        Assert.Equal(PhiBFromPhi1(phi1), Sp16PhiB.Compute(m, true).PhiB, 9);
    }

    [Fact]
    public void Cantilever_UniformOnCompressedFlange_NotInTable()
    {
        var m = Rolled(new SteelDesignParams { LefB = 2, Cantilever = true, LtbLoad = LtbLoadKind.Uniform });
        Assert.NotNull(Sp16PhiB.Compute(m, true).NotApplicable);
    }

    // ── Двутавр с одной осью симметрии: пояса 200×12 (верх) и 120×10 (низ), стенка 378×8 ──

    static List<(double, double)> MonoIBeamPoints()
    {
        const double h = 0.400, b1 = 0.200, t1 = 0.012, b2 = 0.120, t2 = 0.010, tw = 0.008;
        return
        [
            (-b2 / 2, 0), (b2 / 2, 0), (b2 / 2, t2), (tw / 2, t2), (tw / 2, h - t1), (b1 / 2, h - t1), (b1 / 2, h),
            (-b1 / 2, h), (-b1 / 2, h - t1), (-tw / 2, h - t1), (-tw / 2, t2), (-b2 / 2, t2),
        ];
    }

    static Sp16Member Mono(SteelDesignParams p) =>
        Sp16Member.Create(new PolygonSection(MonoIBeamPoints()), C245, p with
        {
            Profile = new SteelProfile { Kind = SteelProfileKind.IBeam, Fabrication = SteelFabrication.Welded, H = 0.400, Bf1 = 0.200, Tf1 = 0.012, Bf2 = 0.120, Tf2 = 0.010, Tw = 0.008 },
        });

    [Fact]
    public void SinglySymmetric_MoreDevelopedCompressed_ConcentratedOnCompressedFlange()
    {
        var m = Mono(new SteelDesignParams { LefB = 5, LtbLoad = LtbLoadKind.ConcentratedMid });
        var s = m.S;
        // Центр тяжести от низа.
        double a1 = 0.200 * 0.012, a2 = 0.120 * 0.010, aw = 0.378 * 0.008;
        double yc = (a1 * 0.394 + a2 * 0.005 + aw * 0.199) / (a1 + a2 + aw);
        double h1 = 0.394 - yc, h2 = yc - 0.005, h = h1 + h2;
        double i1 = 0.012 * Math.Pow(0.200, 3) / 12, i2 = 0.010 * Math.Pow(0.120, 3) / 12, n = i1 / (i1 + i2);
        double alpha = 1.54 * s.It / s.Iy * Math.Pow(5 / 0.400, 2);
        double r = 0.200 / h;
        double beta = (2 * n - 1) * (0.47 - 0.035 * r * (1 + r - 0.072 * r * r));
        double delta = n + 0.734 * beta;
        double eta = (1 - n) * (9.87 * n + 0.385 * s.It / i2 * Math.Pow(5 / h, 2));
        double B = delta - 1, C = 0.330 * eta, D = 3.265;                                        // табл. Ж.4, строка 2
        double psiA = (B + Math.Sqrt(B * B + C)) * D;
        double k0 = s.Iy / s.Ix * 2 * h / 25 * E / Ry;
        double phi1 = psiA * k0 * h1, phi2 = psiA * k0 * h2;
        double expected = phi2 <= 0.85 ? Math.Min(1, phi1) : Math.Min(1, phi1 * (0.21 + 0.68 * (n / phi1 + (1 - n) / phi2)));

        var res = Sp16PhiB.Compute(m, topCompressed: true);
        Assert.Null(res.NotApplicable);
        Assert.Equal(n, res.Vars.Single(v => v.Name == "n").Value, 9);
        Assert.Equal(h1, res.Vars.Single(v => v.Name == "h1").Value, 9);
        Assert.Equal(expected, res.PhiB, 9);
    }

    [Fact]
    public void SinglySymmetric_LessDevelopedCompressed_UsesPhi2()
    {
        var up = Sp16PhiB.Compute(Mono(new SteelDesignParams { LefB = 2.5 }), topCompressed: true);
        var down = Sp16PhiB.Compute(Mono(new SteelDesignParams { LefB = 2.5 }), topCompressed: false);
        Assert.True(down.PhiB < up.PhiB);
        Assert.Contains(down.Notes, x => x.Contains("менее развитый"));
        // Ж.6: n > 0,7 и 5 ≤ lef/b2 = 20,8 ≤ 25 — φ2 × (1,025 − 0,015·lef/b2), не более 0,95.
        double phi2 = down.Vars.Single(v => v.Name == "φ2").Value;
        double red = Math.Min(0.95, phi2 * (1.025 - 0.015 * 2.5 / 0.120));
        Assert.Equal(red, down.Vars.Single(v => v.Name == "φ2,red").Value, 12);
        Assert.Equal(red <= 0.85 ? red : Math.Min(1, 0.68 + 0.21 * red), down.PhiB, 12);
    }

    [Fact]
    public void SinglySymmetric_LessDevelopedCompressed_LefOverB2Above25_NotAllowed()
    {
        var down = Sp16PhiB.Compute(Mono(new SteelDesignParams { LefB = 5 }), topCompressed: false);
        Assert.Contains("> 25", down.NotApplicable);
    }

    [Fact]
    public void Tee_FlangeInTension_NotAllowed_ByZh6()
    {
        var p = new SteelDesignParams { LefB = 3, Profile = new SteelProfile { Kind = SteelProfileKind.Tee, Fabrication = SteelFabrication.Welded, H = 0.2, Bf1 = 0.2, Tf1 = 0.012, Tw = 0.010 } };
        var m = Sp16Member.Create(new PolygonSection(TemplatePoints.TeePoints(0.2, 0.2, 0.010, 0.012)), C245, p);
        Assert.Null(Sp16PhiB.Compute(m, topCompressed: true).NotApplicable);
        Assert.NotNull(Sp16PhiB.Compute(m, topCompressed: false).NotApplicable);
    }

    [Fact]
    public void Channel_PhiB_Is07Phi1()
    {
        var ch = new ChannelProfile("20П", 0.200, 0.076, 0.0052, 0.009, 0.0095, 0.004, 23.4e-4, 0);
        var m = Sp16Member.Create(new PolygonSection(ch.ToPolygonPoints(12)), C245, new SteelDesignParams { LefB = 4 });
        Assert.Equal(SteelProfileKind.Channel, m.S.Kind);
        var r = Sp16PhiB.Compute(m, false);
        double phi1 = r.Vars.Single(v => v.Name == "φ1").Value;
        Assert.Equal(Math.Min(1, 0.7 * phi1), r.PhiB, 12);
    }

    // ── 8.4 ──

    [Fact]
    public void Formula69_LongBeam()
    {
        var m = Rolled(new SteelDesignParams { LefB = 6 });
        var res = Sp16Section8Stability.Check(m, new SteelForces(0, -60, 0, 0, 0));
        var r = res.Single(x => x.Formula == "(69)");
        double phiB = Sp16PhiB.Compute(m, true).PhiB;
        Assert.Equal(60 / (phiB * m.S.Wx(true) * Ry), r.Utilization, 9);
        Assert.Contains(r.Notes, x => x.StartsWith("8.4.4 б) не выполнено"));
    }

    [Fact]
    public void Table11_ShortBeam_Exempt()
    {
        var m = Rolled(new SteelDesignParams { LefB = 1.0 });
        var res = Sp16Section8Stability.Check(m, new SteelForces(0, -60, 0, 0, 0));
        var r = Assert.Single(res);
        Assert.Equal("8.4.4", r.Clause);
        Assert.Equal(CheckStatus.Ok, r.Status);
        double b = 0.140, t = 0.0085, h = 0.296 - 0.0085, bt = b / t;
        double lub = (0.35 + 0.0032 * bt + (0.76 - 0.02 * bt) * b / h) * Math.Sqrt(Ry / (60 / m.S.Wx(true)));   // (71) × √(Ry/σ)
        Assert.Equal(1.0 / b * Math.Sqrt(Ry / E) / lub, r.Utilization, 9);
    }

    [Fact]
    public void RigidDeck_NotRequired()
    {
        var res = Sp16Section8Stability.Check(Rolled(new SteelDesignParams { LefB = 6, ContinuousRigidDeck = true }), new SteelForces(0, -60, 0, 0, 0));
        Assert.Equal(CheckStatus.NotApplicable, Assert.Single(res).Status);
    }

    [Fact]
    public void Formula70_AddsWeakAxisTermAtCompressedTip()
    {
        var m = Rolled(new SteelDesignParams { LefB = 6 });
        var r = Sp16Section8Stability.Check(m, new SteelForces(0, -60, 5, 0, 0)).Single(x => x.Formula == "(70)");
        double phiB = Sp16PhiB.Compute(m, true).PhiB;
        double expected = 60 / (phiB * m.S.Wx(true) * Ry) + 5 * 0.070 / m.S.Iy / Ry;
        Assert.Equal(expected, r.Utilization, 6);
    }

    [Fact]
    public void Plastic_846_UsesDeltaTimesLambdaUb()
    {
        var m = Welded(new SteelDesignParams { LefB = 1.5, AllowPlastic = true });
        double mx = 1.1 * m.S.WxMin * Ry;                                                         // пластическая стадия
        var r = Assert.Single(Sp16Section8Stability.Check(m, new SteelForces(0, -mx, 0, 0, 0)));
        Assert.Equal("8.4.6", r.Clause);
        double cx = r.Variables.Single(v => v.Key == "cx").Value;
        double c1x = Math.Min(cx, Math.Max(1.1, 1.0 * cx));                                       // (77): β = 1 без поперечной силы
        double delta = 1 - 0.6 * (c1x - 1) / (cx - 1);
        Assert.Equal(delta, r.Variables.Single(v => v.Key == "δ").Value, 9);
    }

    [Fact]
    public void Box_NotApplicable()
    {
        var pts = TemplatePoints.RectPoints(0.2, 0.3);
        var m = Sp16Member.Create(new PolygonSection(pts), C245, new SteelDesignParams());
        var r = Assert.Single(Sp16Section8Stability.Check(m, new SteelForces(0, 10, 0, 0, 0)));
        Assert.Equal(CheckStatus.NotApplicable, r.Status);
    }
}
