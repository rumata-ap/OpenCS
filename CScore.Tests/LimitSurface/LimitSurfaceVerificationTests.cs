using System.Diagnostics;
using CScore.LimitSurface;
using Xunit;
using Xunit.Abstractions;

namespace CScore.Tests.LimitSurface;

/// <summary>
/// Сверка сетки с LimitForceSolverFast (режим Moment). Мера — абсолютная: ΔM = |Mпред сетки − Mпред решателя|,
/// отнесённая к M_max поверхности (относительная |ΔКисп| при малом Mпред раздувается и только выводится).
/// Эталон принимается, если его плоскость на границе области (λ = 1 ± 5e-3 по модели решателя — без (8.53))
/// и сама даёт заявленные усилия; λ &lt; 0,98 (за собственным допуском решателя на деформации — 2 %)
/// или невязка усилий > 0,1 % — FAIL (дефект решателя; 0,98 ≤ λ &lt; 0,995 — не эталон),
/// см. LimitForceSolverFastResidualTests). Точки, где начало луча вне тела
/// (преднапряжение, растяжение), неоднозначны — луч пересекает поверхность дважды — и только подсчитываются.
/// </summary>
public class LimitSurfaceVerificationTests(ITestOutputHelper output)
{
    const double LambdaTol = 5e-3;
    /// <summary>Собственный допуск решателя на превышение предельных деформаций (LimitForceSolverFast.IsValidSolution, 2 %).</summary>
    const double SolverStrainTol = 0.02;
    const double MomentLoad = 100;

    public static TheoryData<string> Sections => new() { "symmetric", "asymmetric", "prestressed" };

    static CrossSection Make(string name) => name switch
    {
        "symmetric" => LimitSurfaceTestSections.Symmetric(),
        "asymmetric" => LimitSurfaceTestSections.Asymmetric(),
        _ => TestSections.RectWithEccentricPrestressedRebar(),
    };

    sealed record RefPoint(double N, double Mx, double My, double Ratio, bool SingleSign);

    /// <summary>Сравнение точки: ΔM/M_max, относительная |ΔКисп|, знак (сетка консервативна — Mпред сетки ниже).</summary>
    sealed record Compared(RefPoint P, SurfaceUtilization U, double DmRel, double RatioRel, bool MeshConservative);

    static double MaxMoment(LimitSurfaceMesh mesh) => mesh.Vertices.Max(v => Math.Sqrt(v.Mx * v.Mx + v.My * v.My));

    List<RefPoint> Reference(CrossSection section, LimitSurfaceMesh mesh, int count, int seed)
    {
        var domainSolverModel = new UlsStrainCriterion(CalcType.C, ten: false).BuildDomain(section).WithSingleSignRule(false);
        var domainNorm = new UlsStrainCriterion(CalcType.C, ten: false).BuildDomain(section);
        var solver = new LimitForceSolverFast(section, CalcType.C, ten: false);
        var calc = section.CloneForCalc();
        // Диапазон выборки — по полюсам, а не по крайним вершинам: он не должен зависеть от деталей сетки
        // (по (8.53) вершины рядом со сжатым полюсом сжаты сильнее самого полюса).
        double nMin = Math.Min(mesh.Vertices[0].N, mesh.Vertices[1].N), nMax = Math.Max(mesh.Vertices[0].N, mesh.Vertices[1].N);
        double mMax = MaxMoment(mesh);
        var rnd = new Random(seed);
        var res = new List<RefPoint>();
        var violations = new List<string>();
        for (int i = 0; i < count; i++)
        {
            double n = 0.95 * (nMin + (nMax - nMin) * rnd.NextDouble());
            double a = rnd.NextDouble() * 2 * Math.PI, mx = MomentLoad * Math.Cos(a), my = MomentLoad * Math.Sin(a);
            var r = solver.MomentFactor(n, mx, my);
            if (!r.Converged || r.StrainPlane is not { } k) continue;
            double lam = domainSolverModel.Boundary(k.e0, k.ky, k.kz).Lambda;
            string desc = $"N={n:F1} M=({mx:F1};{my:F1}) λ={lam:F4} e0={k.e0:G4} ky={k.ky:G4} kz={k.kz:G4}";
            if (lam < 1 - SolverStrainTol) { violations.Add(desc); continue; }
            if (Math.Abs(lam - 1) > LambdaTol) continue;   // не на границе в пределах допуска сетки — не эталон
            // Плоскость решателя должна давать заявленные усилия, иначе эталон неверен.
            var f = calc.Compute(k, CalcType.C, false, computeStiffness: false);
            double miss = Math.Max(Math.Abs(f.N - n) / (nMax - nMin),
                Math.Sqrt(Math.Pow(f.Mx - mx * r.Factor, 2) + Math.Pow(f.My - my * r.Factor, 2)) / mMax);
            if (miss > 1e-3) { violations.Add($"невязка усилий {100 * miss:F2} %: {desc}"); continue; }
            if (lam < 1 - 1e-3) output.WriteLine($"  решатель чуть за границей (сходимость): {desc}");
            bool single = domainNorm.Boundary(k.e0, k.ky, k.kz).Active.SingleSign;
            res.Add(new RefPoint(n, mx, my, 1 / r.Factor, single));
        }
        Assert.True(violations.Count == 0, "Плоскость решателя за границей области или не даёт заявленных усилий:\n" + string.Join("\n", violations));
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
            double capMesh = MomentLoad / u.Ratio, capSolver = MomentLoad / p.Ratio;
            res.Add(new Compared(p, u, Math.Abs(capMesh - capSolver) / mMax, Math.Abs(u.Ratio / p.Ratio - 1), capMesh <= capSolver));
        }
        output.WriteLine($"{name}: {refs.Count} эталонных точек, в сверке {res.Count}, начало луча вне тела {outside}");
        if (res.Count > 0)
        {
            var worst = res.MaxBy(c => c.DmRel)!;
            output.WriteLine($"{name}: max ΔM/M_max = {100 * worst.DmRel:F3} %, max |ΔКисп| = {100 * res.Max(c => c.RatioRel):F3} %");
            output.WriteLine($"  худшая: {Describe(name, worst)}, плоскость решателя {(worst.P.SingleSign ? "однозначная" : "двузначная")}");
        }
        return res;
    }

    static string Describe(string name, Compared c) =>
        $"{name}: N={c.P.N:F1} M=({c.P.Mx:F1};{c.P.My:F1}) Кисп сетки {c.U.Ratio:F4}, решателя {c.P.Ratio:F4}, ΔM/M_max {100 * c.DmRel:F3} %";

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
