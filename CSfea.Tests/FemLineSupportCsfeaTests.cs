using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.Planar;
using CSfea.CScoreBridge;
using CSfea.CScoreBridge.Structural;

namespace CSfea.Tests;

/// <summary>Линейные связи граней пластинчатого КонЭ в расчёте CSfea (раскладка на узлы сетки → адаптер).</summary>
[HarnessChecks]
public class FemLineSupportCsfeaTests
{
    private const double E = 30e9, Nu = 0.2, H = 0.2, A = 4, Q = 10e3;
    private const int Div = 16;

    [Fact]
    public static void RunAll()
    {
        RunSimplySupportedNavier();
        RunElasticEdgesEquilibrium();
        RunEdgeReactions();
    }

    private sealed class Props : IFemElementStiffnessSource
    {
        public FemShellStiffness? Shell(FemElement e) => new(E, Nu, H);
        public FemBarStiffness? Bar(FemElement e) => null;
        public double? UnitWeight(FemElement e) => null;
        public bool IsWithoutStiffness(FemElement e) => false;
        public double? BarArea(FemElement e) => null;
    }

    private static int Tag(int i, int j) => j * (Div + 1) + i + 1;

    /// <summary>Квадратная пластина A × A сеткой Div × Div Q4 (хранение «1 2 4 3»), КонЭ «P» с областью-контуром.</summary>
    private static (List<FemMeshNode> Nodes, List<FemElement> Elements, FemMember Member, PlanarRegion Region) Plate()
    {
        double h = A / Div;
        var nodes = new List<FemMeshNode>();
        for (int j = 0; j <= Div; j++)
            for (int i = 0; i <= Div; i++)
                nodes.Add(new FemMeshNode { NodeTag = Tag(i, j).ToString(), X = i * h, Y = j * h });
        var elements = new List<FemElement>();
        for (int j = 0; j < Div; j++)
            for (int i = 0; i < Div; i++)
                elements.Add(new FemElement
                {
                    ElemTag = (elements.Count + 1).ToString(), ElemType = "shell", SourceMemberTag = "P", ThicknessM = H,
                    NodeIdsJson = JsonSerializer.Serialize(new[] { Tag(i, j), Tag(i + 1, j), Tag(i, j + 1), Tag(i + 1, j + 1) }),
                });
        var region = new PlanarRegion
        {
            Id = 1, Contours = [new Contour([0, A, A, 0, 0], [0, 0, A, A, 0], "P") { Type = ContourType.Hull }],
        };
        return (nodes, elements, new FemMember { ElemTag = "P", ElemType = "shell", PlanarRegionId = 1 }, region);
    }

    /// <summary>Решение: линейные связи на всех гранях, равномерная q по всем КЭ.</summary>
    private static (FemRcModelResult Result, double[] U, Func<int, int, int> Dof, FemLineSupportNodal Lines) Solve(
        Func<FemPlateEdge, FemLineSupport> make)
    {
        var (nodes, elements, member, region) = Plate();
        var edges = FemLineSupportGeometry.EdgesByMember([member], [region]);
        var supports = edges["P"].Select(e => { var s = make(e); s.AssignEdge(e); return s; }).ToList();
        var lines = FemLineSupportResolver.ToNodal(supports, edges, nodes, elements);
        TestHarness.Check("раскладка без диагностик", lines.Diagnostics.Count == 0,
            string.Join("; ", lines.Diagnostics.Select(d => d.Message)));
        var (sup, spr) = lines.AppendTo([], []);

        var load = new FemElementLoad { LoadCaseId = 1, LoadKind = FemElementLoadKinds.Uniform, Axis = "z" };
        load.SetTargetTags(elements.Select(e => e.ElemTag));
        load.SetValues([-Q]);
        var r = FemRcModelAdapter.Adapt(new FemRcModelInput
        {
            MeshNodes = nodes, MeshElements = elements, Members = [member], Properties = new Props(),
            Supports = sup, Springs = spr, LoadCases = [new FemLoadCase { Id = 1, Tag = "q" }], ElementLoads = [load],
            Stages = [new FemRcStage("S", [(1, 1.0)])],
        });
        TestHarness.Check("адаптер без ошибок", !r.HasErrors, string.Join("; ", r.Report));
        var build = RcStructuralMeshBuilder.Build(r.Model, new LinearRcSectionFactory());
        var u = build.Mesh.SolveLinear(build.Combination(r.Model.Stages[0].Loads), build.Bc);
        return (r, u, build.Dof, lines);
    }

