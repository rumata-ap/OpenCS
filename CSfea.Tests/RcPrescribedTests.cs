using CSfea.CScoreBridge.Structural;
using CSfea.Core;

namespace CSfea.Tests;

/// <summary>Заданные перемещения загружений <see cref="RcPrescribed"/>: сборка, линейный и секущий расчёт модели.</summary>
[HarnessChecks]
public class RcPrescribedTests
{
    private const double E = 30e9;
    private const double I = 2.13e-3;

    [Fact]
    public static void RunAll()
    {
        RunBuildDofsAndCombination();
        RunBuildErrors();
        RunLinearSuperposition();
        RunSecantStages();
        RunBucklingWarning();
    }

    /// <summary>
    /// Заделка/заделка L = 6, загружение A — сила в узле 11, B — осадка δ узла 13. Модель для задачи 4: стадии «A», «B»,
    /// «A + 0,5·B».
    /// </summary>
    private static RcStructuralModel SettlementModel(double delta, double fz)
    {
        var m = Line(3, 6.0);
        m.Supports.Add(new RcSupport(10, 0x3F));
        m.Supports.Add(new RcSupport(13, 0x3F));
        var a = new RcLoadCase(1, "A");
        a.Nodal.Add(new RcNodalLoad(11, new[] { 0.0, 0.0, fz, 0, 0, 0 }));
        var b = new RcLoadCase(2, "B");
        b.Prescribed.Add(new RcPrescribed(13, 2, delta));
        m.LoadCases.AddRange(new[] { a, b });
        return m;
    }

    private static double[] NodeReaction(RcSecantStepFields f, int nodeId)
    {
        int k = Array.IndexOf(f.ReactionNodeIds, nodeId);
        return k < 0 ? new double[6] : f.Reactions.Skip(6 * k).Take(6).ToArray();
    }

    /// <summary>
    /// Линейный расчёт: чистая осадка — R_z = ±12EIδ/L³, M_y = 6EIδ/L² в обеих заделках (см.
    /// <see cref="PrescribedDisplacementTests"/>), ΣR_z = 0; стадия «A + 0,5·B» = A + 0,5·B по перемещениям и реакциям;
    /// ΣR_z + F_z = 0 у стадии с силой.
    /// </summary>
    private static void RunLinearSuperposition()
    {
        TestHarness.Section("RcPrescribed: линейный расчёт — осадка и суперпозиция");
        double l = 6.0, delta = -0.01, fz = -4e4;
        var m = SettlementModel(delta, fz);
        m.Stages.Add(new RcStage("A", [(1, 1.0)]));
        m.Stages.Add(new RcStage("B", [(2, 1.0)]));
        m.Stages.Add(new RcStage("A + 0,5·B", [(1, 1.0), (2, 0.5)]));
        var run = RcLinearAnalysis.Run(m);
        var (ua, ub, uab) = (run.Cases[0].U, run.Cases[1].U, run.Cases[2].U);
        var build = run.Build;

        TestHarness.CheckRel("B: uz13 = δ", ub[build.Dof(13, 2)], delta, 1e-12);
        TestHarness.CheckRel("A + 0,5·B: uz13 = δ/2", uab[build.Dof(13, 2)], 0.5 * delta, 1e-12);
        double d = 0, scale = uab.Max(Math.Abs);
        for (int i = 0; i < uab.Length; i++) d = Math.Max(d, Math.Abs(uab[i] - ua[i] - 0.5 * ub[i]));
        TestHarness.Check("A + 0,5·B: u = u_A + 0,5·u_B", d / scale < 1e-10, $"max|Δu|/max|u|={d / scale:e2}");

        var rb13 = NodeReaction(run.Cases[1].Fields, 13);
        var rb10 = NodeReaction(run.Cases[1].Fields, 10);
        TestHarness.CheckRel("B: R_z13 = 12EIδ/L³", rb13[2], 12 * E * I * delta / (l * l * l), 1e-9);
        TestHarness.CheckRel("B: R_z10 = −12EIδ/L³", rb10[2], -12 * E * I * delta / (l * l * l), 1e-9);
        TestHarness.CheckRel("B: M_y10 = 6EIδ/L²", rb10[4], 6 * E * I * delta / (l * l), 1e-9);
        TestHarness.CheckRel("B: M_y13 = 6EIδ/L²", rb13[4], 6 * E * I * delta / (l * l), 1e-9);
        var sumsB = run.Cases[1].Fields.ReactionSums;
        TestHarness.Check("B: ΣR_z опор = 0", Math.Abs(sumsB[2]) < 1e-6 * Math.Abs(rb13[2]), $"ΣR_z = {sumsB[2]:e3}");
        var sumsAb = run.Cases[2].Fields.ReactionSums;
        TestHarness.CheckRel("A + 0,5·B: ΣR_z опор = −F_z", sumsAb[2], -fz, 1e-9);
        foreach (int node in new[] { 10, 13 })
        {
            var ra = NodeReaction(run.Cases[0].Fields, node);
            var rb = NodeReaction(run.Cases[1].Fields, node);
            var rab = NodeReaction(run.Cases[2].Fields, node);
            double dr = Enumerable.Range(0, 6).Max(c => Math.Abs(rab[c] - ra[c] - 0.5 * rb[c]));
            double rs = Math.Max(rab.Max(Math.Abs), 1.0);
            TestHarness.Check($"A + 0,5·B: реакции узла {node} = R_A + 0,5·R_B", dr / rs < 1e-9, $"max|ΔR|/max|R|={dr / rs:e2}");
        }
    }

