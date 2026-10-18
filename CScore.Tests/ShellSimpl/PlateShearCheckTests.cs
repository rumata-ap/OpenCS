using CScore.ShellSimpl;
using CScore.Sp63Shear;
using Xunit;
using static CScore.Tests.ShellSimpl.StripNdmFixture;

namespace CScore.Tests.ShellSimpl;

/// <summary>
/// Поперечная сила в пластине (спека, раздел 8; раздел 10, п. 9): (8.55), (8.60)–(8.61), φn по п. 8.1.34,
/// требуемое qsw по (8.62) и порог 0,25·Rbt·b п. 8.1.33. Полоса фикстуры: h = 0,2 м, ряды x/y в 30 мм
/// от граней (h0 = 0,17 м), B25 по виду C: Rb = 14 500 кПа, Rbt = 1 050 кПа.
/// </summary>
public sealed class PlateShearCheckTests
{
    const double Rb = 14_500.0, Rbt = 1_050.0, H0 = 0.17;

    static PlateShearResult? Check(double qx, double qy, double nx = 0, double ny = 0, double nxy = 0,
        double mx = 10, double my = 0, double mxy = 0, PlateSection? plate = null) =>
        PlateShearCheck.Check(qx, qy, nx, ny, nxy, mx, my, mxy, plate ?? Plate(), Concrete(), Rebar(), CalcType.C);

    [Fact]
    public void ZeroShear_NoBlock()
    {
        Assert.Null(Check(0, 0));
        Assert.Null(Check(1e-12, -1e-12));
    }

    [Fact]
    public void Direction_And_Magnitude_FromQxQy()
    {
        var r = Check(30, 40, nx: 100, ny: 200, nxy: 50, mx: 10, my: 20, mxy: 5)!;

        double t = Math.Atan2(40, 30), c = Math.Cos(t), s = Math.Sin(t);
        Assert.Equal(50.0, r.Q0, 9);
        Assert.Equal(53.130102, r.ThetaDeg, 5);
        Assert.Equal(100 * c * c + 200 * s * s + 2 * 50 * c * s, r.N, 9);
        Assert.Equal(10 * c * c + 20 * s * s + 2 * 5 * c * s, r.M, 9);
        Assert.Equal("top", r.TensionFace);
    }

    [Fact]
    public void Formula855_And_861_WithoutAxialForce()
    {
        var r = Check(60, 0)!;

        Assert.Equal(1.0, r.PhiN, 12);
        Assert.Equal(H0, r.H0, 9);
        Assert.Equal(0.3 * Rb * 1.0 * H0, r.QStrip, 6);        // (8.55): 739,5 кН/м
        Assert.Equal(0.5 * Rbt * 1.0 * H0, r.Qb1, 6);          // (8.61): 89,25 кН/м
        Assert.Equal(60.0 / 739.5, r.EtaStrip, 9);
        Assert.Equal(60.0 / 89.25, r.EtaConcrete, 9);
        Assert.Equal(60.0 / 89.25, r.Eta, 9);
        Assert.False(r.StirrupsRequired);
        Assert.Equal(0.0, r.QswRequired);
        Assert.Equal(0.0, r.AswPerSw);
    }

    [Fact]
    public void RequiredQsw_Formula862_AboveThreshold()
    {
        var r = Check(150, 0)!;

        double qsw = (150.0 - 89.25) / H0;                   // 357,4 кН/м² > 0,25·Rbt·b = 262,5
        Assert.True(r.StirrupsRequired);
        Assert.Equal(0.25 * Rbt, r.QswMin, 9);
        Assert.Equal(qsw, r.QswRequired, 6);
        // Класс арматуры фикстуры не задан — Rsw ограничено 300 МПа (табл. 6.15) с оговоркой.
        Assert.Equal(StirrupResolver.MaxRsw, r.Rsw, 6);
        Assert.Equal(qsw / 300_000.0, r.AswPerSw, 12);
        Assert.Contains(r.Notes, n => n.Contains("6.15"));
        Assert.True(r.EtaStrip <= 1.0);
    }

    [Fact]
    public void RequiredQsw_BelowThreshold_TakesQuarterRbt()
    {
        // (100 − 89,25)/0,17 = 63,2 кН/м² < 262,5 — хомуты с меньшим qsw не учитываются (п. 8.1.33).
        var r = Check(100, 0)!;

        Assert.True(r.StirrupsRequired);
        Assert.Equal(0.25 * Rbt, r.QswRequired, 9);
    }

    [Fact]
    public void Rsw_FromTable615_ByRebarClass()
    {
        var rebar = Rebar();
        foreach (var ch in rebar.MaterialChars) ch.Class = 400;
        var r = PlateShearCheck.Check(150, 0, 0, 0, 0, 10, 0, 0, Plate(), Concrete(), rebar, CalcType.C)!;

        Assert.Equal(280_000.0, r.Rsw, 6);
        Assert.Equal(r.QswRequired / 280_000.0, r.AswPerSw, 12);
        Assert.DoesNotContain(r.Notes, n => n.Contains("6.15"));
    }

    [Fact]
    public void Formula855_Violated_IsReported()
    {
        var r = Check(800, 0)!;

        Assert.True(r.EtaStrip > 1.0);
        Assert.Equal(800.0 / 739.5, r.EtaStrip, 9);
        Assert.Contains(r.Notes, n => n.Contains("(8.55)"));
    }

