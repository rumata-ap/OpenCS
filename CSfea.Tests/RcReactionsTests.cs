using CSfea.Core;
using CSfea.CScoreBridge.Structural;

namespace CSfea.Tests;

/// <summary>
/// Опорные реакции в полях шага (<see cref="RcReactions"/>, <see cref="SecantStepResult.Reactions"/>): консоль, балка на
/// двух опорах с пружиной и узлом-сиротой, плита на упругом основании C1, жёсткое тело на опоре; равновесие ΣR + ΣF = 0
/// по шагам секущего расчёта с трещинами, при коротационной геометрической нелинейности и по теории второго порядка.
/// </summary>
[HarnessChecks]
public class RcReactionsTests
{
    private const double E = 30e9;

    private static readonly RcBeamSection Rect = new("rect 0.3×0.5")
    {
        Elastic = new BeamSection(E, 0.15, 0.3 * 0.125 / 12, 0.5 * 0.027 / 12, 2.8e-3),
    };

    [Fact]
    public static void RunAll()
    {
        TestHarness.Section("Реакции: консоль, линейный расчёт");
        RunCantilever();
        TestHarness.Section("Реакции: балка на двух опорах, пружина, автозакрепление");
        RunBeamWithSpring();
        TestHarness.Section("Реакции: плита на основании C1");
        RunPlateOnFoundation();
        TestHarness.Section("Реакции: жёсткое тело на опоре");
        RunRigidBodySupport();
        TestHarness.Section("Реакции: секущий расчёт с трещинами, ΣR + ΣF по шагам");
        RunCrackedStrip();
        TestHarness.Section("Реакции: геометрическая нелинейность (CR и второй порядок)");
        RunGeometric(secondOrder: false);
        RunGeometric(secondOrder: true);
    }

    private static double[] Node(RcSecantStepFields f, int nodeId)
    {
        int i = Array.IndexOf(f.ReactionNodeIds, nodeId);
        return i < 0 ? [] : f.Reactions.AsSpan(6 * i, 6).ToArray();
    }

    private static byte Kind(RcSecantStepFields f, int nodeId)
    {
        int i = Array.IndexOf(f.ReactionNodeIds, nodeId);
        return i < 0 ? (byte)0 : f.ReactionKinds[i];
    }

    // ΣR по всем узлам реакций и по сводке типов (компонента c).
    private static double SumNodes(RcSecantStepFields f, int c)
        => Enumerable.Range(0, f.ReactionNodeIds.Length).Sum(i => f.Reactions[6 * i + c]);

    private static double SumKinds(RcSecantStepFields f, int c)
        => Enumerable.Range(0, RcReactionSet.KindCount).Sum(k => f.ReactionSums[3 * k + c]);