    /// <summary>
    /// Секущий расчёт физически линейной модели: стадия 1 — A, стадия 2 добавляет B за 3 шага; конец стадии 2 = линейному
    /// «A + B» (накопление заданных перемещений по стадиям, как нагрузки), конец стадии 1 — без осадки.
    /// </summary>
    private static void RunSecantStages()
    {
        TestHarness.Section("RcPrescribed: секущий расчёт по стадиям");
        double delta = -0.01, fz = -4e4;
        var lin = SettlementModel(delta, fz);
        lin.Stages.Add(new RcStage("A", [(1, 1.0)]));
        lin.Stages.Add(new RcStage("A + B", [(1, 1.0), (2, 1.0)]));
        var linRun = RcLinearAnalysis.Run(lin);

        var sec = SettlementModel(delta, fz);
        sec.Stages.Add(new RcStage("A", [(1, 1.0)], 2));
        sec.Stages.Add(new RcStage("+B", [(2, 1.0)], 3));
        var run = RcSecantAnalysis.Run(sec, new RcSecantOptions { PhysicallyLinear = true });
        TestHarness.Check("секущий расчёт завершён", run.Result.Completed, run.Result.Message ?? "");
        var end1 = run.Result.StageEnd(0);
        var end2 = run.Result.StageEnd(1);
        if (end1 == null || end2 == null) return;
        double Diff(double[] a, double[] b) => a.Zip(b, (x, y) => Math.Abs(x - y)).Max() / Math.Max(a.Max(Math.Abs), 1e-300);
        TestHarness.Check("конец стадии 1 = линейному A", Diff(linRun.Cases[0].U, end1.U) < 1e-9,
            $"max|Δu|/max|u|={Diff(linRun.Cases[0].U, end1.U):e2}");
        TestHarness.Check("конец стадии 2 = линейному A + B", Diff(linRun.Cases[1].U, end2.U) < 1e-9,
            $"max|Δu|/max|u|={Diff(linRun.Cases[1].U, end2.U):e2}");
        var r = RcReactions.Compute(run.Build, end2.U, end2.Reactions);
        var rLin = NodeReaction(linRun.Cases[1].Fields, 13);
        int k = Array.IndexOf(r.NodeIds, 13);
        TestHarness.Check("реакции узла 13 есть", k >= 0);
        if (k >= 0) TestHarness.CheckRel("R_z13 конца стадии 2 = линейному", r.Values[6 * k + 2], rLin[2], 1e-8);
    }

