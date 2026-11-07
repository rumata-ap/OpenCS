using System.Text.Json;
using CScore.Fem;
using CScore.Fem.Imperfections;
using CSfea.CScoreBridge.Structural;
using CSfea.Core;

namespace CSfea.Tests;

/// <summary>
/// Загружение «Несовершенство» схемы FEM (срез 1): элементы несовершенства → привязка к сетке
/// (<see cref="FemImperfectionResolver"/>) → адаптер (<see cref="FemRcModelAdapter"/>) → секущий расчёт второго порядка.
/// Сверки: крен консоли ≡ горизонтальная сила φ·P на верхе (рис. 5.3), погибь шарнирной колонны против аналитики
/// (коэффициент загружения и знак — множитель e₀), оси погиби — местные y/z КонЭ, КЭ вне модели.
/// </summary>
[HarnessChecks]
public class FemImperfectionAdapterTests
{
    private const double E = 30e9, G = 12.5e9, A = 0.16, I = 0.4 * 0.4 * 0.4 * 0.4 / 12, J = 2 * I, L = 6.0;
    private const int N = 8;

    [Fact]
    public static void RunAll()
    {
        TestHarness.Section("Несовершенство: привязка к сетке и оси погиби");
        RunResolveAxes();

        TestHarness.Section("Несовершенство: крен консоли через адаптер ≡ сила φ·P");
        RunSwayEquivalence();

        TestHarness.Section("Несовершенство: погибь КонЭ через адаптер против аналитики");
        RunBowEquivalence();

        TestHarness.Section("Несовершенство: КЭ вне модели, нагрузки в загружении-несовершенстве");
        RunGuards();

        TestHarness.Section("Несовершенство: предварительный расчёт — знак «авто» и п. 5.3.2(6)");
        RunDecide();
    }

    private sealed class Props : IFemElementStiffnessSource
    {
        public FemShellStiffness? Shell(FemElement e) => null;
        public FemBarStiffness? Bar(FemElement e) => e.ElemType == "beam" && e.StiffnessNum != 0 ? new(E, G, A, I, I, J) : null;
        public double? UnitWeight(FemElement e) => null;
        public bool IsWithoutStiffness(FemElement e) => e.ElemType == "beam" && e.StiffnessNum == 0;
        public double? BarArea(FemElement e) => A;
    }

    /// <summary>Вертикальная колонна КонЭ «C»: узлы сетки 1…N+1 по z, КЭ 1…N; закрепление низа и верха.</summary>
    private static (List<FemMeshNode> Nodes, List<FemElement> Elements) Column()
    {
        var nodes = Enumerable.Range(0, N + 1).Select(i => new FemMeshNode { NodeTag = (i + 1).ToString(), Z = L * i / N }).ToList();
        var elements = Enumerable.Range(1, N).Select(i => new FemElement
        {
            ElemTag = i.ToString(), ElemType = "beam", NodeIdsJson = JsonSerializer.Serialize(new[] { i, i + 1 }), SourceMemberTag = "C",
        }).ToList();
        return (nodes, elements);
    }

    private static FemImperfection Sway(double phiInv, double angle) => new()
    {
        Id = 1, LoadCaseId = 9, Kind = FemImperfectionKinds.Sway, Tag = "Крен",
        ParamsJson = new FemSwayParams { PhiInv = phiInv, AngleDeg = angle }.ToJson(),
    };

    private static FemImperfection Bow(string plane, string sign, double? lOverE0 = 250, double? e0Mm = null) => new()
    {
        Id = 2, LoadCaseId = 9, Kind = FemImperfectionKinds.Bow, Tag = "Погибь",
        ParamsJson = new FemBowParams
        {
            MemberTags = ["C"], Plane = plane, Sign = sign, Activity = FemBowParams.ActivityAlways,
            E0Mode = e0Mm != null ? FemBowParams.E0Absolute : FemBowParams.E0Relative, LOverE0 = lOverE0, E0Mm = e0Mm,
        }.ToJson(),
    };

