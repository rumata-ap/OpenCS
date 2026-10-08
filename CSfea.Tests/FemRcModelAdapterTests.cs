using System.Text.Json;
using CScore;
using CScore.Fem;
using CSfea.CScoreBridge;
using CSfea.CScoreBridge.Structural;

namespace CSfea.Tests;

/// <summary>Адаптер схемы FEM OpenCS → <see cref="RcStructuralModel"/> (CSfea, срез 4в).</summary>
[HarnessChecks]
public class FemRcModelAdapterTests
{
    private const double E = 30e9, G = 12.5e9, A = 0.15, Iy = 3.125e-3, Iz = 1.125e-3, J = 2.8e-3;

    [Fact]
    public static void RunAll()
    {
        RunShellContourAndAxes();
        RunShellWithoutSection();
        RunBoundary();
        RunCantileverMemberLoads();
        RunReleasedBeamLoad();
        RunMeshLoadsAndSelfWeight();
        RunKinematicAndMissingCases();
    }

    /// <summary>Упругие свойства: стержни — прямоугольник 0,3 × 0,5, пластины — E, ν 0,2, h 0,2; удельный вес 25 кН/м³.</summary>
    private sealed class Props : IFemElementStiffnessSource
    {
        public FemShellStiffness? Shell(FemElement e) => e.ElemType == "shell" && e.StiffnessNum != 0 ? new(E, 0.2, 0.2) : null;
        public FemBarStiffness? Bar(FemElement e) => e.ElemType == "beam" ? new(E, G, A, Iy, Iz, J) : null;
        public double? UnitWeight(FemElement e) => 25e3;
        public double? BarArea(FemElement e) => e.ElemType == "beam" ? A : null;
    }

    private static FemMeshNode N(int tag, double x, double y, double z = 0, string? source = null) =>
        new() { NodeTag = tag.ToString(), X = x, Y = y, Z = z, SourceNodeTag = source };

    private static FemElement El(int tag, string type, int[] nodes, string? member = null) => new()
    {
        ElemTag = tag.ToString(), ElemType = type, NodeIdsJson = JsonSerializer.Serialize(nodes), SourceMemberTag = member,
        ThicknessM = type == "shell" ? 0.2 : null,
    };

    private static FemRcStage Stage(params int[] cases) => new("S", cases.Select(c => (c, 1.0)).ToList());

    /// <summary>Q4 «1 2 4 3» → контур; ось x — угол осей выдачи и поворот в оси армирования; дедупликация сечений.</summary>
    private static void RunShellContourAndAxes()
    {
        TestHarness.Section("FemRcModelAdapter: контур Q4 и оси сечения пластины");
        // Квадрат A(0,0) B(1,0) C(1,1) D(0,1): узлы 1=A 2=B 3=C 4=D, хранение «1 2 4 3» = [A, B, D, C].
        var nodes = new[] { N(1, 0, 0), N(2, 1, 0), N(3, 1, 1), N(4, 0, 1), N(5, 2, 0), N(6, 2, 1) };
        var e1 = El(1, "shell", [1, 2, 4, 3]);
        e1.LocalAxisAngleDeg = 90;
        var e2 = El(2, "shell", [2, 5, 3, 6]);
        var e3 = El(3, "shell", [2, 5, 3]);
        var steel = BridgeTestSections.LinearSteel(30_000);
        var diag = steel.GetDiagramms(DiagrammType.L2)![CalcType.N];
        var mats = new PlateSectionMaterials { ConcreteDiagram = diag, RebarDiagram = diag };
        var plate = new PlateSection { H = 0.2, NLayers = 8 };
        var rebar = new FemRcPlateSection(new PlateElementSection(plate, "П1", "k1", null) { ForceAngleDeg = 30 }, mats, "m");
        var r = FemRcModelAdapter.Adapt(new FemRcModelInput
        {
            MeshNodes = nodes, MeshElements = [e1, e2, e3], LoadCases = [], Stages = [], Properties = new Props(),
            PlateSection = e => e.ElemTag == "1" ? null : rebar,
        });
        TestHarness.Check("нет ошибок", !r.HasErrors, string.Join("; ", r.Report));
        var s1 = r.Model.Shells.Single(s => s.Id == 1);
        TestHarness.Check("контур 1 2 3 4", s1.NodeIds.SequenceEqual(new[] { 1, 2, 3, 4 }), string.Join(" ", s1.NodeIds));
        TestHarness.CheckRel("ось x с углом 90° — глобальная Y", s1.SectionAxisX![1], 1.0, 1e-12);
        TestHarness.Check("пластина 1 упругая", s1.Section.Elastic != null && s1.Section.Plate == null);
        var s2 = r.Model.Shells.Single(s => s.Id == 2);
        // Ось выдачи — X1 (узел 2 → 5 = +X); оси армирования — повёрнуты на −30°.
        TestHarness.CheckRel("ось армирования cos 30°", s2.SectionAxisX![0], Math.Cos(Math.PI / 6), 1e-12);
        TestHarness.CheckRel("ось армирования −sin 30°", s2.SectionAxisX![1], -0.5, 1e-12);
        TestHarness.Check("одно ЖБ-сечение на два КЭ", ReferenceEquals(s2.Section, r.Model.Shells.Single(s => s.Id == 3).Section));
        TestHarness.Check("узлов 6", r.Model.Nodes.Count == 6);
    }