    /// <summary>Устойчивость по сочетанию с заданными перемещениями: предупреждение, что они в нагрузку не входят.</summary>
    private static void RunBucklingWarning()
    {
        TestHarness.Section("RcPrescribed: устойчивость — предупреждение");
        // Сжатие балки силой в узле 13 (опора без ux), иначе K_σ = 0 и задачи устойчивости нет.
        var m = SettlementModel(-0.01, -4e4);
        m.Supports.RemoveAll(sp => sp.NodeId == 13);
        m.Supports.Add(new RcSupport(13, 0x3E));
        m.LoadCases[0].Nodal.Add(new RcNodalLoad(13, new[] { -1e5, 0.0, 0.0, 0, 0, 0 }));
        var model = RcBucklingAnalysis.Prepare(m, [new[] { 100, 101, 102 }]);
        var withSettle = RcBucklingAnalysis.Solve(model, [(1, 1.0), (2, 1.0)], new RcBucklingOptions(Modes: 1));
        var without = RcBucklingAnalysis.Solve(model, [(1, 1.0)], new RcBucklingOptions(Modes: 1));
        TestHarness.Check("с осадкой — предупреждение", withSettle.Warnings.Any(w => w.Contains("заданные перемещения", StringComparison.OrdinalIgnoreCase)),
            string.Join(" | ", withSettle.Warnings));
        TestHarness.Check("без осадки — предупреждений нет", without.Warnings.Count == 0, string.Join(" | ", without.Warnings));
        var env = RcBucklingAnalysis.SolveEnvelope(model, [[(1, 1.0)], [(1, 1.0), (2, 1.0)]], 0, new RcBucklingOptions(Modes: 1));
        TestHarness.Check("огибающая с осадкой в одном из сочетаний — предупреждение", env.Warnings.Count == 1, string.Join(" | ", env.Warnings));
    }

    private static readonly RcBeamSection Sq = new("sq 0.4") { Elastic = new BeamSection(E, 0.16, I, I, 3.6e-3) };

    /// <summary>Балка вдоль X: узлы 10…10+n, стержни 100…; refVec = Z.</summary>
    private static RcStructuralModel Line(int n, double l)
    {
        var m = new RcStructuralModel();
        for (int i = 0; i <= n; i++) m.Nodes.Add(new RcNode(10 + i, l * i / n, 0.0, 0.0));
        for (int i = 0; i < n; i++) m.Beams.Add(new RcBeam(100 + i, 10 + i, 11 + i, Sq, new[] { 0.0, 0.0, 1.0 }));
        return m;
    }

    private static bool Throws<T>(Action a, string? contains = null) where T : Exception
    {
        try { a(); return false; }
        catch (T ex) { return contains == null || ex.Message.Contains(contains); }
    }

    /// <summary>
    /// Два загружения задают разные DOF одного свободного узла: оба DOF входят в закрепления сборки, у загружения без
    /// значения на DOF — 0; сочетание — сумма с коэффициентами; загружение без заданных перемещений — null.
    /// </summary>
    private static void RunBuildDofsAndCombination()
    {
        TestHarness.Section("RcPrescribed: закрепления сборки и сочетания");
        var m = Line(3, 6.0);
        m.Supports.Add(new RcSupport(10, 0x3F));
        m.Supports.Add(new RcSupport(13, 0x3F));
        var lc1 = new RcLoadCase(1, "осадка");
        lc1.Prescribed.Add(new RcPrescribed(12, 2, -0.01));
        var lc2 = new RcLoadCase(2, "сдвиг");
        lc2.Prescribed.Add(new RcPrescribed(12, 0, 0.003));
        lc2.Prescribed.Add(new RcPrescribed(13, 4, 0.002));   // поворот заделки — DOF уже закреплён опорой
        var lc3 = new RcLoadCase(3, "сила");
        lc3.Nodal.Add(new RcNodalLoad(11, new[] { 0.0, 0.0, -1e4, 0, 0, 0 }));
        m.LoadCases.AddRange(new[] { lc1, lc2, lc3 });
        var build = RcStructuralMeshBuilder.Build(m, new LinearRcSectionFactory());

        var fixedDofs = build.Bc.FixedDofs;
        int uz12 = build.Dof(12, 2), ux12 = build.Dof(12, 0), ry13 = build.Dof(13, 4);
        TestHarness.Check("uz и ux узла 12 закреплены", fixedDofs.Contains(uz12) && fixedDofs.Contains(ux12));
        TestHarness.Check("прочие DOF узла 12 свободны", !fixedDofs.Contains(build.Dof(12, 1)) && !fixedDofs.Contains(build.Dof(12, 4)));
        TestHarness.Check("PrescribedDofs = {uz12, ux12, θy13}",
            build.PrescribedDofs.SetEquals(new[] { uz12, ux12, ry13 }), string.Join(",", build.PrescribedDofs));
        TestHarness.Check("реакции заданных DOF — как опора (SupportDofs)",
            build.SupportDofs.Contains(uz12) && build.SupportDofs.Contains(ux12));

        double At(double[] v, int dof) => v[Array.IndexOf(fixedDofs, dof)];
        var p1 = build.PrescribedCombination([(1, 1.0)]);
        TestHarness.Check("загружение 1: значения есть", p1 != null && p1.Length == fixedDofs.Length);
        if (p1 != null)
        {
            TestHarness.CheckRel("загружение 1: uz12 = −0,01", At(p1, uz12), -0.01, 1e-15);
            TestHarness.Check("загружение 1: ux12 = 0, θy13 = 0", At(p1, ux12) == 0 && At(p1, ry13) == 0);
        }
        var p12 = build.PrescribedCombination([(1, 1.5), (2, 0.5), (3, 2.0)]);
        if (p12 != null)
        {
            TestHarness.CheckRel("1,5·(1) + 0,5·(2): uz12", At(p12, uz12), -0.015, 1e-15);
            TestHarness.CheckRel("1,5·(1) + 0,5·(2): ux12", At(p12, ux12), 0.0015, 1e-15);
            TestHarness.CheckRel("1,5·(1) + 0,5·(2): θy13", At(p12, ry13), 0.001, 1e-15);
        }
        else TestHarness.Check("сочетание 1 + 2: значения есть", false);
        TestHarness.Check("загружение без заданных перемещений → null", build.PrescribedCombination([(3, 1.0)]) == null);
        TestHarness.Check("нулевой коэффициент → null", build.PrescribedCombination([(1, 0.0)]) == null);
    }

