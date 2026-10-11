using CSfea.CScoreBridge.Structural;
using CSfea.Core;

namespace CSfea.Tests;

/// <summary>
/// Линейный расчёт схемы (<see cref="RcLinearAnalysis"/>): независимые загружения одной факторизацией, поля загружений;
/// секущий расчёт без физической нелинейности (<see cref="RcSecantOptions.PhysicallyLinear"/>).
/// </summary>
[HarnessChecks]
public class RcLinearAnalysisTests
{
    private const double E = 30e9;

    [Fact]
    public static void RunAll()
    {
        RunIndependentCases();
        RunCantileverFields();
        RunPhysicallyLinearSecondOrder();
    }

    private static readonly RcBeamSection Rect = new("rect 0.3×0.5")
    {
        Elastic = new BeamSection(E, 0.15, 0.3 * 0.125 / 12, 0.5 * 0.027 / 12, 2.8e-3),
    };

    /// <summary>Пластина 3×2 м из Q4 и T3 на угловых опорах с ребром-стержнем по краю; два загружения.</summary>
    private static RcStructuralModel PlateWithRib()
    {
        var m = new RcStructuralModel();
        (int, double, double)[] pts = { (1, 0, 0), (2, 1.3, 0), (3, 3, 0), (4, 0, 1.1), (5, 1.6, 0.9), (6, 3, 1.2), (7, 0, 2), (8, 1.4, 2), (9, 3, 2) };
        foreach (var (id, x, y) in pts) m.Nodes.Add(new RcNode(id, x, y, 0));
        var sec = new RcShellSection("h200") { Elastic = new Laminate(new[] { new Ply(new OrthotropicMaterial(E, E, 0.2, E / 2.4), 0, 0.2) }) };
        m.Shells.Add(new RcShell(1, new[] { 1, 2, 5, 4 }, sec));
        m.Shells.Add(new RcShell(2, new[] { 2, 3, 6, 5 }, sec));
        m.Shells.Add(new RcShell(3, new[] { 4, 5, 8 }, sec));
        m.Shells.Add(new RcShell(4, new[] { 4, 8, 7 }, sec));
        m.Shells.Add(new RcShell(5, new[] { 5, 6, 9, 8 }, sec));
        m.Beams.Add(new RcBeam(101, 1, 2, Rect, new[] { 0.0, 0.0, 1.0 }));
        m.Beams.Add(new RcBeam(102, 2, 3, Rect, new[] { 0.0, 0.0, 1.0 }));
        foreach (int n in new[] { 1, 3, 7, 9 }) m.Supports.Add(new RcSupport(n, 0b000111));
        var q = new RcLoadCase(1, "q");
        foreach (var s in m.Shells) q.Shells.Add(new RcShellLoad(s.Id, 5e3, new[] { 0.0, 0.0, -1.0 }));
        var p = new RcLoadCase(2, "P и погонная");
        p.Nodal.Add(new RcNodalLoad(5, new[] { 2e3, 0.0, -20e3, 0.0, 0.0, 0.0 }));
        p.Beams.Add(new RcBeamLoad(101, new[] { 0.0, 0.0, -8e3 }));
        m.LoadCases.Add(q);
        m.LoadCases.Add(p);
        return m;
    }