    private static void RunShellWithoutSection()
    {
        TestHarness.Section("FemRcModelAdapter: пластина без сечения — ошибка");
        var e = El(1, "shell", [1, 2, 3]);
        e.StiffnessNum = 0;
        var r = FemRcModelAdapter.Adapt(new FemRcModelInput
        {
            MeshNodes = [N(1, 0, 0), N(2, 1, 0), N(3, 0, 1)], MeshElements = [e], LoadCases = [], Stages = [],
            Properties = new Props(),
        });
        TestHarness.Check("ошибка shell_no_section", r.HasErrors && r.Diagnostics.Any(d => d.Code == "shell_no_section"));
    }

    /// <summary>Опора КонЭ через исходный узел, опора и пружина сетки, жёсткое тело, шарнир конца КЭ.</summary>
    private static void RunBoundary()
    {
        TestHarness.Section("FemRcModelAdapter: граничные условия");
        var fem = new[] { new FemNode { Id = 1, NodeTag = "1", DofMask = 0x3F } };
        var nodes = new[] { N(10, 0, 0, 0, "1"), N(11, 3, 0, 0), N(12, 6, 0, 0), N(13, 6, 0, 1) };
        var beam = El(1, "beam", [10, 11]);
        var beam2 = El(2, "beam", [11, 12]);
        beam2.ReleaseJ = 0b11_0000;
        var spring = new FemSpring { NodeTag = "12" };
        spring.SetStiffnesses([0, 0, 5e6, 0, 0, 0]);
        var body = new FemRigidBody { MasterNodeTag = "12" };
        body.SetSlaveNodeTags(["13"]);
        var r = FemRcModelAdapter.Adapt(new FemRcModelInput
        {
            MeshNodes = nodes, MeshElements = [beam, beam2], Nodes = fem, LoadCases = [], Stages = [], Properties = new Props(),
            Supports = [new FemMeshNodeSupport { NodeTag = "11", Mask = 0b011 }], Springs = [spring], RigidBodies = [body],
        });
        TestHarness.Check("нет ошибок", !r.HasErrors, string.Join("; ", r.Report));
        TestHarness.Check("опора КонЭ на узле 10", r.Model.Supports.Any(s => s.NodeId == 10 && s.Mask == 0x3F));
        TestHarness.Check("опора сетки на узле 11", r.Model.Supports.Any(s => s.NodeId == 11 && s.Mask == 0b011));
        TestHarness.Check("пружина Z узла 12", r.Model.Springs.Single() == new RcSpring(12, 2, 5e6));
        TestHarness.Check("жёсткое тело 12 → 13", r.Model.RigidBodies.Single() is { Master: 12 } b && b.Slaves.SequenceEqual(new[] { 13 }));
        TestHarness.Check("узел 13 в модели", r.Model.Nodes.Any(n => n.Id == 13));
        TestHarness.Check("шарнир конца J", r.Model.Beams.Single(x => x.Id == 2).ReleaseJ == 0b11_0000);
        var y = r.Model.Beams[0].RefVec!;
        TestHarness.Check("местная Y горизонтального стержня — глобальная Z", Math.Abs(y[2] - 1) < 1e-12);
    }