    /// <summary>Ошибки сборки: узел без КЭ, ведомый DOF жёсткого тела, DOF вне 0…5, повтор; пружина — предупреждение.</summary>
    private static void RunBuildErrors()
    {
        TestHarness.Section("RcPrescribed: ошибки и предупреждения сборки");
        RcStructuralModel Model(Action<RcStructuralModel, RcLoadCase> setup)
        {
            var m = Line(2, 4.0);
            m.Supports.Add(new RcSupport(10, 0x3F));
            var lc = new RcLoadCase(1, "u");
            m.LoadCases.Add(lc);
            setup(m, lc);
            return m;
        }
        RcStructuralMeshBuild Build(RcStructuralModel m) => RcStructuralMeshBuilder.Build(m, new LinearRcSectionFactory());

        TestHarness.Check("узел без КЭ → ArgumentException с номером узла", Throws<ArgumentException>(() => Build(Model((m, lc) =>
        {
            m.Nodes.Add(new RcNode(20, 9.0, 0.0, 0.0));
            lc.Prescribed.Add(new RcPrescribed(20, 2, 0.01));
        })), "20"));
        TestHarness.Check("ведомый DOF жёсткого тела → ArgumentException с номером узла", Throws<ArgumentException>(() => Build(Model((m, lc) =>
        {
            m.Nodes.Add(new RcNode(21, 4.5, 0.0, 0.0));
            m.RigidBodies.Add(new RcRigidBody(1, 12, new[] { 21 }));
            lc.Prescribed.Add(new RcPrescribed(21, 2, 0.01));
        })), "21"));
        TestHarness.Check("DOF 6 → ArgumentException", Throws<ArgumentException>(() => Build(Model((_, lc) =>
            lc.Prescribed.Add(new RcPrescribed(12, 6, 0.01))))));
        TestHarness.Check("повтор DOF в загружении → ArgumentException", Throws<ArgumentException>(() => Build(Model((_, lc) =>
        {
            lc.Prescribed.Add(new RcPrescribed(12, 2, 0.01));
            lc.Prescribed.Add(new RcPrescribed(12, 2, 0.02));
        }))));
        var withSpring = Build(Model((m, lc) =>
        {
            m.Springs.Add(new RcSpring(12, 2, 1e7));
            lc.Prescribed.Add(new RcPrescribed(12, 2, -0.01));
        }));
        TestHarness.Check("пружина на заданном DOF — предупреждение в отчёте",
            withSpring.Report.Any(r => r.Contains("12") && r.Contains("пружин")), string.Join(" | ", withSpring.Report));
    }
}
