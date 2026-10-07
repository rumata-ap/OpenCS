using CScore.Fem;
using CScore.Fem.Loads;
using Xunit;

namespace CScore.Tests;

public sealed class FemElementLoadNodalizerTests
{
    sealed class UnitWeights(double gamma, double area) : IFemSelfWeightSource
    {
        public double? UnitWeight(FemElement element) => gamma;
        public double? BarArea(FemElement element) => element.ElemType == "beam" ? area : null;
    }

    static FemMeshNode N(int tag, double x, double y, double z = 0) => new() { NodeTag = tag.ToString(), X = x, Y = y, Z = z };

    static FemElement E(string tag, string type, int[] nodes, double? h = null, string? member = null) => new()
    {
        ElemTag = tag, ElemType = type, NodeIdsJson = System.Text.Json.JsonSerializer.Serialize(nodes), ThicknessM = h,
        SourceMemberTag = member,
    };

    static FemElementLoad Load(string kind, double[] values, string target = "1", string cs = "global", string axis = "z")
    {
        var l = new FemElementLoad { LoadCaseId = 1, LoadKind = kind, CoordinateSystem = cs, Axis = axis };
        l.SetTargetTags(target.Split(','));
        l.SetValues(values);
        return l;
    }

    /// <summary>Прямоугольник 2 × 3 м, хранение «1 2 4 3»: узлы 1 (0,0), 2 (2,0), 3 (0,3), 4 (2,3).</summary>
    static FemLoadMeshContext Rect(IFemSelfWeightSource? sw = null) =>
        new([N(1, 0, 0), N(2, 2, 0), N(3, 0, 3), N(4, 2, 3)], [E("1", "shell", [1, 2, 3, 4], 0.2)], null, sw);

    static Dictionary<string, double[]> Run(FemElementLoad load, FemLoadMeshContext mesh, List<FemValidationDiagnostic>? diag = null)
    {
        var f = new Dictionary<string, double[]>();
        FemElementLoadNodalizer.Accumulate(load, mesh, 1, f, diag ?? []);
        return f;
    }

    [Fact]
    public void Q4_StorageOrder1243_GivesUpwardNormalAndQuarterAreaPerNode()
    {
        var mesh = Rect();
        var g = mesh.Geometry(mesh.Elements[0])!;
        Assert.Equal(new[] { 0, 1, 3, 2 }, g.Contour);
        Assert.Equal(1, g.ShellFrame().Z.Z, 12);
        Assert.All(g.ShellNodeWeights(), w => Assert.Equal(1.5, w, 12));

        var f = Run(Load("uniform", [-1000]), mesh);
        Assert.All(f.Values, v => Assert.Equal(-1500, v[2], 9));
    }

    [Fact]
    public void LocalNormalOnWall_PointsAlongContourNormal()
    {
        // Стена в плоскости XZ: обход 1 → 2 → 4 → 3 даёт нормаль −Y.
        var mesh = new FemLoadMeshContext([N(1, 0, 0, 0), N(2, 2, 0, 0), N(3, 0, 0, 3), N(4, 2, 0, 3)],
            [E("1", "shell", [1, 2, 3, 4], 0.2)]);
        var f = Run(Load("uniform", [100], cs: "local"), mesh);
        Assert.Equal(-600, f.Values.Sum(v => v[1]), 9);
        Assert.Equal(0, f.Values.Sum(v => v[2]), 9);
    }

    [Fact]
    public void T3_UniformAndConsistentMatrixIntegrateArea()
    {
        var mesh = new FemLoadMeshContext([N(1, 0, 0), N(2, 4, 0), N(3, 0, 3)], [E("1", "shell", [1, 2, 3])]);
        var g = mesh.Geometry(mesh.Elements[0])!;
        Assert.All(g.ShellNodeWeights(), w => Assert.Equal(2, w, 12));
        var m = g.ShellConsistentMatrix();
        Assert.Equal(1.0, m[0, 0] / (6.0 / 6), 12); // A/6 на диагонали
        Assert.Equal(0.5, m[0, 1], 12);              // A/12 вне диагонали
    }

    [Fact]
    public void NodalIntensity_LinearInX_GivesResultantAndItsPosition()
    {
        // q = 5·x: узлы 1, 3 (x = 0) — 0, узлы 2, 4 (x = 2) — 10. ∫q dA = 30, ∫q·x dA = 40.
        var mesh = Rect();
        var f = Run(Load("nodal", [0, 10, 0, 10]), mesh);
        Assert.Equal(30, f.Values.Sum(v => v[2]), 9);
        Assert.Equal(40, f.Sum(kv => kv.Value[2] * mesh.NodesByTag[kv.Key].X), 9);
    }