    [Fact]
    public void PhiN_Compression_AppliesTo855And861()
    {
        var r = Check(100, 0, nx: -1000)!;

        double asN = 2 * AsPerMeter;
        double nu = Rb / (0.002 * 30_000_000.0);
        double aRed = H + 200_000_000.0 / 30_000_000.0 / nu * asN;
        double phi = PhiNCalculator.CompressionValue(1000.0 / aRed, Rb);
        Assert.Equal(1.25, phi, 12);                           // σcp ≈ 0,30·Rb — полка
        Assert.Equal(phi, r.PhiN, 9);
        Assert.True(r.PhiNToStrip);
        Assert.Equal(phi * 0.3 * Rb * H0, r.QStrip, 6);
        Assert.Equal(phi * 0.5 * Rbt * H0, r.Qb1, 6);
    }

    [Fact]
    public void PhiN_Tension_AppliesTo861Only()
    {
        var r = Check(100, 0, nx: 200)!;

        double asN = 2 * AsPerMeter;
        double nu = Rbt / (0.0001 * 30_000_000.0);
        double aRed = H + 200_000_000.0 / 30_000_000.0 / nu * asN;
        double phi = 1.0 - 200.0 / aRed / (2.0 * Rbt);
        Assert.Equal(phi, r.PhiN, 9);
        Assert.False(r.PhiNToStrip);
        Assert.Equal(0.3 * Rb * H0, r.QStrip, 6);              // (8.55) без φn
        Assert.Equal(phi * 0.5 * Rbt * H0, r.Qb1, 6);
    }

    [Fact]
    public void PhiN_UsesGivenMeshArea_InDesign()
    {
        // σcp = 1000/0,2 = 5000 кПа = 0,345·Rb без арматуры; с N = −2000 — 0,69·Rb, ниспадающая ветвь.
        var b = PlateShearCheck.Check(100, 0, -2000, 0, 0, 10, 0, 0, Plate(), Concrete(), Rebar(), CalcType.C, asNTotal: 0.0)!;
        var a = Check(100, 0, nx: -2000)!;

        double sigma = 2000.0 / H;                             // A_red без арматуры
        Assert.Equal(PhiNCalculator.CompressionValue(sigma, Rb), b.PhiN, 9);
        Assert.Equal(2.5 * (1.0 - sigma / Rb), b.PhiN, 9);
        Assert.True(a.PhiN > b.PhiN);                          // арматура сечения увеличивает A_red
    }

    [Fact]
    public void H0_SmallerOfXY_AtTensionFace()
    {
        var plate = Plate();
        plate.RebarLayers[1].Zsy = 0.058;                      // верхний ряд y глубже: h0,y = 0,158
        plate.RebarLayers[0].Zsx = -0.06;                      // нижний ряд x: h0,x = 0,160

        Assert.Equal(0.158, Check(60, 0, mx: 10, plate: plate)!.H0, 9);
        var bot = Check(60, 0, mx: -10, plate: plate)!;
        Assert.Equal("bot", bot.TensionFace);
        Assert.Equal(0.16, bot.H0, 9);
        var none = Check(60, 0, mx: 0, plate: plate)!;
        Assert.Equal("", none.TensionFace);
        Assert.Equal(0.158, none.H0, 9);
    }

    [Fact]
    public void NoRebarAtTensionFace_FallsBackTo09h()
    {
        var plate = Plate();
        plate.RebarLayers.RemoveAt(1);                         // верхних рядов нет

        var r = Check(60, 0, mx: 10, plate: plate)!;
        Assert.Equal(0.9 * H, r.H0, 9);
        Assert.Contains(r.Notes, n => n.Contains("0,9·h"));
    }

    [Fact]
    public void Solve_ShearEntersEtaMax_OnlyForUls()
    {
        var uls = ShellSimplSolver.Solve(new ShellSimplSolver.SolveParams(0, 0, 0, 5, 0, 0, "shell_simpl_capri_uls",
            Model: StripModel.Ndm, Qx: 150), Plate(), Concrete(), Rebar(), CalcType.C);
        Assert.NotNull(uls.Shear);
        Assert.Equal(uls.Shear!.Eta, uls.EtaMax!.Value, 12);
        Assert.True(uls.EtaMax > 1.0);

        var wa = ShellSimplSolver.Solve(new ShellSimplSolver.SolveParams(0, 0, 0, 5, 0, 0, "shell_simpl_wa_uls",
            Qy: 60), Plate(), Concrete(), Rebar(), CalcType.C);
        Assert.NotNull(wa.Shear);
        Assert.Equal(90.0, wa.Shear!.ThetaDeg, 9);
        Assert.True(wa.EtaMax >= wa.Shear.Eta);

        var noQ = ShellSimplSolver.Solve(new ShellSimplSolver.SolveParams(0, 0, 0, 5, 0, 0, "shell_simpl_capri_uls"),
            Plate(), Concrete(), Rebar(), CalcType.C);
        Assert.Null(noQ.Shear);

        var sls = ShellSimplSolver.Solve(new ShellSimplSolver.SolveParams(0, 0, 0, 5, 0, 0, "shell_simpl_capri_sls",
            Qx: 150), Plate(), Concrete(), Rebar(), CalcType.N);
        Assert.Null(sls.Shear);
    }

    [Fact]
    public void Result_SerializesWithoutInfinity()
    {
        var plate = Plate();
        var r = PlateShearCheck.Check(100, 0, 100_000, 0, 0, 10, 0, 0, plate, Concrete(), Rebar(), CalcType.C)!;
        Assert.Equal(0.0, r.PhiN, 12);                         // огромное растяжение — φn = 0
        Assert.Equal(PlateShearCheck.Infinite, r.EtaConcrete);
        string json = System.Text.Json.JsonSerializer.Serialize(r);
        Assert.Contains("\"Eta\"", json);
    }
}