    private static void RunResolveAxes()
    {
        var (nodes, elements) = Column();
        var members = new List<FemMember> { new() { ElemTag = "C", RotationDeg = 0 } };
        var y = FemImperfectionResolver.Resolve([(Bow("y", "plus"), 1.0)], elements, nodes, members);
        var bow = (FemResolvedBow)y.Items.Single();
        TestHarness.Check("цепочка — все КЭ КонЭ", bow.ElemTags.Count == N);
        TestHarness.CheckRel("e₀ = L/250", bow.E0, L / 250, 1e-12);
        TestHarness.Check("y вертикального КонЭ — глобальная X", Math.Abs(bow.Direction.X - 1) < 1e-12);
        TestHarness.Check("предупреждений о знаке и активности нет",
            y.Diagnostics.All(d => d.Code is not ("imperfection_sign_auto" or "imperfection_activity")));

        var z = FemImperfectionResolver.Resolve([(Bow("z", "minus", null, 30), 0.5)], elements, nodes, members);
        var bz = (FemResolvedBow)z.Items.Single();
        TestHarness.CheckRel("e₀ = −0,5·30 мм", bz.E0, -0.015, 1e-12);
        TestHarness.Check("z = x × y = глобальная Y", Math.Abs(bz.Direction.Y - 1) < 1e-12);

        members[0].RotationDeg = 90;
        var r = (FemResolvedBow)FemImperfectionResolver.Resolve([(Bow("y", "plus"), 1.0)], elements, nodes, members).Items.Single();
        TestHarness.Check("поворот КонЭ 90°: y — глобальная Y", Math.Abs(r.Direction.Y - 1) < 1e-12);

        var s = (FemResolvedSway)FemImperfectionResolver.Resolve([(Sway(200, 90), -1.0)], elements, nodes, members).Items.Single();
        TestHarness.CheckRel("φ = −1/200", s.Phi, -1.0 / 200, 1e-12);
        TestHarness.Check("направление 90° — +Y", Math.Abs(s.Direction.Y - 1) < 1e-12 && Math.Abs(s.Direction.X) < 1e-12);
        TestHarness.Check("охват «вся схема»", s.ElemTags == null);

        var auto = FemImperfectionResolver.Resolve([(new FemImperfection
        {
            Kind = FemImperfectionKinds.Bow, ParamsJson = new FemBowParams { MemberTags = ["C"], LOverE0 = 300 }.ToJson(),
        }, 1.0)], elements, nodes, members);
        var autoBow = (FemResolvedBow)auto.Items[0];
        TestHarness.Check("знак «авто» и п. 5.3.2(6) — отложены до предварительного расчёта",
            !auto.HasErrors && auto.Diagnostics.Count == 0 && autoBow.SignAuto && autoBow.ActivityEn1993 && autoBow.E0 > 0);
        var undecided = FemImperfectionResolver.Decide(auto.Items, null, elements, nodes, "нет расчёта");
        TestHarness.Check("без предварительного расчёта — «+» и «всегда» с предупреждениями",
            undecided.Diagnostics.Count(d => !d.IsError && d.Code is "imperfection_sign_auto" or "imperfection_activity") == 2
            && ((FemResolvedBow)undecided.Items.Single()).E0 > 0);
    }

    private static FemRcModelInput Input(List<FemMeshNode> nodes, List<FemElement> elements, int baseMask, int topMask,
        IReadOnlyList<FemMeshNodeLoad> loads, IReadOnlyList<FemLoadCase> cases, FemRcStage stage,
        IReadOnlyList<FemResolvedImperfection> imperfections, bool allCases = false) => new()
    {
        MeshNodes = nodes, MeshElements = elements, Properties = new Props(),
        Members = [new FemMember { ElemTag = "C" }],
        Supports = topMask == 0
            ? [new FemMeshNodeSupport { NodeTag = "1", Mask = baseMask }]
            : [new FemMeshNodeSupport { NodeTag = "1", Mask = baseMask }, new FemMeshNodeSupport { NodeTag = (N + 1).ToString(), Mask = topMask }],
        LoadCases = cases, MeshNodeLoads = loads, Stages = [stage], Imperfections = imperfections, AllLoadCases = allCases,
    };

    private static RcSecantOptions Options() => new()
    {
        PhysicallyLinear = true,
        Solver = new SecantPicardOptions { Geometric = true, GeometricSecondOrder = true, TolDisplacement = 1e-10 },
    };