    /// <summary>Загружения независимы: каждое равно своему отдельному решению, комбинация — суперпозиции.</summary>
    private static void RunIndependentCases()
    {
        TestHarness.Section("RcLinearAnalysis: независимые загружения одной факторизацией");
        var m = PlateWithRib();
        m.Stages.Add(new RcStage("q", new[] { (1, 1.0) }, Steps: 5));
        m.Stages.Add(new RcStage("P", new[] { (2, 1.5) }));
        m.Stages.Add(new RcStage("q + P", new[] { (1, 1.0), (2, 1.5) }));
        var run = RcLinearAnalysis.Run(m);
        TestHarness.Check("решений — по числу стадий модели", run.Cases.Count == 3);
        TestHarness.Check("одна символика Холецкого на все загружения", run.Build.Mesh.CholeskyAnalyses == 1,
            $"анализов {run.Build.Mesh.CholeskyAnalyses}");

        var single = RcStructuralMeshBuilder.Build(m, new LinearRcSectionFactory());
        for (int i = 0; i < 3; i++)
        {
            var u = single.Mesh.SolveLinear(single.Combination(m.Stages[i].Loads), single.Bc);
            TestHarness.Check($"«{m.Stages[i].Name}»: совпадает с отдельным решением", MaxRelDiff(run.Cases[i].U, u) < 1e-9,
                $"{MaxRelDiff(run.Cases[i].U, u):e2}");
            var f = run.Cases[i].Fields;
            TestHarness.Check($"«{m.Stages[i].Name}»: поля — стадия {i}, шаг целиком",
                f.Stage == i && f.LoadFactor == 1.0 && !f.IsRefinement && f.Displacements.SequenceEqual(run.Cases[i].U));
        }
        var sum = run.Cases[0].U.Zip(run.Cases[1].U, (a, b) => a + b).ToArray();
        TestHarness.Check("комбинация = сумма загружений (без накопления по стадиям)", MaxRelDiff(run.Cases[2].U, sum) < 1e-9,
            $"{MaxRelDiff(run.Cases[2].U, sum):e2}");
        // Усилия пластин линейны: поля комбинации — сумма полей.
        var fa = run.Cases[0].Fields.ShellForces;
        var fb = run.Cases[1].Fields.ShellForces;
        var fSum = fa.Zip(fb, (a, b) => a + b).ToArray();
        TestHarness.Check("усилия пластин комбинации = сумма", MaxRelDiff(run.Cases[2].Fields.ShellForces, fSum) < 1e-8,
            $"{MaxRelDiff(run.Cases[2].Fields.ShellForces, fSum):e2}");
        TestHarness.Check("состояния пластин не заполняются (упругие КЭ)", run.Cases[0].Fields.ShellStates.All(double.IsNaN)
            && run.Cases[0].Fields.ShellFlags.All(b => b == 0));
    }

    /// <summary>Консоль под равномерной нагрузкой: усилия концов КЭ — за вычетом пролётной нагрузки своего загружения.</summary>
    private static void RunCantileverFields()
    {
        TestHarness.Section("RcLinearAnalysis: усилия концов стержня с пролётной нагрузкой");
        double l = 4.0, q = 12e3;
        var m = new RcStructuralModel();
        m.Nodes.Add(new RcNode(10, 0, 0, 0));
        m.Nodes.Add(new RcNode(11, l, 0, 0));
        m.Beams.Add(new RcBeam(100, 10, 11, Rect, new[] { 0.0, 0.0, 1.0 }));
        m.Supports.Add(new RcSupport(10, 0x3F));
        var lc = new RcLoadCase(1, "q");
        lc.Beams.Add(new RcBeamLoad(100, new[] { 0.0, 0.0, -q }));
        var empty = new RcLoadCase(2, "узловая");
        empty.Nodal.Add(new RcNodalLoad(11, new[] { 0.0, 0.0, -1e3, 0.0, 0.0, 0.0 }));
        m.LoadCases.Add(lc);
        m.LoadCases.Add(empty);
        // Порядок стадий — пролётная нагрузка второй: в накопительной (секущей) трактовке она попала бы и в первую.
        m.Stages.Add(new RcStage("P", new[] { (2, 1.0) }));
        m.Stages.Add(new RcStage("q", new[] { (1, 1.0) }));
        var run = RcLinearAnalysis.Run(m);

        var fq = run.Cases[1].Fields.BeamForces;
        double shearI = Math.Sqrt(fq[1] * fq[1] + fq[2] * fq[2]), momentI = Math.Sqrt(fq[4] * fq[4] + fq[5] * fq[5]);
        TestHarness.CheckRel("q: поперечная сила в заделке = qL", shearI, q * l, 1e-9);
        TestHarness.CheckRel("q: момент в заделке = qL²/2", momentI, q * l * l / 2, 1e-9);
        TestHarness.Check("q: свободный конец без усилий", fq.Skip(6).All(v => Math.Abs(v) < 1e-6 * q * l),
            $"max {fq.Skip(6).Max(Math.Abs):e2}");
        var fp = run.Cases[0].Fields.BeamForces;
        TestHarness.CheckRel("P: момент в заделке = P·L (без пролётной нагрузки чужого загружения)",
            Math.Sqrt(fp[4] * fp[4] + fp[5] * fp[5]), 1e3 * l, 1e-9);
    }