    [Fact]
    public void PointOnSkewedQuad_KeepsForceAndMoment()
    {
        var mesh = new FemLoadMeshContext([N(1, 0, 0), N(2, 3, 0.5), N(3, 0.4, 2), N(4, 2.6, 2.8)],
            [E("1", "shell", [1, 2, 3, 4])]);
        var g = mesh.Geometry(mesh.Elements[0])!;
        var (ex, ey, _) = g.ShellFrame();
        double x = 1.2, y = 0.9;
        var p = g.Points[0] + ex * x + ey * y;
        var f = Run(Load("point", [-1000, x, y]), mesh);
        Assert.Equal(-1000, f.Values.Sum(v => v[2]), 9);
        Assert.Equal(-1000 * p.X, f.Sum(kv => kv.Value[2] * mesh.NodesByTag[kv.Key].X), 6);
        Assert.Equal(-1000 * p.Y, f.Sum(kv => kv.Value[2] * mesh.NodesByTag[kv.Key].Y), 6);
    }

    [Fact]
    public void PointOutsideShell_IsReported()
    {
        var diag = new List<FemValidationDiagnostic>();
        Run(Load("point", [1, 5, 5]), Rect(), diag);
        Assert.Contains(diag, d => d.Code == "element_load_skipped" && d.Message.Contains("вне КЭ"));
    }

    [Fact]
    public void BarUniform_FixedEndMomentsBalanceTheLoad()
    {
        var mesh = new FemLoadMeshContext([N(1, 0, 0), N(2, 4, 0)], [E("1", "beam", [1, 2])]);
        var f = Run(Load("uniform", [-10]), mesh);
        Assert.Equal(-20, f["1"][2], 12);
        Assert.Equal(-20, f["2"][2], 12);
        Assert.Equal(10 * 16 / 12.0, f["1"][4], 12);
        Assert.Equal(-10 * 16 / 12.0, f["2"][4], 12);
    }

    [Fact]
    public void BarPoint_EquilibriumAboutNodeI()
    {
        var mesh = new FemLoadMeshContext([N(1, 0, 0), N(2, 3, 4)], [E("1", "beam", [1, 2])]);
        var f = Run(Load("point", [-100, 1.5]), mesh); // L = 5, точка в 1,5 м от I
        Assert.Equal(-100, f["1"][2] + f["2"][2], 9);
        // Момент сил и моментов относительно I равен r × P, r = 0,3·(3, 4, 0).
        double rx = 0.9, ry = 1.2;
        double myLoad = -rx * -100, mxLoad = ry * -100;
        double mx = f["1"][3] + f["2"][3] + 4 * f["2"][2];
        double my = f["1"][4] + f["2"][4] - 3 * f["2"][2];
        Assert.Equal(mxLoad, mx, 9);
        Assert.Equal(myLoad, my, 9);
    }

    [Fact]
    public void BarLocalTransverse_IsSkippedNotGuessed()
    {
        var diag = new List<FemValidationDiagnostic>();
        var mesh = new FemLoadMeshContext([N(1, 0, 0), N(2, 4, 0)], [E("1", "beam", [1, 2])]);
        var f = Run(Load("uniform", [-10], cs: "local", axis: "y"), mesh, diag);
        Assert.Empty(f);
        Assert.Contains(diag, d => d.Code == "element_load_skipped" && d.Message.Contains("местные оси"));
    }

    [Fact]
    public void SelfWeight_ShellAndBar()
    {
        var mesh = new FemLoadMeshContext([N(1, 0, 0), N(2, 2, 0), N(3, 0, 3), N(4, 2, 3), N(5, 2, 3, -3)],
            [E("1", "shell", [1, 2, 3, 4], 0.2), E("2", "beam", [4, 5])], null, new UnitWeights(25000, 0.16));
        var lc = new FemLoadCase { Id = 1, Tag = "СВ", SelfWeightFactor = 1.1 };
        var r = FemLoadCaseNodalForces.Resolve(lc, [], [], mesh);
        // Плита 25000·0,2·6 = 30 кН, колонна 25000·0,16·3 = 12 кН; ×1,1.
        Assert.Equal(-1.1 * (30000 + 12000), r.Total.Fz, 6);
        Assert.Empty(r.Diagnostics);
    }

    [Fact]
    public void SelfWeightLoad_WithoutUnitWeight_IsReported()
    {
        var r = FemLoadCaseNodalForces.Resolve(new FemLoadCase { Id = 1, Tag = "L1" },
            [Load("self_weight", [1])], [], Rect());
        Assert.Empty(r.Forces);
        Assert.Contains(r.Diagnostics, d => d.Code == "element_load_skipped");
    }

