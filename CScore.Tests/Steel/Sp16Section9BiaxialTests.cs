using CScore.Sp16;
using Xunit;

namespace CScore.Tests.Steel;

/// <summary>Раздел 9 СП 16: сжатие с изгибом в двух главных плоскостях 9.2.9 (116)–(119), 9.2.10 (120)–(122) — кПа, кН, м.</summary>
public class Sp16Section9BiaxialTests
{
    static readonly SteelMaterialProps C245 = new(240000, 360000, 245000, 370000, 2.06e8);
    const double Ry = 240000;

    static Sp16Member Beam(SteelDesignParams p) =>
        Sp16Member.Create(new PolygonSection(TemplatePoints.IBeamPoints(0.300, 0.150, 0.008, 0.012)), C245, p);

    // Сварной короб 300×400 (ширина × высота), стенки 10, поясные листы 16.
    static Sp16Member Box(SteelDesignParams p) =>
        Sp16Member.Create(new PolygonSection(TemplatePoints.RectPoints(0.300, 0.400),
            [TemplatePoints.RectPoints(0.300 - 2 * 0.010, 0.400 - 2 * 0.016)]), C245, p);

    static double Var(Sp16CheckResult r, string name) => r.Variables.First(v => v.Key == name).Value;

    // ── 9.2.9 ──

    [Fact]
    public void Formula116_HandCalc()
    {
        var p = new SteelDesignParams { LefX = 6, LefY = 2.5 };
        var m = Beam(p);
        var res = Sp16Section9.BiaxialStability(m, new SteelForces(-500, 30, 8, 0, 0));
        var r = res[0];
        Assert.Equal("(116)", r.Formula);
        double c = Sp16Section9.CoefficientC(m, 500, 30).C;
        var pe = Sp16Section9.PhiE(m, 500, 8, false);
        double my = 8.0 / 500 * m.S.A / m.S.WyMin;
        Assert.Equal(my, pe.M, 9);
        double phiExy = pe.PhiE * (0.6 * Math.Cbrt(c) + 0.4 * Math.Pow(c, 0.25));   // (117)
        Assert.Equal(c, Var(r, "c"), 12);
        Assert.Equal(phiExy, Var(r, "φexy"), 12);
        Assert.Equal(500 / (phiExy * m.S.A * Ry), r.Utilization, 9);
        // mef,y ≥ mx и λx < λy — дополнительных проверок нет.
        Assert.True(Var(r, "mef,y") >= Var(r, "mx"));
        Assert.Single(res);
    }

    [Fact]
    public void Formula116_SmallMy_AddsChecks109And111()
    {
        var res = Sp16Section9.BiaxialStability(Beam(new SteelDesignParams { LefX = 6, LefY = 2.5 }), new SteelForces(-500, 30, 0.1, 0, 0));
        Assert.Equal(["(116)", "(109)", "(111)"], res.Select(x => x.Formula));
        Assert.Contains(res[1].Notes, n => n.Contains("ey = 0") && n.Contains("mef,y"));
        Assert.Equal(0, Var(res[1], "M") - 30, 9);                          // (109) — только Mx
    }

    [Fact]
    public void Formula116_LambdaXGreater_Adds109Only()
    {
        var res = Sp16Section9.BiaxialStability(Beam(new SteelDesignParams { LefX = 6, LefY = 0.6 }), new SteelForces(-500, 10, 3, 0, 0));
        Assert.Equal("(116)", res[0].Formula);
        Assert.Contains(res, x => x.Formula == "(109)" && x.Notes.Any(n => n.Contains("λx")));
    }

    [Fact]
    public void UnequalFlanges_EtaAsType8()
    {
        var s = new PolygonSection(
        [
            (-0.06, 0), (0.06, 0), (0.06, 0.010), (0.004, 0.010), (0.004, 0.388), (0.1, 0.388), (0.1, 0.4),
            (-0.1, 0.4), (-0.1, 0.388), (-0.004, 0.388), (-0.004, 0.010), (-0.06, 0.010),
        ]);
        var m = Sp16Member.Create(s, C245, new SteelDesignParams { LefX = 6, LefY = 3 });
        var r = Sp16Section9.BiaxialStability(m, new SteelForces(-400, -30, 3, 0, 0))[0];
        Assert.Equal("(116)", r.Formula);
        Assert.NotEqual(CheckStatus.NotApplicable, r.Status);
        Assert.Contains(r.Notes, n => n.Contains("типа 8"));
    }