    // Консоль своей схемы: КонЭ 1 (узлы 1 → 2, L = 4 м), 4 КЭ.
    private static FemRcModelInput Cantilever(IReadOnlyList<FemMemberLoad> loads, double rotationDeg = 0)
    {
        var fem = new[]
        {
            new FemNode { Id = 1, NodeTag = "1", X = 0, DofMask = 0x3F },
            new FemNode { Id = 2, NodeTag = "2", X = 4 },
        };
        var member = new FemMember { Id = 7, ElemTag = "1", ElemType = "beam", NodeIdsJson = "[1,2]", RotationDeg = rotationDeg };
        var nodes = Enumerable.Range(0, 5).Select(i => N(i + 1, i, 0, 0, i == 0 ? "1" : i == 4 ? "2" : null)).ToArray();
        var elements = Enumerable.Range(0, 4).Select(i => El(i + 1, "beam", [i + 1, i + 2], "1")).ToArray();
        return new FemRcModelInput
        {
            MeshNodes = nodes, MeshElements = elements, Nodes = fem, Members = [member], Properties = new Props(),
            LoadCases = [new FemLoadCase { Id = 1, Tag = "q" }], MemberLoads = loads, Stages = [Stage(1)],
        };
    }

    private static double TipUz(FemRcModelResult r)
    {
        var build = RcStructuralMeshBuilder.Build(r.Model, new LinearRcSectionFactory());
        var u = build.Mesh.SolveLinear(build.Combination(r.Model.Stages[0].Loads), build.Bc);
        return u[build.Dof(5, 2)];
    }

    /// <summary>
    /// Нагрузки КонЭ на консоли: частичная равномерная, сосредоточенная внутри КЭ, местная при повороте сечения —
    /// прогиб конца точен (эрмитовы КЭ с согласованными силами точны в узлах).
    /// </summary>
    private static void RunCantileverMemberLoads()
    {
        TestHarness.Section("FemRcModelAdapter: нагрузки КонЭ на консоли");
        const double l = 4, q = 10e3, p = 7e3;
        double ei = E * Iz; // вертикальная нагрузка вдоль местной Y (вверх) — изгиб вокруг местной Z

        // Частичная равномерная [0,7; 2,6] м — внутри КЭ и через узлы.
        var partial = new FemMemberLoad
        {
            LoadCaseId = 1, MemberId = 7, CoordinateSystem = "global", DistributionType = "uniform",
            StartOffsetM = 0.7, EndOffsetM = l - 2.6, QzStart = -q, QzEnd = -q,
        };
        var r = FemRcModelAdapter.Adapt(Cantilever([partial]));
        TestHarness.Check("нет ошибок", !r.HasErrors, string.Join("; ", r.Report));
        double Tip(double a, double b) => q / (6 * ei) * (l * b * b * b - Math.Pow(b, 4) / 4 - l * a * a * a + Math.Pow(a, 4) / 4);
        TestHarness.CheckRel("ΣFz стадии", r.StageTotals[0].Fz, -q * 1.9, 1e-12);
        TestHarness.CheckRel("прогиб конца от частичной нагрузки", -TipUz(r), Tip(0.7, 2.6), 1e-9);

        // Сосредоточенная в 1,3 м — внутри КЭ 2.
        var point = new FemMemberLoad
        {
            LoadCaseId = 1, MemberId = 7, CoordinateSystem = "global", DistributionType = "point", StartOffsetM = 1.3, QzStart = -p,
        };
        r = FemRcModelAdapter.Adapt(Cantilever([point]));
        TestHarness.CheckRel("прогиб конца от силы", -TipUz(r), p * 1.3 * 1.3 * (3 * l - 1.3) / (6 * ei), 1e-9);

        // Трапеция по всей длине в местных осях: −y местная при повороте сечения 0 — вниз по глобальной Z.
        var trap = new FemMemberLoad
        {
            LoadCaseId = 1, MemberId = 7, CoordinateSystem = "local", DistributionType = "trapezoidal",
            QyStart = -q, QyEnd = 0,
        };
        r = FemRcModelAdapter.Adapt(Cantilever([trap]));
        // Треугольная нагрузка q·(1 − x/L), максимум у заделки: δ = qL⁴/30EI.
        TestHarness.CheckRel("прогиб конца от местной трапеции", -TipUz(r), q * Math.Pow(l, 4) / (30 * ei), 1e-9);

        // Поворот сечения на 90°: местная −y — горизонтальная, вертикального прогиба нет.
        r = FemRcModelAdapter.Adapt(Cantilever([trap], 90));
        TestHarness.CheckLess("при повороте 90° прогиб по Z ≈ 0", Math.Abs(TipUz(r)), 1e-12);
    }

