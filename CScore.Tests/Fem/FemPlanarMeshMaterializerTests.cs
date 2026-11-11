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

    /// <summary>Пошаговый префикс после каждой области — то же, что материализация этих областей целиком (без проверки
    /// висящих узлов); прежний результат не меняется при добавлении следующей области.</summary>
    [Fact]
    public void Prefix_MatchesMaterializeAfterEachRegion()
    {
        var (nodes, meshNodes, elements) = Frame();
        FemPlanarRegionMesh[] regions = [Region("P1", 0, 0, H, 4, 4, 1.0, 0.2), Region("P2", 4, 0, H, 2, 4, 1.0, 0.3)];
        static string Key(FemPlanarMaterializationResult r) =>
            string.Join("|", r.Nodes.Select(n => $"{n.NodeTag}:{n.X},{n.Y},{n.Z}:{n.SourceNodeTag}")) + "#" +
            string.Join("|", r.Elements.Select(e => $"{e.ElemTag}:{e.ElemType}:{e.NodeIdsJson}:{e.SourceMemberTag}:{e.ThicknessM}")) + "#" +
            r.SharedNodeCount + "," + r.SplitBeamCount;

        var prefix = new FemPlanarMeshMaterializer.Prefix(1, meshNodes, elements, nodes);
        Assert.Equal(Key(FemPlanarMeshMaterializer.Materialize(1, meshNodes, elements, nodes, [])), Key(prefix.Build()));
        prefix.Add(regions[0]);
        var first = prefix.Build();
        var firstKey = Key(first);
        Assert.Equal(Key(FemPlanarMeshMaterializer.Materialize(1, meshNodes, elements, nodes, regions[..1])), firstKey);
        prefix.Add(regions[1]);
        Assert.Equal(Key(FemPlanarMeshMaterializer.Materialize(1, meshNodes, elements, nodes, regions)), Key(prefix.Build()));
        Assert.Equal(firstKey, Key(first));
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
    public void ImportedBeamWithInnerShellNode_StrictIsError()
    {
        var meshNodes = new List<FemMeshNode>
        {
            new() { NodeTag = "1", X = 0, Y = 0, Z = 0, Origin = FemMember.MeshSourceImported },
            new() { NodeTag = "2", X = 2, Y = 0, Z = 0, Origin = FemMember.MeshSourceImported },
        };
        var beam = new FemElement
        {
            ElemTag = "1", ElemType = "beam", NodeIdsJson = "[1,2]", Origin = FemMember.MeshSourceImported, SourceMemberTag = "B",
        };

        var result = FemPlanarMeshMaterializer.Materialize(1, meshNodes, [beam], [], [Region("P", 0, 0, 0, 2, 2, 1.0, 0.2)],
            strictImported: true);

        Assert.Contains(result.Diagnostics, d => d.Code == "planar_materialize_imported_beam_inner_node" && d.IsError);
        Assert.Equal(["B"], result.ImportedMembersAtFault);
    }

    /// <summary>
    /// Два совпадающих узла сохранённой сетки у концов стержней «A» и «B» (паук ЛИРЫ «Мирной») и одна точка новой плиты:
    /// общим становится один, второй — «не стал общим»; его КонЭ — виновный для лестницы перемешивания.
    /// </summary>
    [Fact]
    public void CoincidentImportedNodes_PointNotShared_OwnerAtFault()
    {
        var meshNodes = new List<FemMeshNode>
        {
            new() { NodeTag = "1", X = 1, Y = 1, Z = 0, Origin = FemMember.MeshSourceImported, SourceMemberTag = "A" },
            new() { NodeTag = "2", X = 1, Y = 1, Z = 0, Origin = FemMember.MeshSourceImported, SourceMemberTag = "B" },
            new() { NodeTag = "3", X = 1, Y = 1, Z = -1, Origin = FemMember.MeshSourceImported, SourceMemberTag = "A" },
            new() { NodeTag = "4", X = 1, Y = 1, Z = 1, Origin = FemMember.MeshSourceImported, SourceMemberTag = "B" },
        };
        FemElement Beam(string tag, string member, string ids) =>
            new() { ElemTag = tag, ElemType = "beam", NodeIdsJson = ids, Origin = FemMember.MeshSourceImported, SourceMemberTag = member };
        var plate = Region("P", 0, 0, 0, 2, 2, 1.0, 0.2);
        var snapshot = new PlanarMeshSnapshot
        {
            RegionId = 1, IsCalculable = true, Nodes = plate.Snapshot.Nodes, Elements = plate.Snapshot.Elements,
            ConstraintMappings =
            [
                new PlanarConstraintMeshMapping
                {
                    ConstraintObjectId = "derived:point", PointNodeIndices = [4],
                    SourceReferences = [new PlanarSourceReference(0, "A", [], [], [], ["1", "2"])],
                },
            ],
        };

        var result = FemPlanarMeshMaterializer.Materialize(1, meshNodes, [Beam("10", "A", "[1,3]"), Beam("11", "B", "[2,4]")], [],
            [new FemPlanarRegionMesh(plate.Member, plate.Region, snapshot, 0.2)], strictImported: true);

        var d = Assert.Single(result.Diagnostics, x => x.Code == "planar_materialize_point_not_shared");
        Assert.True(d.IsError);
        var missed = Assert.Single(d.SourceKeys!);
        Assert.Contains(missed == "1" ? "A" : "B", result.ImportedMembersAtFault);
    }

    /// <summary>Импортная плита 2×2 (Q4 1 м) и новая справа шагом 0,5: узлы новой внутри рёбер импортной.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportedSlab_NewRegionFinerOnEdge_ImportedHangingNode(bool strict)
    {
        var (meshNodes, elements) = ImportedSlab("PI", 0, 0, 0, 2, 2, 1.0);

        var result = FemPlanarMeshMaterializer.Materialize(1, meshNodes, elements, [], [Region("PN", 2, 0, 0, 4, 4, 0.5, 0.2)],
            strictImported: strict);

        var d = Assert.Single(result.Diagnostics, x => x.Code == "planar_materialize_imported_hanging_node");
        Assert.Equal(strict, d.IsError);
        Assert.Contains("PI", d.SourceKeys!);
        Assert.Contains("PN", d.SourceKeys!);
        Assert.Equal(["PI"], result.ImportedMembersAtFault);
    }

    /// <summary>Новая плита крупнее импортной (1 м против 0,5): узлы импортной внутри рёбер новой.</summary>
    [Fact]
    public void ImportedSlab_NewRegionCoarserOnEdge_ImportedHangingNode()
    {
        var (meshNodes, elements) = ImportedSlab("PI", 0, 0, 0, 4, 4, 0.5);

        var result = FemPlanarMeshMaterializer.Materialize(1, meshNodes, elements, [], [Region("PN", 2, 0, 0, 2, 2, 1.0, 0.2)],
            strictImported: true);

        Assert.Contains(result.Diagnostics, x => x.Code == "planar_materialize_imported_hanging_node" && x.IsError);
        Assert.Equal(["PI"], result.ImportedMembersAtFault);
    }

    /// <summary>Сохранённая балка по кромке сохранённой плиты одним КЭ на два КЭ плиты: в строгом режиме — нарушение
    /// балки (её и перемешивать), в нестрогом — как раньше, без диагностики.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportedBeamOverImportedSlabNode_StrictFaultsBeam(bool strict)
    {
        var (meshNodes, elements) = ImportedSlab("PI", 0, 0, 0, 2, 2, 1.0);
        elements.Add(new FemElement
        {
            ElemTag = "3001", ElemType = "beam", NodeIdsJson = "[1001,1003]", Origin = FemMember.MeshSourceImported, SourceMemberTag = "BI",
        });

        var result = FemPlanarMeshMaterializer.Materialize(1, meshNodes, elements, [], [], strictImported: strict);

        if (strict)
        {
            Assert.Contains(result.Diagnostics, d => d.Code == "planar_materialize_imported_beam_inner_node" && d.IsError);
            Assert.Equal(["BI"], result.ImportedMembersAtFault);
        }
        else
        {
            Assert.DoesNotContain(result.Diagnostics, d => d.Code == "planar_materialize_imported_beam_inner_node");
            Assert.Empty(result.ImportedMembersAtFault);
        }
    }

    /// <summary>Сгенерированный стержень по кромке сохранённой плиты в строгом режиме делится по её узлам.</summary>
    [Fact]
    public void GeneratedBeamOverImportedSlabNode_StrictSplit()
    {
        var (meshNodes, elements) = ImportedSlab("PI", 0, 0, 0, 2, 2, 1.0);
        elements.Add(new FemElement { ElemType = "beam", NodeIdsJson = "[1001,1003]", Origin = FemMember.MeshSourceGenerated, SourceMemberTag = "B" });

        var result = FemPlanarMeshMaterializer.Materialize(1, meshNodes, elements, [], [], strictImported: true);

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.Equal(1, result.SplitBeamCount);
        var beams = result.Elements.Where(e => e.SourceMemberTag == "B").Select(e => e.NodeIdsJson).ToList();
        Assert.Equal(["[1001,1002]", "[1002,1003]"], beams);
    }

    [Fact]
    public void ImportedSlab_MatchingEdge_NoDiagnostics()
    {
        var (meshNodes, elements) = ImportedSlab("PI", 0, 0, 0, 2, 2, 1.0);

        var result = FemPlanarMeshMaterializer.Materialize(1, meshNodes, elements, [], [Region("PN", 2, 0, 0, 2, 2, 1.0, 0.2)],
            strictImported: true);

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.Empty(result.ImportedMembersAtFault);
        Assert.Equal(3, result.SharedNodeCount);
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

    /// <summary>Импортная плита nx×ny ячеек шагом step в плоскости z: Q4 «1 2 4 3», теги узлов с 1001, КЭ с 2001.</summary>
    static (List<FemMeshNode> Nodes, List<FemElement> Elements) ImportedSlab(string tag, double x0, double y0, double z,
        int nx, int ny, double step)
    {
        var nodes = new List<FemMeshNode>();
        int NodeTag(int i, int j) => 1001 + j * (nx + 1) + i;
        for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++)
                nodes.Add(new FemMeshNode
                {
                    NodeTag = NodeTag(i, j).ToString(), X = x0 + i * step, Y = y0 + j * step, Z = z,
                    Origin = FemMember.MeshSourceImported, SourceMemberTag = tag,
                });
        var elements = new List<FemElement>();
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
                elements.Add(new FemElement
                {
                    ElemTag = (2001 + elements.Count).ToString(), ElemType = "shell", SourceMemberTag = tag,
                    Origin = FemMember.MeshSourceImported, ThicknessM = 0.2,
                    NodeIdsJson = JsonSerializer.Serialize(new[] { NodeTag(i, j), NodeTag(i + 1, j), NodeTag(i, j + 1), NodeTag(i + 1, j + 1) }),
                });
        return (nodes, elements);
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