    [Fact]
    public void Tee_MyNotInTableD2_NotApplicable()
    {
        var m = Sp16Member.Create(new PolygonSection(TemplatePoints.TeePoints(0.150, 0.200, 0.010, 0.014)), C245, new SteelDesignParams());
        var r = Sp16Section9.BiaxialStability(m, new SteelForces(-100, 5, 1, 0, 0)).Single();
        Assert.Equal(CheckStatus.NotApplicable, r.Status);
        Assert.Contains(r.Notes, n => n.StartsWith("φey"));
    }

    [Fact]
    public void Pipe_NotInTable21_NotApplicable()
    {
        var outer = TemplatePoints.CirclePoints(0.1, 64);
        var inner = TemplatePoints.CirclePoints(0.09, 64);
        var m = Sp16Member.Create(new PolygonSection(outer, [inner]), C245, new SteelDesignParams());
        var r = Sp16Section9.BiaxialStability(m, new SteelForces(-100, 5, 1, 0, 0)).Single();
        Assert.Equal(CheckStatus.NotApplicable, r.Status);
        Assert.Equal("9.2.9", r.Clause);
    }

    // ── 9.2.10 ──

    [Fact]
    public void Box_Formulas120And121_HandCalc()
    {
        var m = Box(new SteelDesignParams { LefX = 8, LefY = 8 });
        var res = Sp16Section9.BiaxialStability(m, new SteelForces(-2000, 150, -60, 0, 0));
        Assert.Equal(["(120)", "(121)"], res.Select(x => x.Formula));
        var s = m.S;
        var e1 = Sp16Tables.TableE1(s)!;
        double pex = Sp16Section9.PhiE(m, 2000, 150, true).PhiE, pey = Sp16Section9.PhiE(m, 2000, -60, false).PhiE;
        double lbx = m.LambdaBar(true), lby = m.LambdaBar(false);
        double dx = lbx <= 1 ? 1 : 1 - 0.1 * 2000 * lbx * lbx / (s.A * Ry);
        double dy = lby <= 1 ? 1 : 1 - 0.1 * 2000 * lby * lby / (s.A * Ry);
        Assert.True(lbx > 1 && lby > 1);
        Assert.Equal(2000 / (pey * s.A * Ry) + 150 / (e1.Cx * dx * s.WxMin * Ry), res[0].Utilization, 9);
        Assert.Equal(2000 / (pex * s.A * Ry) + 60 / (e1.Cy * dy * s.WyMin * Ry), res[1].Utilization, 9);
    }

    [Fact]
    public void Box_Formula121a_WhenRequested()
    {
        var m = Box(new SteelDesignParams { LefX = 8, LefY = 8, UseFormula121a = true });
        var r = Sp16Section9.BiaxialStability(m, new SteelForces(-2000, 150, -60, 0, 0)).Single();
        double pex = Sp16Section9.PhiE(m, 2000, 150, true).PhiE, pey = Sp16Section9.PhiE(m, 2000, -60, false).PhiE;
        Assert.Equal("(121а)", r.Formula);
        Assert.Equal(2000 / (m.S.A * Ry) * (1 / pex + 1 / pey - 1), r.Utilization, 9);
    }

    [Fact]
    public void Box_SingleMx_OutOfPlane120WithPhiY()
    {
        var m = Box(new SteelDesignParams { LefX = 8, LefY = 8 });
        var r = Sp16Section9.OutOfPlaneStability(m, new SteelForces(-2000, 150, 0, 0, 0)).Single();
        Assert.Equal("(120)", r.Formula);
        Assert.Equal("9.2.10", r.Clause);
        var s = m.S;
        double phiY = m.Phi(false)!.Value, lbx = m.LambdaBar(true);
        double dx = 1 - 0.1 * 2000 * lbx * lbx / (s.A * Ry);
        Assert.Equal(2000 / (phiY * s.A * Ry) + 150 / (Sp16Tables.TableE1(s)!.Cx * dx * s.WxMin * Ry), r.Utilization, 9);
    }

    [Fact]
    public void Box_Delta122_NonPositive_Fails()
    {
        var m = Box(new SteelDesignParams { LefX = 40, LefY = 8 });
        var r = Sp16Section9.OutOfPlaneStability(m, new SteelForces(-3500, 50, 0, 0, 0)).Single();
        Assert.True(Var(r, "δx") <= 0);
        Assert.Equal(CheckStatus.Fail, r.Status);
    }
}