    private static RcStructuralModel CantileverModel(double l, int n, double[] tip, int steps = 1)
    {
        var m = new RcStructuralModel();
        for (int i = 0; i <= n; i++) m.Nodes.Add(new RcNode(i, l * i / n, 0, 0));
        for (int i = 0; i < n; i++) m.Beams.Add(new RcBeam(10 + i, i, i + 1, Rect, [0.0, 1.0, 0.0]));
        m.Supports.Add(new RcSupport(0, 0b111111));
        var lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(n, tip));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("P", [(1, 1.0)], steps));
        return m;
    }

    /// <summary>
    /// Консоль 3 м, на конце Pz = −10 кН и пролётная q = 2 кН/м по z: в заделке Rz = P + qL, Rmy = −(P·L + qL²/2)
    /// (момент опоры на схему), Rx = Ry = 0; тип — связь; ΣR + ΣF = 0, суммы по узлам и по типам совпадают.
    /// </summary>
    private static void RunCantilever()
    {
        const double l = 3.0, p = 10e3, q = 2e3;
        var m = CantileverModel(l, 4, [0, 0, -p, 0, 0, 0]);
        foreach (var b in m.Beams) m.LoadCases[0].Beams.Add(new RcBeamLoad(b.Id, [0, 0, -q]));
        var f = RcLinearAnalysis.Run(m).Cases[0].Fields;
        var r = Node(f, 0);
        TestHarness.Check("консоль: реакция только в заделке", f.ReactionNodeIds.SequenceEqual([0]) && r.Length == 6);
        if (r.Length != 6) return;
        TestHarness.CheckRel("консоль: Rz = P + qL", r[2], p + q * l, 1e-9);
        TestHarness.CheckRel("консоль: Rmy = −(P·L + qL²/2)", r[4], -(p * l + q * l * l / 2), 1e-9);
        TestHarness.Check("консоль: Rx, Ry, Rmx, Rmz ≈ 0", new[] { r[0], r[1], r[3], r[5] }.All(v => Math.Abs(v) < 1e-6),
            string.Join(" ", r.Select(v => v.ToString("e2"))));
        TestHarness.Check("консоль: тип — связь", Kind(f, 0) == RcReactionSet.Support);
        TestHarness.CheckRel("консоль: ΣFz нагрузки", f.LoadSum[2], -(p + q * l), 1e-12);
        TestHarness.Check("консоль: ΣRz по узлам = по типам = −ΣFz",
            Math.Abs(SumNodes(f, 2) + f.LoadSum[2]) < 1e-6 && Math.Abs(SumKinds(f, 2) + f.LoadSum[2]) < 1e-6);
    }

    /// <summary>
    /// Балка 6 м на шарнирных опорах, в середине пружина kz; сила P в середине. Узел-сирота (без КЭ) с силой —
    /// автозакрепление с заметной реакцией. Реакция пружины = −kz·uz; ΣRz пружины + опор = P; суммы по типам.
    /// </summary>
    private static void RunBeamWithSpring()
    {
        const double l = 6.0, p = 50e3, kz = 5e6;
        var m = new RcStructuralModel();
        for (int i = 0; i <= 4; i++) m.Nodes.Add(new RcNode(i, l * i / 4, 0, 0));
        m.Nodes.Add(new RcNode(99, 10, 10, 0));
        for (int i = 0; i < 4; i++) m.Beams.Add(new RcBeam(10 + i, i, i + 1, Rect, [0.0, 1.0, 0.0]));
        m.Supports.Add(new RcSupport(0, 0b001111));
        m.Supports.Add(new RcSupport(4, 0b000110));
        m.Springs.Add(new RcSpring(2, 2, kz));
        var lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(2, [0, 0, -p, 0, 0, 0]));
        lc.Nodal.Add(new RcNodalLoad(99, [0, 0, -1e3, 0, 0, 0]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("P", [(1, 1.0)]));
        var run = RcLinearAnalysis.Run(m);
        var f = run.Cases[0].Fields;
        double uz = run.Cases[0].U[run.Build.Dof(2, 2)];
        var rs = Node(f, 2);
        TestHarness.Check("пружина: узел 2 в реакциях, тип — пружина", rs.Length == 6 && Kind(f, 2) == RcReactionSet.Spring);
        if (rs.Length == 6) TestHarness.CheckRel("пружина: Rz = −kz·uz", rs[2], -kz * uz, 1e-12);
        TestHarness.Check("пружина: упругая доля 0 < Rz < P", rs.Length == 6 && rs[2] > 0 && rs[2] < p, rs.Length == 6 ? $"{rs[2]:0}" : "");
        TestHarness.CheckRel("пружина: ΣRz пружин по типам = Rz узла", f.ReactionSums[3 + 2], rs.Length == 6 ? rs[2] : double.NaN, 1e-12);
        double rz0 = Node(f, 0) is { Length: 6 } r0 ? r0[2] : double.NaN, rz4 = Node(f, 4) is { Length: 6 } r4 ? r4[2] : double.NaN;
        TestHarness.CheckRel("опоры: Rz0 = Rz4 (симметрия)", rz0, rz4, 1e-9);
        TestHarness.CheckRel("опоры + пружина: ΣRz = P", rz0 + rz4 + (rs.Length == 6 ? rs[2] : 0), p, 1e-9);
        TestHarness.Check("сирота: тип — автозакрепление, Rz = 1 кН",
            Kind(f, 99) == RcReactionSet.Auto && Node(f, 99) is { Length: 6 } ro && Math.Abs(ro[2] - 1e3) < 1e-6);
        TestHarness.CheckRel("сирота: ΣRz автозакреплений = 1 кН", f.ReactionSums[9 + 2], 1e3, 1e-9);
        TestHarness.Check("ΣR + ΣF = 0 по всем осям", Enumerable.Range(0, 3).All(c => Math.Abs(SumKinds(f, c) + f.LoadSum[c]) < 1e-6
            && Math.Abs(SumNodes(f, c) + f.LoadSum[c]) < 1e-6));
    }

    /// <summary>
    /// Плита 2 × 2 м (4 КЭ) на основании C1 = 1e7 Н/м³, давление q: вся вертикаль — в основание (ΣRz C1 = q·A), в плане
    /// закреплены от сдвига и поворота вокруг z два угла; тип узлов — C1, у углов — C1 и связь.
    /// </summary>
    private static void RunPlateOnFoundation()
    {
        const double q = 20e3, c1 = 1e7;
        var lam = new Laminate([new Ply(new OrthotropicMaterial(E, E, 0.2, E / 2.4), 0.0, 0.2)]);
        var sec = new RcShellSection("h200") { Elastic = lam };
        var m = new RcStructuralModel();
        int Id(int i, int j) => i * 3 + j;
        for (int i = 0; i <= 2; i++)
            for (int j = 0; j <= 2; j++) m.Nodes.Add(new RcNode(Id(i, j), i, j, 0));
        int e = 0;
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
                m.Shells.Add(new RcShell(e++, [Id(i, j), Id(i + 1, j), Id(i + 1, j + 1), Id(i, j + 1)], sec, FoundationC1: c1));
        m.Supports.Add(new RcSupport(Id(0, 0), 0b100011));
        m.Supports.Add(new RcSupport(Id(2, 0), 0b000010));
        var lc = new RcLoadCase(1, "q");
        foreach (var sh in m.Shells) lc.Shells.Add(new RcShellLoad(sh.Id, q, [0, 0, -1]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("q", [(1, 1.0)]));
        var f = RcLinearAnalysis.Run(m).Cases[0].Fields;
        TestHarness.CheckRel("C1: ΣRz основания = q·A", f.ReactionSums[6 + 2], q * 4, 1e-9);
        TestHarness.Check("C1: все 9 узлов в реакциях", f.ReactionNodeIds.Length == 9);
        TestHarness.Check("C1: угол — C1 и связь, середина — C1",
            Kind(f, Id(0, 0)) == (RcReactionSet.Support | RcReactionSet.Foundation) && Kind(f, Id(1, 1)) == RcReactionSet.Foundation);
        TestHarness.Check("C1: ΣRz по узлам = q·A", Math.Abs(SumNodes(f, 2) - q * 4) < 1e-6 * q);
    }

    /// <summary>
    /// Консоль, заделанная через жёсткое тело: ведущий узел без КЭ на 0,5 м левее начала консоли закреплён полностью,
    /// ведомый — узел начала консоли. Реакция — в ведущем узле: Rz = P, Rmy = −P·(L + 0,5).
    /// </summary>
    private static void RunRigidBodySupport()
    {
        const double l = 3.0, p = 10e3;
        var m = CantileverModel(l, 4, [0, 0, -p, 0, 0, 0]);
        m.Supports.Clear();
        m.Nodes.Add(new RcNode(50, -0.5, 0, 0));
        m.RigidBodies.Add(new RcRigidBody(1, 50, [0]));
        m.Supports.Add(new RcSupport(50, 0b111111));
        var f = RcLinearAnalysis.Run(m).Cases[0].Fields;
        var r = Node(f, 50);
        TestHarness.Check("жёсткое тело: реакция только в ведущем узле", f.ReactionNodeIds.SequenceEqual([50]) && r.Length == 6);
        if (r.Length != 6) return;
        TestHarness.CheckRel("жёсткое тело: Rz = P", r[2], p, 1e-9);
        TestHarness.CheckRel("жёсткое тело: Rmy = −P·(L + 0,5)", r[4], -p * (l + 0.5), 1e-9);
    }

    /// <summary>
    /// ЖБ-полоса с трещинами (4 шага): реакции на каждом шаге, ΣRz + ΣFz — в пределах истинной невязки шага (относительно
    /// ΣFz); на опорах Rz пропорционально нагрузке шага (статически определимая схема — по половине).
    /// </summary>
    private static void RunCrackedStrip()
    {
        double q = 8 * 30.0 / 36.0;
        var m = SecantPicardTests.StripModel(q, 0, out _, steps: 4);
        var fields = new List<RcSecantStepFields>();
        var steps = new List<SecantStepResult>();
        var run = RcSecantAnalysis.Run(m, new RcSecantOptions
        {
            OnStep = (s, get) => { steps.Add(s); fields.Add(get()); },
        });
        TestHarness.Check("полоса: расчёт сошёлся, 4 шага", run.Result.Completed && fields.Count == 4, $"полей {fields.Count}");
        for (int k = 0; k < fields.Count; k++)
        {
            var f = fields[k];
            double fz = f.LoadSum[2], unbalance = Math.Abs(SumKinds(f, 2) + fz) / Math.Abs(fz);
            TestHarness.CheckRel($"шаг {k + 1}: ΣFz = λ·q·A", fz, -(k + 1) / 4.0 * q * 1e3 * 6.0, 1e-9);
            TestHarness.Check($"шаг {k + 1}: |ΣRz + ΣFz|/|ΣFz| ≤ max(невязка, 1e-6)",
                unbalance <= Math.Max(steps[k].TrueResidual, 1e-6), $"{unbalance:e2} при невязке {steps[k].TrueResidual:e2}");
            TestHarness.Check($"шаг {k + 1}: ΣRz по узлам = по типам", Math.Abs(SumNodes(f, 2) - SumKinds(f, 2)) < 1e-9 * Math.Abs(fz));
            TestHarness.Check($"шаг {k + 1}: автозакреплений нет, ΣR авто = 0", f.ReactionSums.Skip(9).All(v => v == 0));
        }
        if (fields.Count == 4)
        {
            double left = Enumerable.Range(0, 3).Sum(j => Node(fields[3], j)[2]);
            TestHarness.CheckRel("полоса: левая опора несёт половину", left, -fields[3].LoadSum[2] / 2, 1e-3);
        }
        var sums = run.StepReactions(run.Result.Steps[^1]);
        TestHarness.Check("полоса: StepReactions = поля последнего шага",
            sums is { } s && fields.Count == 4 && s.Reactions.Sums.SequenceEqual(fields[3].ReactionSums)
            && s.LoadSum.SequenceEqual(fields[3].LoadSum));
    }

    /// <summary>
    /// Консоль 3 м, упругая, геометрическая нелинейность, 4 шага. CR: вертикальная сила на конце (большой прогиб,
    /// ~10 % длины) — Rz = P, Rx = 0, момент заделки по деформированному плечу Rmy = −P·(L + ux). Второй порядок: сжатие
    /// 0,3·Pcr и поперечная сила — ΣR + ΣF = 0, Rmy = −(H·L + N·w) по прогибу конца.
    /// </summary>
    private static void RunGeometric(bool secondOrder)
    {
        const double l = 3.0;
        double ei = E * Rect.Elastic!.Iy;
        double pcr = Math.PI * Math.PI * ei / (4 * l * l);
        double[] tip = secondOrder ? [-0.3 * pcr, 0, -0.01 * pcr, 0, 0, 0] : [0, 0, -0.1 * 3 * ei / (l * l * l) * l, 0, 0, 0];
        var m = CantileverModel(l, 16, tip, steps: 4);
        var fields = new List<RcSecantStepFields>();
        var steps = new List<SecantStepResult>();
        var run = RcSecantAnalysis.Run(m, new RcSecantOptions
        {
            PhysicallyLinear = true,
            Solver = new SecantPicardOptions { Geometric = true, GeometricSecondOrder = secondOrder },
            OnStep = (s, get) => { steps.Add(s); fields.Add(get()); },
        });
        string tag = secondOrder ? "второй порядок" : "CR";
        TestHarness.Check($"{tag}: расчёт сошёлся, 4 шага", run.Result.Completed && fields.Count == 4, $"полей {fields.Count}");
        if (fields.Count != 4) return;
        var f = fields[3];
        var u = steps[3].U;
        var r = Node(f, 0);
        double ux = u[run.Build.Dof(16, 0)], uz = u[run.Build.Dof(16, 2)];
        double scale = Math.Sqrt(tip[0] * tip[0] + tip[2] * tip[2]);
        for (int c = 0; c < 3; c++)
            TestHarness.Check($"{tag}: ΣR + ΣF = 0 по оси {c}", Math.Abs(SumKinds(f, c) + f.LoadSum[c]) < 1e-6 * scale,
                $"{SumKinds(f, c) + f.LoadSum[c]:e2}");
        // Момент нагрузки на конце относительно заделки по деформированной геометрии: (L + ux, 0, uz) × (Px, 0, Pz), по y.
        double my = uz * tip[0] - (l + ux) * tip[2];
        TestHarness.CheckRel($"{tag}: Rmy = −момент нагрузки по деформированному плечу", r[4], -my, secondOrder ? 2e-3 : 1e-5);
        TestHarness.Check($"{tag}: прогиб заметный (|uz|/L > 1 %)", Math.Abs(uz) / l > 0.01, $"{uz / l:0.###}");
    }
}