    [Fact]
    public void Targets_MembersGroupsAndMissing()
    {
        var nodes = new[] { N(1, 0, 0), N(2, 1, 0), N(3, 0, 1), N(4, 1, 1), N(5, 2, 0), N(6, 2, 1) };
        var elements = new[] { E("10", "shell", [1, 2, 3, 4], member: "P1"), E("11", "shell", [2, 5, 4, 6], member: "P1") };
        var meshGroup = new FemMemberGroup { Id = 7, Kind = FemMemberGroup.KindMesh };
        meshGroup.SetTags(["11"]);
        var memberGroup = new FemMemberGroup { Id = 8, Kind = FemMemberGroup.KindMembers };
        memberGroup.SetTags(["P1"]);
        var mesh = new FemLoadMeshContext(nodes, elements, [meshGroup, memberGroup]);

        var byMember = Load("uniform", [-1], target: "P1");
        byMember.TargetKind = FemLoadTargetKinds.Members;
        Assert.Equal(-2, Total(byMember, mesh), 9);

        var byMeshGroup = new FemElementLoad { LoadCaseId = 1, TargetKind = FemLoadTargetKinds.Group, GroupId = 7 };
        byMeshGroup.SetValues([-1]);
        Assert.Equal(-1, Total(byMeshGroup, mesh), 9);

        var byMemberGroup = new FemElementLoad { LoadCaseId = 1, TargetKind = FemLoadTargetKinds.Group, GroupId = 8 };
        byMemberGroup.SetValues([-1]);
        Assert.Equal(-2, Total(byMemberGroup, mesh), 9);

        var diag = new List<FemValidationDiagnostic>();
        Run(Load("uniform", [-1], target: "10,99"), mesh, diag);
        Assert.Contains(diag, d => d.Code == "element_load_element_missing" && d.SourceKeys!.Contains("99"));

        var noGroup = new FemElementLoad { LoadCaseId = 1, TargetKind = FemLoadTargetKinds.Group, GroupId = 3 };
        noGroup.SetValues([-1]);
        diag.Clear();
        Run(noGroup, mesh, diag);
        Assert.Contains(diag, d => d.Code == "element_load_group_missing");
    }

    [Fact]
    public void LoadCase_CombinesFactorsAndMeshNodeLoads()
    {
        var mesh = Rect();
        var lc1 = new FemLoadCase { Id = 1, Tag = "L1" };
        var lc2 = new FemLoadCase { Id = 2, Tag = "L2" };
        var load = Load("uniform", [-1000]);
        var nodeLoad = new FemMeshNodeLoad { LoadCaseId = 2, MeshNodeTag = "4", Fz = -500 };
        var missing = new FemMeshNodeLoad { LoadCaseId = 2, MeshNodeTag = "77", Fz = -1 };
        var r = FemLoadCaseNodalForces.Resolve([(lc1, 1.2), (lc2, 0.5)], [load], [nodeLoad, missing], mesh);
        Assert.Equal(-1.2 * 6000 - 0.5 * 500, r.Total.Fz, 9);
        Assert.Contains(r.Diagnostics, d => d.Code == "mesh_node_load_node_missing");
    }

    [Fact]
    public void Glyphs_SignPruningLabelAndSelfWeight()
    {
        // Полоса из 10 КЭ 1 × 1 м.
        var nodes = new List<FemMeshNode>();
        for (int i = 0; i <= 10; i++) { nodes.Add(N(2 * i + 1, i, 0)); nodes.Add(N(2 * i + 2, i, 1)); }
        var elements = Enumerable.Range(0, 10)
            .Select(i => E((100 + i).ToString(), "shell", [2 * i + 1, 2 * i + 3, 2 * i + 2, 2 * i + 4], 0.2)).ToArray();
        var mesh = new FemLoadMeshContext(nodes, elements, null, new UnitWeights(25000, 0));
        var load = Load("uniform", [-3000], target: string.Join(",", elements.Select(e => e.ElemTag)));
        var lc = new FemLoadCase { Id = 1, SelfWeightFactor = 1 };

        var set = FemElementLoadGlyphs.Build([(lc, 2)], [load], mesh, maxArrowsPerLoad: 4);
        var pressure = set.Arrows.Where(a => a.Magnitude == 6000).ToList();
        Assert.Equal(4, pressure.Count);                       // шаг 3: КЭ 0, 3, 6, 9
        Assert.All(pressure, a => Assert.Equal(-1, a.Direction.Z, 12));
        Assert.Equal(1, pressure[0].SizeM, 9);
        Assert.Equal(2, set.Labels.Count);                     // с. в. и давление
        Assert.Contains(set.Labels, l => l.Kind == "self_weight" && Math.Abs(l.Magnitude - 2 * 5000) < 1e-9);
        Assert.Equal(10, set.LoadedElementTags.Count);
    }

    static double Total(FemElementLoad load, FemLoadMeshContext mesh) => Run(load, mesh).Values.Sum(v => v[2]);
}