    /// <summary>Балка с заделкой и шарнирным концом: нагрузка КонЭ → концевые силы КЭ, реакции 5qL/8 и 3qL/8.</summary>
    private static void RunReleasedBeamLoad()
    {
        TestHarness.Section("FemRcModelAdapter: нагрузка на стержень с шарниром");
        const double l = 4, q = 10e3;
        var input = Cantilever([new FemMemberLoad
        {
            LoadCaseId = 1, MemberId = 7, CoordinateSystem = "global", DistributionType = "uniform", QzStart = -q, QzEnd = -q,
        }]);
        // Один КЭ на всю длину, шарнир (My, Mz) у конца J, опора по поступательным DOF узла 5.
        var single = El(1, "beam", [1, 5], "1");
        single.ReleaseJ = 0b11_0000;
        input = new FemRcModelInput
        {
            MeshNodes = input.MeshNodes.Where(n => n.NodeTag is "1" or "5").ToList(), MeshElements = [single],
            Nodes = input.Nodes, Members = input.Members, Properties = input.Properties, LoadCases = input.LoadCases,
            MemberLoads = input.MemberLoads, Stages = input.Stages,
            Supports = [new FemMeshNodeSupport { NodeTag = "5", Mask = 0b111 }],
        };
        var r = FemRcModelAdapter.Adapt(input);
        TestHarness.Check("нет ошибок", !r.HasErrors, string.Join("; ", r.Report));
        TestHarness.Check("нагрузка — концевыми силами КЭ", r.Model.LoadCases[0].BeamEnds.Count == 1 && r.Model.LoadCases[0].Nodal.Count == 0);
        var build = RcStructuralMeshBuilder.Build(r.Model, new LinearRcSectionFactory());
        var f = build.Combination(r.Model.Stages[0].Loads);
        var u = build.Mesh.SolveLinear(f, build.Bc);
        var react = build.Mesh.ComputeReactions(u, build.Bc, fExternal: f);
        TestHarness.CheckRel("реакция шарнирного конца 3qL/8", react[build.Dof(5, 2)], 3 * q * l / 8, 1e-9);
        TestHarness.CheckRel("реакция заделки 5qL/8", react[build.Dof(1, 2)], 5 * q * l / 8, 1e-9);
    }

    /// <summary>Нагрузка на пластины и собственный вес сеточного уровня — ΣF стадии = q·A + γ·h·A.</summary>
    private static void RunMeshLoadsAndSelfWeight()
    {
        TestHarness.Section("FemRcModelAdapter: нагрузки сеточного уровня и собственный вес");
        var nodes = new[] { N(1, 0, 0), N(2, 2, 0), N(3, 0, 3), N(4, 2, 3) };
        var load = new FemElementLoad { LoadCaseId = 1, LoadKind = FemElementLoadKinds.Uniform, Axis = "z" };
        load.SetTargetTags(["1"]);
        load.SetValues([-5e3]);
        var r = FemRcModelAdapter.Adapt(new FemRcModelInput
        {
            MeshNodes = nodes, MeshElements = [El(1, "shell", [1, 2, 3, 4])], Properties = new Props(),
            LoadCases = [new FemLoadCase { Id = 1, Tag = "q", SelfWeightFactor = 1.1 }], ElementLoads = [load],
            Stages = [new FemRcStage("S", [(1, 2.0)])],
        });
        TestHarness.Check("нет ошибок", !r.HasErrors, string.Join("; ", r.Report));
        TestHarness.CheckRel("ΣFz = 2·(q + 1,1·γh)·A", r.StageTotals[0].Fz, 2 * (-5e3 - 1.1 * 25e3 * 0.2) * 6, 1e-12);
    }

    private static void RunKinematicAndMissingCases()
    {
        TestHarness.Section("FemRcModelAdapter: кинематика и отсутствующие загружения — ошибки");
        var input = Cantilever([]);
        var r = FemRcModelAdapter.Adapt(new FemRcModelInput
        {
            MeshNodes = input.MeshNodes, MeshElements = input.MeshElements, Nodes = input.Nodes, Members = input.Members,
            Properties = input.Properties, LoadCases = input.LoadCases,
            KinematicLoads = [new FemKinematicLoad { LoadCaseId = 1, NodeId = 2, Dof = 2, Value = 0.01 }],
            Stages = [Stage(1), Stage(9), new FemRcStage("пусто", [])],
        });
        var codes = r.Diagnostics.Where(d => d.IsError).Select(d => d.Code).ToHashSet();
        TestHarness.Check("кинематика", codes.Contains("kinematic_not_supported"));
        TestHarness.Check("нет загружения", codes.Contains("stage_load_case_missing"));
        TestHarness.Check("пустая стадия", codes.Contains("stage_empty"));
    }
}