    private static RcSecantStepFields? Solve(string name, FemRcModelInput input)
    {
        var adapted = FemRcModelAdapter.Adapt(input);
        TestHarness.Check($"{name}: адаптер без ошибок", !adapted.HasErrors, string.Join("; ", adapted.Report));
        if (adapted.HasErrors) return null;
        var run = RcSecantAnalysis.Run(adapted.Model, Options());
        TestHarness.Check($"{name}: расчёт сошёлся", run.Result.Completed, run.Result.Message ?? "");
        return run.Result.Completed ? run.LastConvergedFields() : null;
    }

    private static double Disp(RcSecantStepFields f, int node, int dof)
        => f.Displacements[RcSecantStepFields.NodeComponents * Array.IndexOf(f.NodeIds, node) + dof];

    private static double MomentI(RcSecantStepFields f, int beamIndex)
    {
        int o = beamIndex * RcSecantStepFields.BeamForceComponents;
        return Math.Sqrt(f.BeamForces[o + 4] * f.BeamForces[o + 4] + f.BeamForces[o + 5] * f.BeamForces[o + 5]);
    }

    private static readonly FemLoadCase Vertical = new() { Id = 1, Tag = "P" };
    private static readonly FemLoadCase Horizontal = new() { Id = 2, Tag = "H" };
    private static readonly FemLoadCase Imperfection = new() { Id = 9, Tag = "Imp", Sp20Type = FemSp20Types.Imperfection };

    private static FemMeshNodeLoad Load(int caseId, int node, double fx, double fz) =>
        new() { LoadCaseId = caseId, MeshNodeTag = node.ToString(), Fx = fx, Fz = fz };

    /// <summary>Консоль 6 м, P = 0,3·P_cr: крен 1/200 по +X против силы H = φ·P на верхе без несовершенства.</summary>
    private static void RunSwayEquivalence()
    {
        var (nodes, elements) = Column();
        double pcr = Math.PI * Math.PI * E * I / (4 * L * L), p = 0.3 * pcr, phi = 1.0 / 200;
        var resolved = FemImperfectionResolver.Resolve([(Sway(200, 0), 1.0)], elements, nodes, [new FemMember { ElemTag = "C" }]);
        var imp = Solve("крен", Input(nodes, elements, 0x3F, 0, [Load(1, N + 1, 0, -p)], [Vertical, Imperfection],
            new FemRcStage("S", [(1, 1.0)], 4), resolved.Items));
        var eq = Solve("сила φ·P", Input(nodes, elements, 0x3F, 0, [Load(1, N + 1, 0, -p), Load(2, N + 1, phi * p, 0)],
            [Vertical, Horizontal], new FemRcStage("S", [(1, 1.0), (2, 1.0)], 4), []));
        if (imp == null || eq == null) return;
        TestHarness.CheckRel("u верха", Disp(imp, N + 1, 0), Disp(eq, N + 1, 0), 1e-6);
        TestHarness.CheckRel("M заделки", MomentI(imp, 0), MomentI(eq, 0), 1e-6);
        TestHarness.Check("u верха по +X", Disp(imp, N + 1, 0) > 0);
    }

    /// <summary>
    /// Шарнирная колонна, P = 0,5·P_cr, погибь КонЭ «C» в плоскости y (глобальная X) L/250 со знаком «−» и коэффициентом
    /// 0,8 (e₀ = −0,8·L/250) против аналитики 8e₀/(kL)²·(sec(kL/2) − 1) и M = P·y.
    /// </summary>
    private static void RunBowEquivalence()
    {
        var (nodes, elements) = Column();
        const int pinnedBase = 0b100111, pinnedTop = 0b100011;
        double pcr = Math.PI * Math.PI * E * I / (L * L), p = 0.5 * pcr, e0 = -0.8 * L / 250;
        var resolved = FemImperfectionResolver.Resolve([(Bow("y", "minus"), 0.8)], elements, nodes, [new FemMember { ElemTag = "C" }]);
        TestHarness.Check("привязка без ошибок", !resolved.HasErrors);
        var imp = Solve("погибь", Input(nodes, elements, pinnedBase, pinnedTop, [Load(1, N + 1, 0, -p)], [Vertical, Imperfection],
            new FemRcStage("S", [(1, 1.0)], 5), resolved.Items));

        if (imp == null) return;
        int mid = N / 2 + 1;
        TestHarness.Check("прогиб середины по −X (знак «−»)", Disp(imp, mid, 0) < 0);
        double k = Math.Sqrt(p / (E * I));
        double yMid = 8 * e0 / (k * L * k * L) * (1 / Math.Cos(k * L / 2) - 1);
        TestHarness.CheckRel("e₀ + u середины против аналитики", e0 + Disp(imp, mid, 0), yMid, 5e-3);
        // Концевые силы второго порядка выдаются по K·d без K_σ·d (срез 0, отступление 2) — допуск 1 %.
        TestHarness.CheckRel("M середины против P·y", MomentI(imp, N / 2), p * Math.Abs(e0 + Disp(imp, mid, 0)), 1e-2);
    }

