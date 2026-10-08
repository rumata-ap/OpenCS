using System.Text.Json;
using CScore.Fem;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>Материализация снимков областей в сетку схемы (CSfea 4г, срез 2).</summary>
public class FemPlanarMeshMaterializerTests
{
    const double H = 3.0;

    /// <summary>Плита 4×4 м (Q4 1 м) на четырёх угловых колоннах + балка по кромке y = 0 одним КЭ.</summary>
    static (List<FemNode> Nodes, List<FemMeshNode> MeshNodes, List<FemElement> Elements) Frame()
    {
        var nodes = new List<FemNode>
        {
            Node("1", 0, 0, 0), Node("2", 4, 0, 0), Node("3", 4, 4, 0), Node("4", 0, 4, 0),
            Node("5", 0, 0, H), Node("6", 4, 0, H), Node("7", 4, 4, H), Node("8", 0, 4, H),
        };
        var members = new List<FemMember>
        {
            Bar("C1", 1, 5), Bar("C2", 2, 6), Bar("C3", 3, 7), Bar("C4", 4, 8), Bar("B1", 5, 6),
        };
        var (meshNodes, elements) = FemMeshDiscretizer.Discretize(1, nodes, members, null);
        return (nodes, meshNodes, elements);
    }

    [Fact]
    public void SlabOnColumnsWithEdgeBeam_SharedNodesAndSplitBeam()
    {
        var (nodes, meshNodes, elements) = Frame();
        var slab = Region("P1", 0, 0, H, 4, 4, 1.0, 0.2);

        var result = FemPlanarMeshMaterializer.Materialize(1, meshNodes, elements, nodes, [slab]);

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.Equal(8 + 21, result.Nodes.Count);
        Assert.Equal(4, result.SharedNodeCount);
        Assert.Equal(1, result.SplitBeamCount);
        var beams = result.Elements.Where(e => e.ElemType == "beam").ToList();
        var shells = result.Elements.Where(e => e.ElemType == "shell").ToList();
        Assert.Equal(4 + 4, beams.Count);
        Assert.Equal(16, shells.Count);
        Assert.All(beams.Where(b => b.SourceMemberTag == "B1"), b => Assert.Equal(1.0, Length(b, result.Nodes), 9));
        Assert.All(shells, s =>
        {
            Assert.Equal("P1", s.SourceMemberTag);
            Assert.Equal(FemMember.MeshSourceGenerated, s.Origin);
            Assert.Equal(0.2, s.ThicknessM);
        });
        // Узлы колонн (теги 5…8) — углы плиты.
        var corner = shells.Single(s => Ids(s).Contains(5));
        Assert.Contains(5, Ids(corner));
        // Теги КЭ: стержни подряд, затем пластины.
        Assert.Equal(Enumerable.Range(1, 24).Select(i => i.ToString()), result.Elements.Select(e => e.ElemTag));
    }

    [Fact]
    public void Quad_StoredAsLiraOrder_1243()
    {
        var (nodes, meshNodes, elements) = Frame();
        var result = FemPlanarMeshMaterializer.Materialize(1, meshNodes, elements, nodes, [Region("P1", 0, 0, H, 4, 4, 1.0, 0.2)]);
        var byTag = result.Nodes.ToDictionary(n => n.NodeTag);

        var first = result.Elements.First(e => e.ElemType == "shell");
        var p = Ids(first).Select(id => byTag[id.ToString()]).ToArray();
        // Контур (0,0) → (1,0) → (1,1) → (0,1) хранится как «1 2 4 3».
        Assert.Equal((0.0, 0.0), (p[0].X, p[0].Y));
        Assert.Equal((1.0, 0.0), (p[1].X, p[1].Y));
        Assert.Equal((0.0, 1.0), (p[2].X, p[2].Y));
        Assert.Equal((1.0, 1.0), (p[3].X, p[3].Y));
    }

    [Fact]
    public void RepeatedRun_SameTagsAndConnectivity()
    {
        var first = Run();
        var second = Run();
        Assert.Equal(first.Nodes.Select(n => (n.NodeTag, n.X, n.Y, n.Z)), second.Nodes.Select(n => (n.NodeTag, n.X, n.Y, n.Z)));
        Assert.Equal(first.Elements.Select(e => (e.ElemTag, e.NodeIdsJson)), second.Elements.Select(e => (e.ElemTag, e.NodeIdsJson)));

        static FemPlanarMaterializationResult Run()
        {
            var (nodes, meshNodes, elements) = Frame();
            return FemPlanarMeshMaterializer.Materialize(1, meshNodes, elements, nodes, [Region("P1", 0, 0, H, 4, 4, 1.0, 0.2)]);
        }
    }

    [Fact]
    public void AdjacentSlabs_MatchingEdge_SharedWithoutErrors()
    {
        var a = Region("A", 0, 0, 0, 2, 2, 1.0, 0.2);
        var b = Region("B", 2, 0, 0, 2, 2, 1.0, 0.2);

        var result = FemPlanarMeshMaterializer.Materialize(1, [], [], [], [a, b]);

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.Equal(3, result.SharedNodeCount);
        Assert.Equal(9 + 6, result.Nodes.Count);
    }

