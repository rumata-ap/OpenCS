using CScore.LimitSurface;
using Xunit;
using Xunit.Abstractions;

namespace CScore.Tests.LimitSurface;

/// <summary>
/// Сверка поверхности ПС2 с CrackingSolver.CrackingMoment (режим Moment: N = const, направление момента).
/// Мера — ΔM/M_max, как у ПС1. Эталон принимается, если его плоскость на границе области по модели решателя
/// (εbt2 при любой эпюре — без (8.54)): λ = 1 ± LambdaTol и активна трещина. Плоскость за пределом растяжения —
/// FAIL (дефект решателя); за пределом сжатия — пропуск (у решателя этого предела нет).
/// </summary>
[Trait("Category", "Slow")]
public class CrackSurfaceVerificationTests(ITestOutputHelper output)
{
    const double LambdaTol = 1e-2;
    const double MomentLoad = 10;

    public static TheoryData<string> Sections => new() { "symmetric", "asymmetric", "plain", "prestressed" };

    static CrossSection Make(string name) => name switch
    {
        "symmetric" => LimitSurfaceTestSections.Symmetric(),
        "asymmetric" => LimitSurfaceTestSections.Asymmetric(),
        "plain" => LimitSurfaceTestSections.Rect([]),
        _ => TestSections.RectWithEccentricPrestressedRebar(),
    };

    sealed record RefPoint(double N, double Mx, double My, double Ratio, bool SingleSign, double Lambda);

    sealed record Compared(RefPoint P, SurfaceUtilization U, double DmRel, bool MeshConservative);

    static double MaxMoment(LimitSurfaceMesh mesh) => mesh.Vertices.Max(v => Math.Sqrt(v.Mx * v.Mx + v.My * v.My));