    private static void RunGuards()
    {
        var (nodes, elements) = Column();
        elements[2].StiffnessNum = 0;   // КЭ 3 вне модели (стержневой аналог без жёсткости)
        var bow = FemImperfectionResolver.Resolve([(Bow("y", "plus"), 1.0)], elements, nodes, [new FemMember { ElemTag = "C" }]);
        var r = FemRcModelAdapter.Adapt(Input(nodes, elements, 0x3F, 0, [Load(1, N + 1, 0, -1e3)], [Vertical, Imperfection],
            new FemRcStage("S", [(1, 1.0)]), bow.Items));
        TestHarness.Check("погибь с КЭ вне модели — ошибка",
            r.Diagnostics.Any(d => d.Code == "imperfection_outside_model" && d.IsError && d.SourceKeys!.SequenceEqual(["3"])));

        var (n2, e2) = Column();
        var sway = FemImperfectionResolver.Resolve([(new FemImperfection
        {
            Kind = FemImperfectionKinds.Sway,
            ParamsJson = new FemSwayParams { Scope = FemSwayParams.ScopeList, ElementTags = ["1", "2"], PhiInv = 300 }.ToJson(),
        }, 1.0)], e2, n2, []);
        var a = FemRcModelAdapter.Adapt(Input(n2, e2, 0x3F, 0, [Load(1, N + 1, 0, -1e3), Load(9, 2, 5, 0)], [Vertical, Imperfection],
            new FemRcStage("S", [(1, 1.0)]), sway.Items, allCases: true));
        var rs = a.Model.Imperfections.OfType<RcSway>().Single();
        TestHarness.Check("крен по перечню КЭ — стержни 1, 2", rs.Beams!.SetEquals([1, 2]) && rs.Shells!.Count == 0);
        TestHarness.Check("нагрузка в загружении-несовершенстве — ошибка",
            a.Diagnostics.Any(d => d.Code == FemCanonicalValidator.ImperfectionHasLoadsCode && d.IsError));
        TestHarness.Check("загружение-несовершенство не даёт нагрузки",
            a.Model.LoadCases.Single(c => c.Id == 9) is { Nodal.Count: 0, BeamEnds.Count: 0 });

        var chain = FemImperfectionResolver.Resolve([(new FemImperfection
        {
            Kind = FemImperfectionKinds.Bow, ParamsJson = new FemBowParams { ElementChains = [["1", "3"]], LOverE0 = 300 }.ToJson(),
        }, 1.0)], e2, n2, []);
        TestHarness.Check("разрывная цепочка КЭ — ошибка", chain.Diagnostics.Any(d => d.Code == "imperfection_chain_invalid" && d.IsError));
        var norm = FemImperfectionResolver.Resolve([(new FemImperfection
        {
            Kind = FemImperfectionKinds.Sway, ParamsJson = new FemSwayParams { PhiMode = FemSwayParams.PhiEn1993 }.ToJson(),
        }, 1.0)], e2, n2, []);
        var normSway = (FemResolvedSway)norm.Items.Single();
        // h — размах z охвата (вся колонна, 6 м): α_h = 2/√6 = 0,8165, m = 1 → φ = 0,8165/200.
        TestHarness.CheckRel("φ по (5.5), h = размах z", normSway.Phi, 2 / Math.Sqrt(L) / 200, 1e-12);
        TestHarness.Check("ход вычисления φ", normSway.Norm is { M: 1 } && normSway.HeightSource.Contains("размах"));
    }