    [Fact]
    public void AdjacentSlabs_DifferentEdgeDivision_HangingNodeError()
    {
        var a = Region("A", 0, 0, 0, 2, 2, 1.0, 0.2);
        var b = Region("B", 2, 0, 0, 4, 4, 0.5, 0.2);

        var result = FemPlanarMeshMaterializer.Materialize(1, [], [], [], [a, b]);

        var error = Assert.Single(result.Diagnostics, d => d.Code == "planar_materialize_hanging_node");
        Assert.True(error.IsError);
        Assert.Contains("«A»", error.Message);
        Assert.Contains("«B»", error.Message);
    }

    [Fact]
    public void ImportedBeamWithInnerShellNode_NotSplitWarning()
    {
        var meshNodes = new List<FemMeshNode>
        {
            new() { NodeTag = "1", X = 0, Y = 0, Z = 0, Origin = FemMember.MeshSourceImported },
            new() { NodeTag = "2", X = 2, Y = 0, Z = 0, Origin = FemMember.MeshSourceImported },
        };
        var beam = new FemElement { ElemTag = "1", NodeIdsJson = "[1,2]", Origin = FemMember.MeshSourceImported };

        var result = FemPlanarMeshMaterializer.Materialize(1, meshNodes, [beam], [], [Region("P", 0, 0, 0, 2, 2, 1.0, 0.2)]);

        var warning = Assert.Single(result.Diagnostics, d => d.Code == "planar_materialize_imported_beam_inner_node");
        Assert.False(warning.IsError);
        Assert.Single(result.Elements, e => e.ElemType == "beam");
        Assert.Equal("1", result.Elements[0].ElemTag);
        Assert.Equal("2", result.Elements[1].ElemTag);
    }

    [Fact]
    public void FreeFemNodeInSlab_BecomesSourceNodeTag_AndMissingThicknessWarns()
    {
        var free = Node("50", 1, 1, 0);
        var result = FemPlanarMeshMaterializer.Materialize(1, [], [], [free], [Region("P", 0, 0, 0, 2, 2, 1.0, null)]);

        var center = result.Nodes.Single(n => n.X == 1 && n.Y == 1);
        Assert.Equal("50", center.SourceNodeTag);
        Assert.NotEqual("50", center.NodeTag);
        Assert.Contains(result.Diagnostics, d => d.Code == "planar_materialize_thickness_missing" && !d.IsError);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void NotCalculableSnapshot_Error()
    {
        var region = Region("P", 0, 0, 0, 2, 2, 1.0, 0.2);
        var broken = region with { Snapshot = new PlanarMeshSnapshot { RegionId = 1, IsCalculable = false } };

        var result = FemPlanarMeshMaterializer.Materialize(1, [], [], [], [broken]);

        Assert.Contains(result.Diagnostics, d => d.Code == "planar_materialize_snapshot_invalid" && d.IsError);
    }

    /// <summary>Горизонтальная плита nx × ny Q4 с шагом step от (x0, y0) на отметке z; обход КЭ против часовой.</summary>
    static FemPlanarRegionMesh Region(string tag, double x0, double y0, double z, int nx, int ny, double step, double? thickness)
    {
        var nodes = new List<PlanarMeshNode>();
        for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++)
                nodes.Add(new PlanarMeshNode(nodes.Count, i * step, j * step, x0 + i * step, y0 + j * step, z));
        var elements = new List<PlanarMeshElement>();
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int n0 = j * (nx + 1) + i;
                elements.Add(new PlanarMeshElement(elements.Count, PlanarMeshElementKind.Quadrangle4,
                    [n0, n0 + 1, n0 + nx + 2, n0 + nx + 1]));
            }
        var snapshot = new PlanarMeshSnapshot { RegionId = 1, IsCalculable = true, Nodes = nodes, Elements = elements };
        var member = new FemMember { ElemTag = tag, ElemType = "shell", PlanarRegionId = 1 };
        return new FemPlanarRegionMesh(member, new PlanarRegion { Id = 1, Tag = tag }, snapshot, thickness);
    }

    static FemNode Node(string tag, double x, double y, double z) => new() { NodeTag = tag, X = x, Y = y, Z = z };

    static FemMember Bar(string tag, int a, int b) => new() { ElemTag = tag, ElemType = "beam", NodeIdsJson = $"[{a},{b}]" };

    static int[] Ids(FemElement e) => JsonSerializer.Deserialize<int[]>(e.NodeIdsJson)!;

    static double Length(FemElement e, List<FemMeshNode> nodes)
    {
        var ids = Ids(e);
        var a = nodes.Single(n => n.NodeTag == ids[0].ToString());
        var b = nodes.Single(n => n.NodeTag == ids[1].ToString());
        return Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));
    }
}