    List<RefPoint> Reference(CrossSection section, LimitSurfaceMesh mesh, int count, int seed)
    {
        var domainSolverModel = new CrackStrainCriterion().BuildDomain(section).WithSingleSignRule(false);
        var domainNorm = new CrackStrainCriterion().BuildDomain(section);
        double nMin = Math.Min(mesh.Vertices[0].N, mesh.Vertices[1].N), nMax = Math.Max(mesh.Vertices[0].N, mesh.Vertices[1].N);
        var rnd = new Random(seed);
        var res = new List<RefPoint>();
        var violations = new List<string>();
        int compression = 0, notConverged = 0, crackedAtZero = 0, otherFace = 0;
        for (int i = 0; i < count; i++)
        {
            // Половина точек — в растянутой части диапазона N, где работает (8.54).
            double n = i % 2 == 0
                ? nMin + (nMax - nMin) * (0.025 + 0.95 * rnd.NextDouble())
                : nMax * (0.975 * rnd.NextDouble());
            double a = rnd.NextDouble() * 2 * Math.PI, dx = Math.Cos(a), dy = Math.Sin(a);
            var zone = CrackingSolver.LoadedTensionZone(section, n, dx, dy, nAtZeroMoment: n);
            // Допуск бисекции решателя по умолчанию (1e-6, «сошлось» при 1e-5) — до 7 % от εbt2; для эталона ужесточён.
            var solver = new CrackingSolver(section, CalcType.N, bisectTol: 1e-10, bisectMaxIter: 100, tensionZone: zone);
            var r = solver.CrackingMoment(n, dx, dy);
            if (!r.Converged || r.StrainPlane is not { } k) { notConverged++; continue; }
            double mcrc = Math.Sqrt(r.Mx * r.Mx + r.My * r.My);
            // Трещина уже при нулевом моменте (растяжение N или обжатие) — законный ответ M_crc = 0; луч сетки
            // из (N, 0, 0) начинается вне тела, сверять нечего.
            if (mcrc < 1e-6) { crackedAtZero++; continue; }
            var (lam, act) = domainSolverModel.Boundary(k.e0, k.ky, k.kz);
            string desc = $"N={n:F1} α={a:F3} Mcrc={mcrc:F3} λ={lam:F4} e0={k.e0:G4} ky={k.ky:G4} kz={k.kz:G4}";
            if (act.Side == -1) { compression++; continue; }
            var p = domainSolverModel.Points[act.PointIndex];
            // Решатель смотрит только грань, растягиваемую нагрузкой; грань, растрескавшуюся от обжатия, поверхность
            // (полная деформация, решение пользователя) учитывает, а решатель нет — различие определений, не дефект.
            if (lam < 1 - LambdaTol && !zone(p.X, p.Y)) { otherFace++; continue; }
            if (lam < 1 - LambdaTol) { violations.Add(desc); continue; }
            if (lam > 1 + LambdaTol) continue;
            bool single = domainNorm.Boundary(k.e0, k.ky, k.kz).Active.SingleSign;
            res.Add(new RefPoint(n, MomentLoad * dx, MomentLoad * dy, MomentLoad / mcrc, single, lam));
        }
        output.WriteLine($"эталон: принято {res.Count} из {count}, не сошлось {notConverged}, ближе сжатие {compression}, " +
                         $"трещина при M = 0 {crackedAtZero}, другая грань (обжатие) {otherFace}");
        Assert.True(violations.Count == 0, "Плоскость решателя за пределом растяжения:\n" + string.Join("\n", violations));
        return res;
    }

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
            res.Add(new Compared(p, u, Math.Abs(capMesh - capSolver) / mMax, capMesh <= capSolver));
        }
        output.WriteLine($"{name}: в сверке {res.Count}, начало луча вне тела {outside}");
        if (res.Count > 0)
            output.WriteLine($"{name}: max ΔM/M_max = {100 * res.Max(c => c.DmRel):F3} %; {Describe(name, res.MaxBy(c => c.DmRel)!)}");
        return res;
    }

    static string Describe(string name, Compared c) =>
        $"{name}: N={c.P.N:F1} M=({c.P.Mx:F2};{c.P.My:F2}) Кисп сетки {c.U.Ratio:F4}, решателя {c.P.Ratio:F4}, " +
        $"ΔM/M_max {100 * c.DmRel:F3} %, λ эталона {c.P.Lambda:F4}, {(c.P.SingleSign ? "однозначная" : "двузначная")}";

    [Theory]
    [MemberData(nameof(Sections))]
    public void RuleOff_MatchesSolver(string name)
    {
        var section = Make(name);
        var mesh = new StrainDomainMesher(section, new CrackStrainCriterion(),
            new LimitSurfaceOptions { ApplySingleSignRule = false }).Build();
        output.WriteLine($"{name}: сетка {mesh.MeridianCount} меридианов, {mesh.Vertices.Count} вершин");
        var cmp = Compare(name, mesh, Reference(section, mesh, 60, 21));
        Assert.True(cmp.Count >= 15, $"мало валидных эталонных точек: {cmp.Count}");
        foreach (var c in cmp)
            Assert.True(c.DmRel <= 0.006, Describe(name, c));
    }

    /// <summary>
    /// С правилами (8.53)/(8.54): в двузначной зоне совпадение; где по норме предел ниже, чем у решателя
    /// (растяжение одного знака — (8.54), или на луче раньше трещины достигнуто сжатие по (8.53)), сетка не выше.
    /// </summary>
    [Theory]
    [MemberData(nameof(Sections))]
    public void RuleOn_TwoSignMatches_SingleSignNotAboveSolver(string name)
    {
        var section = Make(name);
        var mesh = new StrainDomainMesher(section, new CrackStrainCriterion()).Build();
        int single = 0, compression = 0;
        foreach (var c in Compare(name, mesh, Reference(section, mesh, 60, 22)))
        {
            if (c.P.SingleSign || c.U.Active?.Side == -1)
            {
                if (c.P.SingleSign) single++; else compression++;
                Assert.True(c.MeshConservative || c.DmRel <= 0.006, "предел по норме ниже, а сетка выше решателя — " + Describe(name, c));
            }
            else
                Assert.True(c.DmRel <= 0.006, "двузначная зона — " + Describe(name, c));
        }
        output.WriteLine($"{name}: точек в зоне растяжения одного знака {single}, со сжатием по (8.53) на луче {compression}");
    }

    [Theory]
    [MemberData(nameof(Sections))]
    public void FinerTolerance_Converges(string name)
    {
        var section = Make(name);
        var mesh = new StrainDomainMesher(section, new CrackStrainCriterion(),
            new LimitSurfaceOptions { ApplySingleSignRule = false, RefineTol = 0.0005, MaxMeridians = 2000 }).Build();
        output.WriteLine($"tol 0.0005: {mesh.MeridianCount} меридианов, {mesh.Vertices.Count} вершин");
        var cmp = Compare(name, mesh, Reference(section, mesh, 60, 21));
        Assert.True(cmp.Max(c => c.DmRel) <= 0.003);
    }

    /// <summary>
    /// Пределы — для полной деформации бетона: при умеренном обжатии ненагруженное сечение внутри тела, при сильном
    /// обжатие само растрескивает верхнюю грань — начало координат вне тела, точки «неоднозначны».
    /// </summary>
    [Theory]
    [InlineData(300.0, true)]
    [InlineData(900.0, false)]
    [InlineData(1200.0, false)]
    public void Prestressed_OriginInsideUnlessPreCracked(double sigSp, bool inside)
    {
        var section = TestSections.RectWithEccentricPrestressedRebar(sigSp);
        var mesh = new StrainDomainMesher(section, new CrackStrainCriterion()).Build();
        var u = mesh.Utilization(0, 1, 0, UtilizationMode.Moment);
        output.WriteLine($"σsp = {sigSp}: начало внутри {u.RayOriginInside}, Кисп {u.Ratio:G4}");
        Assert.Equal(inside, u.RayOriginInside);
        // Сама плоскость при нулевых усилиях: верхняя грань за пределом растяжения ⇔ вне тела.
        var free = section.Guess(new Load());
        var solved = new StrainSolver(section, CalcType.N, ten: true, ca: true, tol: 1e-3, maxIter: 200, h: 1e-7).Solve(0, 0, 0, free);
        double lam = new CrackStrainCriterion().BuildDomain(section).Boundary(solved.e0, solved.ky, solved.kz).Lambda;
        output.WriteLine($"  плоскость при F = 0: λ = {lam:F3}");
        Assert.Equal(inside, lam > 1);
    }
}