    /// <summary>
    /// Второй этап (спека 5.4): предварительный расчёт <see cref="RcFirstOrderState"/> модели адаптера → решения
    /// <see cref="FemImperfectionResolver.Decide"/> → <see cref="FemRcModelAdapter.ReplaceImperfections"/>. Колонна с
    /// заделкой внизу и шарнирно-подвижным верхом: знак «авто» — по боковой силе в середине; активность — на границе
    /// 0,25·N_cr,шарн (N_cr = π²EI/L²); шарнир по Mz у нижнего КЭ и шарнирный верх — оба конца шарнирные, погибь в
    /// плоскости y не активна, в плоскости z (шарнир по Mz её не касается) — активна.
    /// </summary>
    private static void RunDecide()
    {
        const int fixedBase = 0x3F, pinnedTop = 0b100011;
        double ncr = Math.PI * Math.PI * E * I / (L * L);
        int mid = N / 2 + 1;

        FemImperfectionDecision Decide(double p, double lateral, int baseRelease = 0, string plane = "y")
        {
            var (nodes, elements) = Column();
            if (baseRelease != 0) elements[0].ReleaseI = baseRelease;
            var bow = new FemImperfection
            {
                Id = 3, LoadCaseId = 9, Kind = FemImperfectionKinds.Bow, Tag = "Авто",
                ParamsJson = new FemBowParams { MemberTags = ["C"], Plane = plane, LOverE0 = 250 }.ToJson(),
            };
            var resolved = FemImperfectionResolver.Resolve([(bow, 1.0)], elements, nodes, [new FemMember { ElemTag = "C" }]);
            var loads = new List<FemMeshNodeLoad> { Load(1, N + 1, 0, -p) };
            if (lateral != 0) loads.Add(Load(1, mid, lateral, 0));
            var input = Input(nodes, elements, fixedBase, pinnedTop, loads, [Vertical, Imperfection], new FemRcStage("S", [(1, 1.0)]),
                resolved.Items);
            var adapted = FemRcModelAdapter.Adapt(input);
            TestHarness.Check("адаптер без ошибок", !adapted.HasErrors, string.Join("; ", adapted.Report));
            var state = RcFirstOrderState.Run(adapted.Model, Options());
            var decision = FemImperfectionResolver.Decide(input.Imperfections, state, elements, nodes);
            var diag = FemRcModelAdapter.ReplaceImperfections(adapted.Model, decision.Items);
            TestHarness.Check("модель — решённые погиби", adapted.Model.Imperfections.Count == decision.Items.Count && !diag.Any(d => d.IsError));
            return decision;
        }

        var minus = Decide(0.3 * ncr, -1e3);
        var row = minus.Report.Bows.Single();
        TestHarness.Check("активна при 0,3·N_cr (конец с моментом — заделка)", row.Active, row.ActiveReason);
        TestHarness.CheckRel("N_Ed = P", row.NEd ?? 0, 0.3 * ncr, 1e-9);
        TestHarness.CheckRel("N_cr = π²EI/L²", row.Ncr ?? 0, ncr, 1e-9);
        TestHarness.Check("знак «авто» — по прогибу к −X", ((FemResolvedBow)minus.Items.Single()).E0 < 0 && row.Sign == "−", row.SignSource);
        TestHarness.CheckRel("|e₀| = L/250", Math.Abs(row.E0), L / 250, 1e-12);

        var plus = Decide(0.3 * ncr, 1e3);
        TestHarness.Check("знак «авто» — по прогибу к +X", ((FemResolvedBow)plus.Items.Single()).E0 > 0);
        var none = Decide(0.3 * ncr, 0);
        TestHarness.Check("прогиба нет — «+»", ((FemResolvedBow)none.Items.Single()).E0 > 0 && none.Report.Bows[0].SignSource.Contains("нет"));

        var low = Decide(0.24 * ncr, -1e3);
        TestHarness.Check("0,24·N_cr — не активна, из модели убрана", low.Items.Count == 0 && !low.Report.Bows[0].Active
            && low.Diagnostics.Any(d => d.Code == "imperfection_bow_inactive"));
        TestHarness.Check("0,26·N_cr — активна", Decide(0.26 * ncr, -1e3).Items.Count == 1);

        // Шарнир по Mz (бит 5) у низа: плоскость y (прогиб вдоль местной y — изгиб вокруг z) — оба конца шарнирные.
        var hinged = Decide(0.5 * ncr, -1e3, baseRelease: 1 << 5);
        TestHarness.Check("шарнир Mz внизу, верх шарнирный — погибь y не активна",
            hinged.Items.Count == 0 && hinged.Report.Bows[0].ActiveReason.Contains("шарнир"), hinged.Report.Bows[0].ActiveReason);
        var hingedZ = Decide(0.5 * ncr, 0, baseRelease: 1 << 5, plane: "z");
        TestHarness.Check("тот же шарнир Mz — погибь z активна", hingedZ.Items.Count == 1, hingedZ.Report.Bows[0].ActiveReason);
        TestHarness.Check("строки отчёта", minus.Report.Lines().Any(l => l.Contains("Погибь «Авто")) && minus.Report.Lines().Any(l => l.Contains("α_cr")));

        // Крен (5.5) двух консольных стоек «C» (x = 0) и «D» (x = 5): подсказка m — стойки с N > 50 % средней; H/V реакций.
        FemImperfectionDecision Sway(double pc, double pd, double h)
        {
            var nodes = new List<FemMeshNode>();
            var elements = new List<FemElement>();
            foreach (var (member, x, b) in new[] { ("C", 0.0, 0), ("D", 5.0, 10) })
            {
                for (int i = 0; i <= N; i++) nodes.Add(new FemMeshNode { NodeTag = (b + i + 1).ToString(), X = x, Z = L * i / N });
                for (int i = 1; i <= N; i++)
                    elements.Add(new FemElement
                    {
                        ElemTag = (b + i).ToString(), ElemType = "beam", NodeIdsJson = JsonSerializer.Serialize(new[] { b + i, b + i + 1 }),
                        SourceMemberTag = member,
                    });
            }
            var members = new List<FemMember> { new() { ElemTag = "C" }, new() { ElemTag = "D" } };
            var sway = new FemImperfection
            {
                Id = 4, LoadCaseId = 9, Kind = FemImperfectionKinds.Sway, Tag = "Крен EN",
                ParamsJson = new FemSwayParams { PhiMode = FemSwayParams.PhiEn1993, M = 2 }.ToJson(),
            };
            var resolved = FemImperfectionResolver.Resolve([(sway, 1.0)], elements, nodes, members);
            var input = new FemRcModelInput
            {
                MeshNodes = nodes, MeshElements = elements, Properties = new Props(), Members = members,
                Supports = [new FemMeshNodeSupport { NodeTag = "1", Mask = 0x3F }, new FemMeshNodeSupport { NodeTag = "11", Mask = 0x3F }],
                LoadCases = [Vertical, Imperfection], Stages = [new FemRcStage("S", [(1, 1.0)])], Imperfections = resolved.Items,
                MeshNodeLoads = [Load(1, N + 1, h, -pc), Load(1, N + 11, 0, -pd)],
            };
            var adapted = FemRcModelAdapter.Adapt(input);
            var state = RcFirstOrderState.Run(adapted.Model, Options());
            return FemImperfectionResolver.Decide(input.Imperfections, state, elements, nodes);
        }
        var s1 = Sway(1e5, 0.4e5, 0);
        var r1 = s1.Report.Sways.Single();
        TestHarness.Check("m = 2: обе стойки > 50 % средней", r1.MHint == 2 && r1.Columns == 2, $"{r1.MHint}/{r1.Columns}");
        TestHarness.CheckRel("φ по (5.5): h = 6, m = 2", r1.Phi, 2 / Math.Sqrt(L) * Math.Sqrt(0.75) / 200, 1e-12);
        var s2 = Sway(1e5, 0.3e5, 2e4);
        var r2 = s2.Report.Sways.Single();
        TestHarness.Check("m = 1: вторая стойка 0,3P < 0,325P", r2.MHint == 1, $"{r2.MHint}");
        TestHarness.CheckRel("H_Ed/V_Ed = 2e4 / 1,3e5", r2.HOverV ?? 0, 2e4 / 1.3e5, 1e-9);
        TestHarness.Check("строка крена — ход вычисления и подсказка", s2.Report.Lines().First().Contains("α_h") && s2.Report.Lines().First().Contains("подсказка m = 1"));
    }
}
