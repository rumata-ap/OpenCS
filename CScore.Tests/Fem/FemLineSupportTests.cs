using System.Text.Json;
using CScore.Fem;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>Линейные связи на гранях пластинчатых КонЭ: грани контура, опознание, раскладка на узлы сетки.</summary>
public sealed class FemLineSupportTests
{
    /// <summary>Пластина 4 × 2 м в плоскости XY, отверстие 1 × 0,5 м.</summary>
    static (FemMember Member, PlanarRegion Region) Plate(Frame3D? frame = null)
    {
        var region = new PlanarRegion
        {
            Id = 1,
            Contours =
            [
                new Contour([0, 4, 4, 0, 0], [0, 0, 2, 2, 0], "P") { Type = ContourType.Hull },
                new Contour([1, 1, 2, 2, 1], [0.5, 1, 1, 0.5, 0.5], "H") { Type = ContourType.Hole },
            ],
            Frame = frame ?? Frame3D.Identity,
        };
        return (new FemMember { ElemTag = "P", ElemType = "shell", PlanarRegionId = 1 }, region);
    }

    static FemMeshNode Node(string tag, double x, double y, double z = 0) => new() { NodeTag = tag, X = x, Y = y, Z = z };

    static FemElement Shell(string tag, string member, params int[] nodes) => new()
    {
        ElemTag = tag, ElemType = "shell", SourceMemberTag = member, NodeIdsJson = JsonSerializer.Serialize(nodes),
    };

    static FemLineSupport On(FemPlateEdge edge, int mask = 0, double kz = 0)
    {
        var s = new FemLineSupport { Id = 5, Mask = mask, Kz = kz };
        s.AssignEdge(edge);
        return s;
    }

    /// <summary>Нижняя грань y = 0 с узлами x = 0, 1, 3, 4 (неравномерная цепочка) и верхний ряд — КЭ «P».</summary>
    static (List<FemMeshNode> Nodes, List<FemElement> Elements) StripMesh()
    {
        var nodes = new List<FemMeshNode>
        {
            Node("1", 0, 0), Node("2", 1, 0), Node("3", 3, 0), Node("4", 4, 0),
            Node("5", 0, 2), Node("6", 1, 2), Node("7", 3, 2), Node("8", 4, 2),
        };
        var elements = new List<FemElement>
        {
            Shell("1", "P", 1, 2, 5, 6), Shell("2", "P", 2, 3, 6, 7), Shell("3", "P", 3, 4, 7, 8),
        };
        return (nodes, elements);
    }

    [Fact]
    public void Edges_OuterAndHole_InGlobalAxes()
    {
        var frame = new Frame3D(new PlanarVector3(10, 0, 5), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 0, 1),
            new PlanarVector3(0, -1, 0));
        var (member, region) = Plate(frame);

        var edges = FemLineSupportGeometry.Edges(member, region);

