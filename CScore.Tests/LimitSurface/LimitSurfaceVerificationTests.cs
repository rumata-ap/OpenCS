using System.Diagnostics;
using CScore.LimitSurface;
using Xunit;
using Xunit.Abstractions;

namespace CScore.Tests.LimitSurface;

/// <summary>
/// Сверка сетки с LimitForceSolverFast (режим Moment). Мера — абсолютная: ΔM = |Mпред сетки − Mпред решателя|,
/// отнесённая к M_max поверхности (относительная |Δη| при малом Mпред раздувается и только выводится).
/// Эталон принимается, если его плоскость на границе области (λ = 1 ± 5e-3 по модели решателя — без (8.53))
/// и сама даёт заявленные усилия (невязка ≤ 0,1 %; иначе точка исключается и выводится);
/// λ &lt; 1 − 5e-3 — FAIL «решатель нарушает предельные деформации». Точки, где начало луча вне тела
/// (преднапряжение, растяжение), неоднозначны — луч пересекает поверхность дважды — и только подсчитываются.
/// </summary>
public class LimitSurfaceVerificationTests(ITestOutputHelper output)
{
    const double LambdaTol = 5e-3;
    const double MomentLoad = 100;

    public static TheoryData<string> Sections => new() { "symmetric", "asymmetric", "prestressed" };

    static CrossSection Make(string name) => name switch
    {
        "symmetric" => LimitSurfaceTestSections.Symmetric(),
        "asymmetric" => LimitSurfaceTestSections.Asymmetric(),
        _ => TestSections.RectWithEccentricPrestressedRebar(),
    };

    sealed record RefPoint(double N, double Mx, double My, double Eta, bool SingleSign);

    /// <summary>Сравнение точки: ΔM/M_max, относительная |Δη|, знак (сетка консервативна — Mпред сетки ниже).</summary>
    sealed record Compared(RefPoint P, SurfaceUtilization U, double DmRel, double EtaRel, bool MeshConservative);

    static double MaxMoment(LimitSurfaceMesh mesh) => mesh.Vertices.Max(v => Math.Sqrt(v.Mx * v.Mx + v.My * v.My));

    List<RefPoint> Reference(CrossSection section, LimitSurfaceMesh mesh, int count, int seed)
    {
        var domainSolverModel = new UlsStrainCriterion(CalcType.C, ten: false).BuildDomain(section).WithSingleSignRule(false);
        var domainNorm = new UlsStrainCriterion(CalcType.C, ten: false).BuildDomain(section);
        var solver = new LimitForceSolverFast(section, CalcType.C, ten: false);
        var calc = section.CloneForCalc();
        double nMin = mesh.Vertices.Min(v => v.N), nMax = mesh.Vertices.Max(v => v.N), mMax = MaxMoment(mesh);
        var rnd = new Random(seed);
        var res = new List<RefPoint>();
        var violations = new List<string>();
        int offTarget = 0;
        for (int i = 0; i < count; i++)
        {
            double n = 0.95 * (nMin + (nMax - nMin) * rnd.NextDouble());
            double a = rnd.NextDouble() * 2 * Math.PI, mx = MomentLoad * Math.Cos(a), my = MomentLoad * Math.Sin(a);
            var r = solver.MomentFactor(n, mx, my);
            if (!r.Converged || r.StrainPlane is not { } k) continue;
            double lam = domainSolverModel.Boundary(k.e0, k.ky, k.kz).Lambda;
            string desc = $"N={n:F1} M=({mx:F1};{my:F1}) λ={lam:F4} e0={k.e0:G4} ky={k.ky:G4} kz={k.kz:G4}";
            if (lam < 1 - LambdaTol) { violations.Add(desc); continue; }
            if (lam > 1 + LambdaTol) continue;   // не на границе — не эталон
            // Плоскость решателя должна давать заявленные усилия, иначе эталон неверен (сходимость по направлению M).
            var f = calc.Compute(k, CalcType.C, false, computeStiffness: false);
            double miss = Math.Max(Math.Abs(f.N - n) / (nMax - nMin),
                Math.Sqrt(Math.Pow(f.Mx - mx * r.Factor, 2) + Math.Pow(f.My - my * r.Factor, 2)) / mMax);
            if (miss > 1e-3)
            {
                offTarget++;
                output.WriteLine($"  плоскость решателя не даёт заявленных усилий (невязка {100 * miss:F2} %): {desc}");
                continue;
            }
            if (lam < 1 - 1e-3) output.WriteLine($"  решатель чуть за границей (сходимость): {desc}");
            bool single = domainNorm.Boundary(k.e0, k.ky, k.kz).Active.SingleSign;
            res.Add(new RefPoint(n, mx, my, 1 / r.Factor, single));
        }
        if (offTarget > 0) output.WriteLine($"  исключено из эталона по невязке усилий: {offTarget}");
        Assert.True(violations.Count == 0, "Решатель нарушает предельные деформации:\n" + string.Join("\n", violations));
        return res;
    }

