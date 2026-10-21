using CSfea.CScoreBridge.Structural;
using CSfea.Core;

namespace CSfea.Tests;

/// <summary>
/// Линейный расчёт на замороженном состоянии секущего прогона второго порядка (<see cref="RcLinearOptions.GeometricFields"/>,
/// P-Δ инженерной нелинейности): загружения решаются на K_s + K_σ(u источника) — сумма загружений воспроизводит источник,
/// прогиб от боковой силы усилен как у стойки второго порядка, реакции уравновешивают нагрузку, выше критической —
/// ошибка «потеря устойчивости»; без состояния — прежний линейный расчёт.
/// </summary>
[HarnessChecks]
public class RcLinearPDeltaTests
{
    private const double E = 30e9, L = 6.0, Side = 0.4, H = 1e3;
    private const int N = 12;
    private static readonly double Inertia = Math.Pow(Side, 4) / 12, Pcr = Math.PI * Math.PI * E * Inertia / (4 * L * L);

    [Fact]
    public static void RunAll()
    {
        TestHarness.Section("P-Δ источника: упругая консоль-стойка");
        RunElasticColumn();

        TestHarness.Section("P-Δ источника: стена в своей плоскости");
        RunWallInPlane();

        TestHarness.Section("P-Δ источника: выше критической — ошибка");
        RunUnstable();

        TestHarness.Section("P-Δ источника: без состояния — прежний линейный расчёт");
        RunRegression();
    }

    private static readonly RcBeamSection Section = new("стойка")
    {
        Cross = RcTestMaterials.Rect(Side, Side, 0.04, 8e-4, 8e-4), Calc = CScore.CalcType.N, TorsionGJ = 1e7,
        Elastic = new BeamSection(E, Side * Side, Inertia, Inertia, 2 * Inertia),
    };

    /// <summary>Консоль-стойка: загружение 1 — P вниз в вершине, 2 — H по X; стадия источника — P + H.</summary>
    private static RcStructuralModel Column(double p)
    {
        var m = new RcStructuralModel();
        for (int i = 0; i <= N; i++) m.Nodes.Add(new RcNode(i, 0, 0, L * i / N));
        for (int i = 0; i < N; i++) m.Beams.Add(new RcBeam(100 + i, i, i + 1, Section, new[] { 1.0, 0.0, 0.0 }));
        m.Supports.Add(new RcSupport(0, 0x3F));
        var lp = new RcLoadCase(1, "P");
        lp.Nodal.Add(new RcNodalLoad(N, new[] { 0.0, 0.0, -p, 0.0, 0.0, 0.0 }));
        var lh = new RcLoadCase(2, "H");
        lh.Nodal.Add(new RcNodalLoad(N, new[] { H, 0.0, 0.0, 0.0, 0.0, 0.0 }));
        m.LoadCases.Add(lp);
        m.LoadCases.Add(lh);
        m.Stages.Add(new RcStage("P + H", new[] { (1, 1.0), (2, 1.0) }, Steps: 2));
        return m;
    }

    /// <summary>Источник — секущий второго порядка без физнелина; затем стадии модели — загружения P и H по отдельности.</summary>
    private static (RcStructuralModel Model, RcSecantStepFields Source) Source(double p)
    {
        var m = Column(p);
        var src = RcSecantAnalysis.Run(m, new RcSecantOptions
        {
            PhysicallyLinear = true, Solver = new SecantPicardOptions { Geometric = true, GeometricSecondOrder = true },
        });
        var f = src.LastConvergedFields()!;
        m.Stages.Clear();
        m.Stages.Add(new RcStage("P", new[] { (1, 1.0) }));
        m.Stages.Add(new RcStage("H", new[] { (2, 1.0) }));
        return (m, f);
    }