        Assert.Equal(8, edges.Count);
        Assert.Equal(4, edges.Count(e => e.Key.Loop == BoundaryLoop.Outer));
        var second = edges.Single(e => e.Key == new FemPlateEdgeKey("P", BoundaryLoop.Outer, 0, 1));
        Assert.Equal(new PlanarVector3(14, 0, 5), second.A);
        Assert.Equal(new PlanarVector3(14, 0, 7), second.B);
        Assert.Equal(2, second.Length, 12);
        Assert.Equal(0.5, edges.Single(e => e.Key == new FemPlateEdgeKey("P", BoundaryLoop.Hole, 0, 0)).Length, 12);
        Assert.Equal("P, отверстие 1, сторона 1", new FemPlateEdgeKey("P", BoundaryLoop.Hole, 0, 0).ToString());
    }

    [Fact]
    public void Match_ByKey_ThenByEnds_ElseLost()
    {
        var (member, region) = Plate();
        var edges = FemLineSupportGeometry.Edges(member, region);
        var support = On(edges[2]); // верхняя грань (4,2) → (0,2)

        Assert.Same(edges[2], FemLineSupportGeometry.Match(support, edges));

        // Контур перенумерован: начало с вершины (0,2) — та же грань стала стороной 1, ключ не совпадает.
        region.Contours[0] = new Contour([4, 0, 0, 4, 4], [2, 2, 0, 0, 2], "P") { Type = ContourType.Hull };
        var renumbered = FemLineSupportGeometry.Edges(member, region);
        var found = FemLineSupportGeometry.Match(support, renumbered);
        Assert.NotNull(found);
        Assert.Equal(0, found!.Key.StartVertex);

        // Грань исчезла (контур изменён).
        region.Contours[0] = new Contour([0, 5, 5, 0, 0], [0, 0, 3, 3, 0], "P") { Type = ContourType.Hull };
        Assert.Null(FemLineSupportGeometry.Match(support, FemLineSupportGeometry.Edges(member, region)));
    }

    [Fact]
    public void Chain_Tributary_SumsToLength()
    {
        var (member, region) = Plate();
        var bottom = FemLineSupportGeometry.Edges(member, region)[0];
        var (nodes, _) = StripMesh();
        var byTag = nodes.ToDictionary(n => n.NodeTag);

        var chain = FemLineSupportResolver.Chain(bottom, byTag.Keys, byTag, out bool gap);

        Assert.False(gap);
        Assert.Equal(new[] { "1", "2", "3", "4" }, chain.Select(c => c.NodeTag));
        Assert.Equal(new[] { 0.5, 1.5, 1.5, 0.5 }, chain.Select(c => c.Tributary));
        Assert.Equal(4, chain.Sum(c => c.Tributary), 12);
    }

    [Fact]
    public void ToNodal_RigidAndElastic()
    {
        var (member, region) = Plate();
        var edges = FemLineSupportGeometry.EdgesByMember([member], [region]);
        var (nodes, elements) = StripMesh();
        var bottom = edges["P"][0];
        var hinge = On(bottom, mask: 0b111);
        var elastic = On(edges["P"][1], kz: 5e6); // правая грань x = 4: узлы 4 и 8
        elastic.Id = 6;

        var r = FemLineSupportResolver.ToNodal([hinge, elastic], edges, nodes, elements);

        Assert.Empty(r.Diagnostics);
        Assert.Equal(new[] { "1", "2", "3", "4" }, r.Supports.Select(s => s.NodeTag));
        Assert.All(r.Supports, s => Assert.Equal(0b111, s.Mask));
        Assert.All(r.Supports, s => Assert.Equal("line:5", s.Origin));
        Assert.Equal(new[] { "4", "8" }, r.Springs.Select(s => s.NodeTag));
        Assert.Equal(5e6 * 2, r.Springs.Sum(s => s.Kz), 6); // Σ = k′·L
        Assert.Equal(2, r.Chains.Count);

        // Общий путь резолвера: угол 4 — и закрепление, и пружина; пружина по закреплённому Z отбрасывается.
        var (supports, springs) = r.AppendTo([], []);
        var resolved = FemBoundaryResolver.Resolve([], nodes, elements, supports, springs, []);
        Assert.Equal(0b111, resolved.SupportMasks["4"]);
        Assert.Equal(new[] { "8" }, resolved.Springs.Select(s => s.MeshNodeTag));
        Assert.Contains(resolved.Diagnostics, d => d.Code == "spring_in_fixed_dof");
    }

    [Fact]
    public void ToNodal_Diagnostics()
    {
        var (member, region) = Plate();
        var edges = FemLineSupportGeometry.EdgesByMember([member], [region]);
        var (nodes, elements) = StripMesh();

        var lost = On(edges["P"][0], mask: 4);
        lost.X1 = 99;
        var noMesh = On(edges["P"][0], mask: 4);
        var gap = On(edges["P"][4], mask: 4); // грань отверстия — узлов сетки на ней нет

        var r = FemLineSupportResolver.ToNodal([lost], edges, nodes, elements);
        Assert.Equal(new[] { "line_support_edge_lost" }, r.Diagnostics.Select(d => d.Code));

        r = FemLineSupportResolver.ToNodal([noMesh], edges, nodes, []);
        Assert.Equal(new[] { "line_support_no_mesh" }, r.Diagnostics.Select(d => d.Code));

        r = FemLineSupportResolver.ToNodal([gap], edges, nodes, elements);
        Assert.Equal(new[] { "line_support_gap" }, r.Diagnostics.Select(d => d.Code));
        Assert.True(r.Diagnostics[0].IsError);
        Assert.Empty(r.Supports);
    }

    [Fact]
    public void ToNodal_OnlyOwnMemberNodes()
    {
        var (member, region) = Plate();
        var edges = FemLineSupportGeometry.EdgesByMember([member], [region]);
        var (nodes, elements) = StripMesh();
        // Чужой КЭ с узлом посередине грани: узел не из сетки «P» — не закрепляется.
        nodes.Add(Node("9", 2, 0));
        nodes.Add(Node("10", 2, -1));
        elements.Add(Shell("9", "Q", 9, 10, 3, 2));

        var r = FemLineSupportResolver.ToNodal([On(edges["P"][0], mask: 4)], edges, nodes, elements);

        Assert.DoesNotContain(r.Supports, s => s.NodeTag == "9");
    }

    [Fact]
    public void SetStiffnesses_DropsFixedDofs()
    {
        var s = new FemLineSupport { Mask = 0b100 };
        s.SetStiffnesses([1, 2, 3, 4, 5, 6]);
        Assert.Equal(new double[] { 1, 2, 0, 4, 5, 6 }, s.Stiffnesses);
        Assert.Equal(0b111011, s.SpringMask);
        Assert.True(s.IsActive);
        Assert.False(new FemLineSupport().IsActive);
    }

    [Fact]
    public void EdgeRotationBit_ByEdgeAndPlateNormal()
    {
        // Плита в XY: грань вдоль X — поворот вокруг Y, вдоль Y — вокруг X.
        var (member, region) = Plate();
        var edges = FemLineSupportGeometry.Edges(member, region);
        Assert.Equal(1 << 4, FemLineSupportGeometry.EdgeRotationBit(edges[0]));
        Assert.Equal(1 << 3, FemLineSupportGeometry.EdgeRotationBit(edges[1]));

        // Стена в XZ, низ вдоль X — поворот вокруг Z (в плоскости стены).
        var wall = new Frame3D(PlanarVector3.Zero, new(1, 0, 0), new(0, 0, 1), new(0, -1, 0));
        var (wm, wr) = Plate(wall);
        Assert.Equal(1 << 5, FemLineSupportGeometry.EdgeRotationBit(FemLineSupportGeometry.Edges(wm, wr)[0]));

        // Наклонная грань — оси нет; при раскладке — предупреждение, поступательные связи остаются.
        var skew = new FemPlateEdge(new FemPlateEdgeKey("P", BoundaryLoop.Outer, 0, 0), new(0, 0, 0), new(1, 1, 0), new(0, 0, 1));
        Assert.Null(FemLineSupportGeometry.EdgeRotationBit(skew));
        var nodes = new List<FemMeshNode> { Node("1", 0, 0), Node("2", 1, 1), Node("3", 0, 1) };
        var s = new FemLineSupport { Mask = 0b100, FixEdgeRotation = true };
        s.AssignEdge(skew);
        var r = FemLineSupportResolver.ToNodal([s], new Dictionary<string, List<FemPlateEdge>> { ["P"] = [skew] }, nodes,
            [Shell("1", "P", 1, 2, 3)]);
        Assert.Equal(new[] { "line_support_edge_rotation_skew" }, r.Diagnostics.Select(d => d.Code));
        Assert.All(r.Supports, x => Assert.Equal(0b100, x.Mask));
    }
}