    /// <summary>Сравнивает эталон с сеткой; точки с началом луча вне тела отбрасываются и подсчитываются.</summary>
    List<Compared> Compare(string name, LimitSurfaceMesh mesh, List<RefPoint> refs)
    {
        double mMax = MaxMoment(mesh);
        var res = new List<Compared>();
        int outside = 0;
        foreach (var p in refs)
        {
            var u = mesh.Utilization(p.N, p.Mx, p.My, UtilizationMode.Moment);
            if (!u.RayOriginInside) { outside++; continue; }
            double capMesh = MomentLoad / u.Eta, capSolver = MomentLoad / p.Eta;
            res.Add(new Compared(p, u, Math.Abs(capMesh - capSolver) / mMax, Math.Abs(u.Eta / p.Eta - 1), capMesh <= capSolver));
        }
        output.WriteLine($"{name}: {refs.Count} эталонных точек, в сверке {res.Count}, начало луча вне тела {outside}");
        if (res.Count > 0)
        {
            var worst = res.MaxBy(c => c.DmRel)!;
            output.WriteLine($"{name}: max ΔM/M_max = {100 * worst.DmRel:F3} %, max |Δη| = {100 * res.Max(c => c.EtaRel):F3} %");
            output.WriteLine($"  худшая: {Describe(name, worst)}, плоскость решателя {(worst.P.SingleSign ? "однозначная" : "двузначная")}");
        }
        return res;
    }

    static string Describe(string name, Compared c) =>
        $"{name}: N={c.P.N:F1} M=({c.P.Mx:F1};{c.P.My:F1}) η сетки {c.U.Eta:F4}, решателя {c.P.Eta:F4}, ΔM/M_max {100 * c.DmRel:F3} %";

    [Theory]
    [MemberData(nameof(Sections))]
    public void RuleOff_MatchesSolverEverywhere(string name)
    {
        var section = Make(name);
        var sw = Stopwatch.StartNew();
        var mesh = new StrainDomainMesher(section, new UlsStrainCriterion(CalcType.C, ten: false),
            new LimitSurfaceOptions { ApplySingleSignRule = false }).Build();
        output.WriteLine($"{name}: сетка {mesh.MeridianCount} меридианов, {mesh.Vertices.Count} вершин, {sw.ElapsedMilliseconds} мс");
        var cmp = Compare(name, mesh, Reference(section, mesh, 60, 11));
        Assert.True(cmp.Count >= 20, $"мало валидных эталонных точек: {cmp.Count}");
        foreach (var c in cmp)
            Assert.True(c.DmRel <= 0.006, Describe(name, c));
    }

    /// <summary>С правилом (8.53): в однозначной зоне сетка не выше решателя — решатель (8.53) не учитывает.</summary>
    [Theory]
    [MemberData(nameof(Sections))]
    public void RuleOn_TwoSignMatches_SingleSignNotAboveSolver(string name)
    {
        var section = Make(name);
        var mesh = new StrainDomainMesher(section, new UlsStrainCriterion(CalcType.C, ten: false)).Build();
        foreach (var c in Compare(name, mesh, Reference(section, mesh, 60, 12)))
        {
            if (c.P.SingleSign)
                Assert.True(c.MeshConservative || c.DmRel <= 0.006, "однозначная зона, сетка выше решателя — " + Describe(name, c));
            else
                Assert.True(c.DmRel <= 0.006, "двузначная зона — " + Describe(name, c));
        }
    }

    [Fact]
    public void FinerTolerance_Converges()
    {
        var section = LimitSurfaceTestSections.Asymmetric();
        var mesh = new StrainDomainMesher(section, new UlsStrainCriterion(CalcType.C, ten: false),
            new LimitSurfaceOptions { ApplySingleSignRule = false, RefineTol = 0.001 }).Build();
        output.WriteLine($"tol 0.001: {mesh.MeridianCount} меридианов");
        var cmp = Compare("asymmetric", mesh, Reference(section, mesh, 60, 11));
        Assert.True(cmp.Max(c => c.DmRel) <= 0.003);
    }
}
