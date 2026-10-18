using Xunit;
using Xunit.Abstractions;
using static CScore.Tests.ShellSimpl.StripNdmFixture;

namespace CScore.Tests.ShellSimpl;

/// <summary>Вуд–Армер и Капра–Мори с моделью полосы «НДМ» (<see cref="StripModel.Ndm"/>).</summary>
public sealed class ShellSimplNdmTests(ITestOutputHelper output)
{
    // Те же наборы, что в ShellSimplCapriDirectionTests: Nx, Ny, Nxy, Mx, My, Mxy.
    static readonly double[] CaseA = [-50, -30, 15, 18, 8, 10];   // прочность
    static readonly double[] CaseB = [-20, -10, 8, 12, 5, 7];     // трещиностойкость
    // Растяжение больше сопротивления растянутого бетона (1,55 МПа · 0,2 м ≈ 310 кН/м), малые моменты.
    static readonly double[] CaseT = [400, 100, 10, 6, 3, 2];

    static ShellSimplSolver.SolveResult Run(double[] f, string kind, StripModel model, double acrcScale = 1.0) =>
        ShellSimplSolver.Solve(
            new ShellSimplSolver.SolveParams(f[0], f[1], f[2], acrcScale * f[3], acrcScale * f[4], acrcScale * f[5],
                kind, 5.0, 0.3, 1.0, 0.5, Model: model),
            Plate(), Concrete(), Rebar(), kind.EndsWith("sls") ? CalcType.N : CalcType.C);

    // 18.10.2026: ВА η 0,676 (формулы) → 0,631 (НДМ), КМ 0,592 → 0,545; ПС2 при моментах ×2,5:
    // ВА a_crc 0,374 → 0,337 мм, КМ 0,334 → 0,284 мм. НДМ мягче формул: M_crc по деформационной
    // модели выше Rbt·1,3·Wred, сжатая зона — по диаграмме, а не прямоугольником.
    [Theory]
    [InlineData("shell_simpl_wa_uls")]
    [InlineData("shell_simpl_capri_uls")]
    public void Uls_NdmEtaFiniteAndCloseToFormulas(string kind)
    {
        var f = Run(CaseA, kind, StripModel.Formulas);
        var n = Run(CaseA, kind, StripModel.Ndm);
        output.WriteLine($"{kind}: η формулы = {f.EtaMax:F4}, η НДМ = {n.EtaMax:F4}");
        Assert.True(double.IsFinite(n.EtaMax!.Value) && n.EtaMax > 0);
        Assert.InRange(n.EtaMax!.Value / f.EtaMax!.Value, 0.8, 1.2);

        var strips = n.WaStrips ?? n.CapriDirs!.Select(d => d.Strip).ToList();
        Assert.All(strips, s => Assert.Equal("ndm", s.Model));
        Assert.Contains(strips, s => s.Pivot is "A" or "B" or "C");
    }

    [Theory]
    [InlineData("shell_simpl_wa_sls")]
    [InlineData("shell_simpl_capri_sls")]
    public void Sls_NdmAcrcFiniteAndSameOrder(string kind)
    {
        // Моменты ×2,5, чтобы полосы надёжно трещали в обеих моделях.
        var f = Run(CaseB, kind, StripModel.Formulas, 2.5);
        var n = Run(CaseB, kind, StripModel.Ndm, 2.5);
        double Max(ShellSimplSolver.SolveResult r) =>
            (r.WaStrips ?? r.CapriDirs!.Select(d => d.Strip).ToList()).Where(s => !s.NoRebar).Max(s => s.Acrc_mm);
        output.WriteLine($"{kind}: a_crc формулы = {Max(f):F4}, НДМ = {Max(n):F4}");
        Assert.True(Max(n) > 0 && double.IsFinite(Max(n)));
        Assert.InRange(Max(n) / Max(f), 0.5, 2.0);
    }

    [Fact]
    public void Capri_ResultComesInOrthogonalPairs()
    {
        var r = Run(CaseA, "shell_simpl_capri_uls", StripModel.Ndm);
        foreach (var d in r.CapriDirs!.Where(d => d.Alpha_deg < 90.0))
            Assert.Contains(r.CapriDirs!, o => Math.Abs(o.Alpha_deg - d.Alpha_deg - 90.0) < 1e-9);
        Assert.NotNull(r.CriticalTop);
        Assert.Equal("ndm", r.CriticalTop!.Strip.Model);
    }

    // Сквозное растяжение: НДМ сама даёт трещины по обоим рядам, обе грани растрескались.
    [Fact]
    public void Wa_Sls_ThroughTension_BothFacesCracked()
    {
        var n = Run(CaseT, "shell_simpl_wa_sls", StripModel.Ndm);
        var x = n.WaStrips!.Where(s => s.Name.StartsWith("x")).ToList();
        Assert.Equal(2, x.Count);
        Assert.All(x, s => Assert.True(s.Cracked && s.Acrc_mm > 0));
    }

    // Обработчики задач пишут результат через JsonSerializer, а он не принимает ∞ и NaN.
    [Theory]
    [InlineData("shell_simpl_wa_uls")]
    [InlineData("shell_simpl_capri_uls")]
    [InlineData("shell_simpl_wa_sls")]
    [InlineData("shell_simpl_capri_sls")]
    public void Result_SerializesToJson_IncludingZeroLoad(string kind)
    {
        foreach (var f in new[] { new double[6], CaseA, CaseT })
        {
            var json = System.Text.Json.JsonSerializer.Serialize(Run(f, kind, StripModel.Ndm));
            Assert.Contains("\"Model\":\"ndm\"", json);
        }
    }

    [Fact]
    public void FormulasRemainDefault()
    {
        var p = new ShellSimplSolver.SolveParams(0, 0, 0, 10, 0, 0, "shell_simpl_wa_uls");
        Assert.Equal(StripModel.Formulas, p.Model);
        var r = ShellSimplSolver.Solve(p, Plate(), Concrete(), Rebar(), CalcType.C);
        Assert.All(r.WaStrips!, s => Assert.Equal("formulas", s.Model));
    }
}