    /// <summary>
    /// Секущий расчёт без физической нелинейности: стержень с сечением CScore берётся упругим (состояний КЭ нет);
    /// с геометрической нелинейностью второго порядка прогиб консоли-стойки от боковой силы растёт как
    /// 3(tg u − u)/u³, u = π/2·√(P/P_cr).
    /// </summary>
    private static void RunPhysicallyLinearSecondOrder()
    {
        TestHarness.Section("RcSecantAnalysis: без физнелина — упругие сечения, второй порядок");
        const double l = 6.0, side = 0.4, h = 1e3, ratio = 0.4;
        const int n = 12;
        double inertia = Math.Pow(side, 4) / 12, ei = E * inertia;
        double pcr = Math.PI * Math.PI * ei / (4 * l * l);
        var section = new RcBeamSection("стойка")
        {
            Cross = RcTestMaterials.Rect(side, side, 0.04, 8e-4, 8e-4), Calc = CScore.CalcType.N, TorsionGJ = 1e7,
            Elastic = new BeamSection(E, side * side, inertia, inertia, 2 * inertia),
        };
        RcStructuralModel Model(double p)
        {
            var m = new RcStructuralModel();
            for (int i = 0; i <= n; i++) m.Nodes.Add(new RcNode(i, 0, 0, l * i / n));
            for (int i = 0; i < n; i++) m.Beams.Add(new RcBeam(100 + i, i, i + 1, section, new[] { 1.0, 0.0, 0.0 }));
            m.Supports.Add(new RcSupport(0, 0x3F));
            var lc = new RcLoadCase(1, "P и H");
            lc.Nodal.Add(new RcNodalLoad(n, new[] { h, 0.0, -p, 0.0, 0.0, 0.0 }));
            m.LoadCases.Add(lc);
            m.Stages.Add(new RcStage("P", new[] { (1, 1.0) }, Steps: 2));
            return m;
        }

        var lin = RcSecantAnalysis.Run(Model(ratio * pcr), new RcSecantOptions { PhysicallyLinear = true });
        var linEnd = lin.Result.StageEnd(0);
        TestHarness.Check("без геомнелина: расчёт сошёлся", lin.Result.Completed && linEnd != null, lin.Result.Message ?? "");
        TestHarness.Check("состояний КЭ нет — сечения упругие", lin.BeamStates.All(s => s == null));
        if (linEnd == null) return;
        double d0 = linEnd.U[lin.Build.Dof(n, 0)];
        TestHarness.CheckRel("без геомнелина: δ₀ = H·L³/(3EI) по упругому сечению", d0, h * l * l * l / (3 * ei), 1e-6);

        var geo = RcSecantAnalysis.Run(Model(ratio * pcr), new RcSecantOptions
        {
            PhysicallyLinear = true, Solver = new SecantPicardOptions { Geometric = true, GeometricSecondOrder = true },
        });
        var geoEnd = geo.Result.StageEnd(0);
        TestHarness.Check("второй порядок: расчёт сошёлся", geo.Result.Completed && geoEnd != null, geo.Result.Message ?? "");
        if (geoEnd == null) return;
        double k = Math.PI / 2 * Math.Sqrt(ratio);
        TestHarness.CheckRel("второй порядок: δ/δ₀ = 3(tg u − u)/u³", geoEnd.U[geo.Build.Dof(n, 0)] / d0,
            3 * (Math.Tan(k) - k) / (k * k * k), 0.01);
    }

    private static double MaxRelDiff(double[] a, double[] b)
    {
        double scale = Math.Max(a.Max(Math.Abs), b.Max(Math.Abs));
        if (scale == 0) return 0;
        double d = 0;
        for (int i = 0; i < a.Length; i++) d = Math.Max(d, Math.Abs(a[i] - b[i]));
        return d / scale;
    }
}