    private static void RunElasticColumn()
    {
        const double ratio = 0.4;
        double p = ratio * Pcr;
        var (m, f) = Source(p);
        var run = RcLinearAnalysis.Run(m, new RcLinearOptions
        {
            Sections = new RecordedRcSectionFactory(f, 0.01), GeometricFields = f,
        });
        int dx = run.Build.Dof(N, 0);
        double usrc = f.Displacements[RcSecantStepFields.NodeComponents * Array.IndexOf(f.NodeIds, N)];
        double sum = run.Cases[0].U[dx] + run.Cases[1].U[dx];
        TestHarness.CheckRel("Σ uᵢ = u источника (вершина, X)", sum, usrc, 1e-3);

        double d0 = H * L * L * L / (3 * E * Inertia);
        double k = Math.PI / 2 * Math.Sqrt(ratio);
        double uh = run.Cases[1].U[dx];
        TestHarness.CheckRel("прогиб от H усилен как 3(tg u − u)/u³", uh / d0, 3 * (Math.Tan(k) - k) / (k * k * k), 0.01);
        TestHarness.Check("K_σ у всех стержней", run.GeometricBeams == N, $"{run.GeometricBeams}");

        var fh = run.Cases[1].Fields;
        TestHarness.CheckRel("ΣRx = −H", fh.ReactionSums[0], -H, 1e-9);
        int s0 = Array.IndexOf(fh.ReactionNodeIds, 0);
        double rmy = s0 >= 0 ? fh.Reactions[6 * s0 + 4] : double.NaN;
        TestHarness.CheckRel("|Rmy| заделки = H·L + P·u_H", Math.Abs(rmy), H * L + p * uh, 0.01);
        var fb = fh.BeamForces;
        TestHarness.CheckRel("момент КЭ у заделки = H·L + P·u_H (второго порядка)",
            Math.Sqrt(fb[4] * fb[4] + fb[5] * fb[5]), H * L + p * uh, 0.02);
    }

    /// <summary>
    /// Стена-консоль 1×6 м (t = 0,2, Q4 4×24) в плоскости xz, uy закреплены по высоте: загружение 1 — вертикаль по верху, 2 — горизонталь по
    /// верху; источник второго порядка с P-Δ в плоскости. Линейная часть на K_σ его состояния: сумма = источник,
    /// прогиб от горизонтали против линейного — 3(tg u − u)/u³ (допуск 2,5 % — сдвиг и сетка Q4, как в тесте 30612556).
    /// </summary>
    private static void RunWallInPlane()
    {
        const double b = 1.0, t = 0.2, ratio = 0.4, hw = 1e3;
        const int nx = 4, nz = 24;
        double pcr = Math.PI * Math.PI * E * (t * b * b * b / 12) / (4 * L * L), p = ratio * pcr;
        var sec = new RcShellSection("стена") { Elastic = new Laminate(new[] { new Ply(new OrthotropicMaterial(E, E, 0.2, E / 2.4), 0, t) }) };
        RcStructuralModel Wall()
        {
            var m = new RcStructuralModel();
            int Id(int i, int k) => k * (nx + 1) + i;
            for (int k = 0; k <= nz; k++)
                for (int i = 0; i <= nx; i++) m.Nodes.Add(new RcNode(Id(i, k), b * i / nx, 0, L * k / nz));
            for (int k = 0; k < nz; k++)
                for (int i = 0; i < nx; i++)
                    m.Shells.Add(new RcShell(k * nx + i + 1, new[] { Id(i, k), Id(i + 1, k), Id(i + 1, k + 1), Id(i, k + 1) }, sec));
            for (int i = 0; i <= nx; i++) m.Supports.Add(new RcSupport(Id(i, 0), 0x3F));
            // Из плоскости стена держится связями uy по высоте: её P_cr из плоскости в (b/t)² = 25 раз меньше.
            for (int k = 1; k <= nz; k++)
                for (int i = 0; i <= nx; i++) m.Supports.Add(new RcSupport(Id(i, k), 0b000010));
            var lv = new RcLoadCase(1, "V");
            var lh = new RcLoadCase(2, "H");
            for (int i = 0; i <= nx; i++)
            {
                double w = (i == 0 || i == nx) ? 0.5 / nx : 1.0 / nx;
                lv.Nodal.Add(new RcNodalLoad(Id(i, nz), new[] { 0.0, 0.0, -p * w, 0.0, 0.0, 0.0 }));
                lh.Nodal.Add(new RcNodalLoad(Id(i, nz), new[] { hw * w, 0.0, 0.0, 0.0, 0.0, 0.0 }));
            }
            m.LoadCases.Add(lv);
            m.LoadCases.Add(lh);
            m.Stages.Add(new RcStage("V + H", new[] { (1, 1.0), (2, 1.0) }, Steps: 2));
            return m;
        }

        var m = Wall();
        var src = RcSecantAnalysis.Run(m, new RcSecantOptions
        {
            PhysicallyLinear = true,
            Solver = new SecantPicardOptions { Geometric = true, GeometricSecondOrder = true, ShellInPlanePDelta = true },
        });
        var f = src.LastConvergedFields();
        TestHarness.Check("стена: источник сошёлся", src.Result.Completed && f != null, src.Result.Message ?? "");
        if (f == null) return;
        m.Stages.Clear();
        m.Stages.Add(new RcStage("V", new[] { (1, 1.0) }));
        m.Stages.Add(new RcStage("H", new[] { (2, 1.0) }));
        var run = RcLinearAnalysis.Run(m, new RcLinearOptions
        {
            Sections = new RecordedRcSectionFactory(f, 0.01), GeometricFields = f, ShellInPlanePDelta = true,
        });
        var lin = RcLinearAnalysis.Run(m, new RcLinearOptions { Sections = new RecordedRcSectionFactory(f, 0.01) });
        int top = nz * (nx + 1) + nx, dx = run.Build.Dof(top, 0);
        double usrc = f.Displacements[RcSecantStepFields.NodeComponents * Array.IndexOf(f.NodeIds, top)];
        TestHarness.CheckRel("стена: Σ uᵢ = u источника", run.Cases[0].U[dx] + run.Cases[1].U[dx], usrc, 1e-3);
        double k = Math.PI / 2 * Math.Sqrt(ratio);
        TestHarness.CheckRel("стена: δ/δ₀ = 3(tg u − u)/u³", run.Cases[1].U[dx] / lin.Cases[1].U[dx],
            3 * (Math.Tan(k) - k) / (k * k * k), 0.025);

        // Без P-Δ в плоскости: усиление в плоскости пропадает (K_σ только из плоскости).
        var outOnly = RcLinearAnalysis.Run(m, new RcLinearOptions
        {
            Sections = new RecordedRcSectionFactory(f, 0.01), GeometricFields = f, ShellInPlanePDelta = false,
        });
        TestHarness.CheckRel("стена: без P-Δ в плоскости — прогиб как линейный", outOnly.Cases[1].U[dx], lin.Cases[1].U[dx], 1e-6);
    }