    /// <summary>
    /// Шарнирное опирание по контуру (X Y Z на всех гранях): с закреплённым поворотом кромки прогиб центра — ряд Навье
    /// 0,00406·qa⁴/D плюс сдвиг Миндлина (≈ 1,2 % при h/a = 0,05; на сетке 32 × 32 — +1,16 %); без него («мягкое»
    /// опирание) прогиб заметно больше (+4…5 %).
    /// </summary>
    private static void RunSimplySupportedNavier()
    {
        TestHarness.Section("Линейные связи: квадратная плита, шарнирно опёртая по контуру");
        var (_, u, dof, lines) = Solve(_ => new FemLineSupport { Mask = 0b111, FixEdgeRotation = true });
        TestHarness.Check("закреплены все узлы контура", lines.Supports.Select(s => s.NodeTag).Distinct().Count() == 4 * Div);
        TestHarness.Check("поворот кромки: грани y = const — UY, x = const — UX",
            lines.Supports.Where(s => s.NodeTag == Tag(Div / 2, 0).ToString()).All(s => s.Mask == (0b111 | 1 << 4)) &&
            lines.Supports.Where(s => s.NodeTag == Tag(0, Div / 2).ToString()).All(s => s.Mask == (0b111 | 1 << 3)));
        double d = E * H * H * H / (12 * (1 - Nu * Nu));
        double navier = 0.00406 * Q * Math.Pow(A, 4) / d;
        double w = -u[dof(Tag(Div / 2, Div / 2), 2)];
        TestHarness.CheckRel("прогиб центра ≈ Навье + сдвиг", w, navier * 1.012, 0.005);

        var (_, soft, softDof, _) = Solve(_ => new FemLineSupport { Mask = 0b111 });
        TestHarness.Check("мягкое опирание податливее", -soft[softDof(Tag(Div / 2, Div / 2), 2)] > w * 1.02);
    }

    /// <summary>
    /// Реакции по граням (линейный расчёт CSfea): у квадратной плиты, шарнирно опёртой по контуру, на каждую грань
    /// приходится q·a²/4 (углы делятся поровну), средняя погонная — q·a/4; у жёсткого опирания Миндлина в углах
    /// отрывающие реакции, так что наибольшая погонная — в середине грани и больше средней.
    /// </summary>
    private static void RunEdgeReactions()
    {
        TestHarness.Section("Линейные связи: реакции по граням");
        var (r, _, _, lines) = Solve(_ => new FemLineSupport { Mask = 0b111, FixEdgeRotation = true });
        var f = RcLinearAnalysis.Run(r.Model).Cases[0].Fields;
        var index = new Dictionary<string, int>();
        for (int i = 0; i < f.ReactionNodeIds.Length; i++) index[f.ReactionNodeIds[i].ToString()] = i;
        var edges = FemLineSupportReactions.Compute(lines.Chains,
            tag => index.TryGetValue(tag, out int i) ? f.Reactions.AsSpan(6 * i, 6).ToArray() : null);
        TestHarness.Check("четыре грани", edges.Count == 4);
        TestHarness.CheckRel("Σ по граням = q·a²", edges.Sum(e => e.Rz), Q * A * A, 1e-6);
        foreach (var e in edges)
            TestHarness.CheckRel($"грань {e.Key}: ΣRz = q·a²/4", e.Rz, Q * A * A / 4, 1e-6);
        TestHarness.CheckRel("средняя погонная = q·a/4", edges[0].QzMean, Q * A / 4, 1e-6);
        TestHarness.Check("наибольшая погонная — больше средней", edges[0].QzMax > edges[0].QzMean * 1.2,
            $"{edges[0].QzMax:G4} / {edges[0].QzMean:G4}");
    }

    /// <summary>Упругие грани Kz′ (X Y закреплены): сумма реакций пружин k·w равна нагрузке q·a².</summary>
    private static void RunElasticEdgesEquilibrium()
    {
        TestHarness.Section("Линейные связи: упругие грани — равновесие");
        const double k = 5e7;
        var (_, u, dof, lines) = Solve(_ => new FemLineSupport { Mask = 0b011, Kz = k });
        TestHarness.CheckRel("Σ k по граням = k′·периметр", lines.Springs.Sum(s => s.Kz), k * 4 * A, 1e-12);
        double reaction = lines.Springs.Sum(s => -s.Kz * u[dof(int.Parse(s.NodeTag), 2)]);
        TestHarness.CheckRel("Σ реакций = q·a²", reaction, Q * A * A, 1e-6);
    }
}