    private static void RunUnstable()
    {
        // Источник на 0,4·Pcr, его перемещения ×3 — продольная сила ≈ 1,2·Pcr: K_s + K_σ не положительно определена.
        var (m, f) = Source(0.4 * Pcr);
        var big = Scaled(f, 3.0);
        try
        {
            RcLinearAnalysis.Run(m, new RcLinearOptions { Sections = new RecordedRcSectionFactory(f, 0.01), GeometricFields = big });
            TestHarness.Check("выше критической — исключение", false, "расчёт прошёл");
        }
        catch (InvalidOperationException ex)
        {
            TestHarness.Check("выше критической — «потеря устойчивости» с DOF",
                ex.Message.Contains("потеря устойчивости", StringComparison.Ordinal) && ex.Message.Contains("—", StringComparison.Ordinal),
                ex.Message);
        }
    }

    private static void RunRegression()
    {
        var (m, f) = Source(0.4 * Pcr);
        var sections = new RecordedRcSectionFactory(f, 0.01);
        var run = RcLinearAnalysis.Run(m, new RcLinearOptions { Sections = sections });
        var build = RcStructuralMeshBuilder.Build(m, new RecordedRcSectionFactory(f, 0.01));
        var direct = build.Mesh.SolveLinear(m.Stages.Select(st => build.Combination(st.Loads)).ToList(), build.Bc);
        bool same = run.Cases.Select((c, i) => c.U.SequenceEqual(direct[i])).All(x => x);
        TestHarness.Check("без состояния: U побитно как SolveLinear", same);
        TestHarness.Check("без состояния: K_σ нет", run.GeometricBeams == 0 && run.GeometricShells == 0);
    }

    /// <summary>Копия записи с перемещениями, умноженными на <paramref name="k"/>.</summary>
    private static RcSecantStepFields Scaled(RcSecantStepFields f, double k) => new()
    {
        Stage = f.Stage, Step = f.Step, LoadFactor = f.LoadFactor, IsRefinement = f.IsRefinement,
        NodeIds = f.NodeIds, Displacements = f.Displacements.Select(v => v * k).ToArray(),
        ShellIds = f.ShellIds, ShellForces = f.ShellForces, ShellStates = f.ShellStates, ShellFlags = f.ShellFlags,
        BeamIds = f.BeamIds, BeamForces = f.BeamForces, BeamFlags = f.BeamFlags,
    };
}
