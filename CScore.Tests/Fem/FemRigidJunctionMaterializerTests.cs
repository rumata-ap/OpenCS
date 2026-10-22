using CScore.Fem;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>Тела АЖТ из следов на синтетическом снимке сетки (без Gmsh): плита 2×2 в плоскости z = 0, сетка 0,5 м.</summary>
public sealed class FemRigidJunctionMaterializerTests
{
    const double Step = 0.5;

    static readonly Dictionary<int, IReadOnlyList<(double Y, double Z)>> Outlines = new()
    {
        // Колонна 1×1 м — след накрывает узлы 0,5…1,5 вокруг центра.
        [1] = [(-0.5, -0.5), (0.5, -0.5), (0.5, 0.5), (-0.5, 0.5)],
        // Ребро шириной 1 м (±0,5 поперёк оси в плоскости плиты), высота 0,3.
        [2] = [(-0.15, -0.5), (0.15, -0.5), (0.15, 0.5), (-0.15, 0.5)],
    };

    static PlanarRegion Slab()
    {
        var slab = PlanarRegion.CreateFromContour(new Contour { X = [0, 2, 2, 0], Y = [0, 0, 2, 2] }, frame: Frame3D.Identity, tag: "П");
        slab.Id = 1;
        slab.MeshMaxElementSizeM = Step;
        return slab;
    }

    static FemMember Plate() => new() { Id = 1, ElemTag = "П", ElemType = "shell", PlanarRegionId = 1, NodeIdsJson = "[]" };

    static FemMember Bar(string tag, int i, int j, int section) =>
        new() { ElemTag = tag, ElemType = "beam", NodeIdsJson = $"[{i},{j}]", CrossSectionId = section };

    /// <summary>Снимок: узлы сетки 5×5 (+ дополнительные), КЭ Q4; отображения Region — узлы в куске следа и на его контуре.</summary>
    static PlanarMeshSnapshot Snapshot(RigidJunctionTraceSet traces, params (double U, double V)[] extra)
    {
        var points = new List<(double U, double V)>();
        for (int j = 0; j <= 4; j++)
            for (int i = 0; i <= 4; i++) points.Add((i * Step, j * Step));
        points.AddRange(extra);
        var nodes = points.Select((p, k) => new PlanarMeshNode(k, p.U, p.V, p.U, p.V, 0)).ToList();
        var elements = new List<PlanarMeshElement>();
        for (int j = 0; j < 4; j++)
            for (int i = 0; i < 4; i++)
            {
                int a = j * 5 + i;
                elements.Add(new PlanarMeshElement(elements.Count, PlanarMeshElementKind.Quadrangle4, [a, a + 1, a + 6, a + 5]));
            }
        var mappings = traces.ToConstraints().Select(c => new PlanarConstraintMeshMapping
        {
            ConstraintObjectId = c.Id,
            RegionNodeIndices = nodes.Where(n => PlanarPolygonOps.Contains(c.Geometry.Points, new PlanarPoint2D(n.U, n.V), 1e-9))
                .Select(n => n.Index).ToArray(),
        }).ToList();
        return new PlanarMeshSnapshot { RegionId = 1, IsCalculable = true, Nodes = nodes, Elements = elements, ConstraintMappings = mappings };
    }

    static (FemPlanarMaterializationResult Mesh, FemRigidJunctionResult Result) Run(
        FemNode[] nodes, FemMember[] bars, IReadOnlySet<string>? blocked = null, params (double U, double V)[] extra)
    {
        var region = Slab();
        var members = bars.Append(Plate()).ToArray();
        var traces = FemRigidJunctions.Build(region, nodes, members, id => Outlines.GetValueOrDefault(id));
        var (beamNodes, beamElements) = FemMeshDiscretizer.Discretize(1, nodes, bars, null);
        var regionMesh = new FemPlanarRegionMesh(Plate(), region, Snapshot(traces, extra), 0.2, traces);
        var mesh = FemPlanarMeshMaterializer.Materialize(1, beamNodes, beamElements, nodes, [regionMesh]);
        Assert.False(mesh.HasErrors, string.Join("\n", mesh.Diagnostics));
        return (mesh, FemRigidJunctionMaterializer.Build(mesh, [regionMesh], nodes, members, blocked ?? new HashSet<string>()));
    }

    static string TagAt(FemPlanarMaterializationResult mesh, double x, double y) =>
        mesh.Nodes.Single(n => Math.Abs(n.X - x) < 1e-9 && Math.Abs(n.Y - y) < 1e-9 && Math.Abs(n.Z) < 1e-9).NodeTag;

    static HashSet<string> TagsAt(FemPlanarMaterializationResult mesh, params (double X, double Y)[] points) =>
        points.Select(p => TagAt(mesh, p.X, p.Y)).ToHashSet();

    static (double, double)[] Square(double c0, double c1, (double, double)? except = null)
    {
        var result = new List<(double, double)>();
        for (double x = c0; x <= c1 + 1e-9; x += Step)
            for (double y = c0; y <= c1 + 1e-9; y += Step)
                if (except is not { } e || Math.Abs(e.Item1 - x) > 1e-9 || Math.Abs(e.Item2 - y) > 1e-9) result.Add((x, y));
        return result.ToArray();
    }

    [Fact]
    public void Column_MasterBySourceNode_SlavesAreTraceNodes()
    {
        var (mesh, result) = Run([new() { NodeTag = "1", X = 1, Y = 1, Z = -3 }, new() { NodeTag = "2", X = 1, Y = 1, Z = 0 }],
            [Bar("К1", 1, 2, section: 1)]);

        var body = Assert.Single(result.Bodies);
        Assert.Equal(mesh.Nodes.Single(n => n.SourceNodeTag == "2").NodeTag, body.MasterNodeTag);
        Assert.Equal(TagsAt(mesh, Square(0.5, 1.5, except: (1, 1))), body.SlaveNodeTags.ToHashSet());
        Assert.Equal(RigidJunctionTraceSet.Provenance, body.Origin);
        Assert.Equal("К1", body.SourceElemTag);
        Assert.Equal(FemBoundaryDofs.All, body.Mask);
        Assert.Equal(FemRigidBodyKinds.Rigid, body.Kind);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Column_BlockedSlaveExcludedWithWarning_MaskAndKindFromMember()
    {
        var nodes = new FemNode[] { new() { NodeTag = "1", X = 1, Y = 1, Z = -3 }, new() { NodeTag = "2", X = 1, Y = 1, Z = 0 } };
        var bar = Bar("К1", 1, 2, section: 1);
        bar.RigidJunctionMask = 7;
        bar.RigidJunctionKind = FemMember.RigidJunctionCoupled;
        var probe = Run(nodes, [bar]);
        var blockedTag = TagAt(probe.Mesh, 0.5, 0.5);

        var (_, result) = Run(nodes, [bar], new HashSet<string> { blockedTag });

        var body = Assert.Single(result.Bodies);
        Assert.Equal(7, body.SlaveNodeTags.Count);
        Assert.DoesNotContain(blockedTag, body.SlaveNodeTags);
        Assert.Equal(7, body.Mask);
        Assert.Equal(FemRigidBodyKinds.Coupled, body.Kind);
        Assert.Contains(result.Diagnostics, d => d.Code == "rigid_junction_slave_blocked" && !d.IsError);
    }

    [Fact]
    public void Rib_BodyPerStation_SlavesToNearestStation_TieToLowerStation()
    {
        // Ребро вдоль y = 1 от x = 0 до x = 2: станции — узлы оси (стержень дробится узлами плиты через 0,5).
        // Узел (0,25; 0,6) на равном расстоянии от станций x = 0 и x = 0,5 — к меньшей.
        var (mesh, result) = Run([new() { NodeTag = "1", X = 0, Y = 1, Z = 0 }, new() { NodeTag = "2", X = 2, Y = 1, Z = 0 }],
            [Bar("Р1", 1, 2, section: 2)], extra: (0.25, 0.6));

        Assert.Equal(5, result.Bodies.Count);
        foreach (var x in new[] { 0.0, 0.5, 1.0, 1.5, 2.0 })
        {
            var body = Assert.Single(result.Bodies, b => b.MasterNodeTag == TagAt(mesh, x, 1));
            var expected = TagsAt(mesh, (x, 0.5), (x, 1.5));
            if (x == 0) expected.Add(TagAt(mesh, 0.25, 0.6));
            Assert.Equal(expected, body.SlaveNodeTags.ToHashSet());
            Assert.Equal("Р1", body.SourceElemTag);
        }
    }

    [Fact]
    public void RibThroughColumn_StationsInsideColumnTrace_MergeIntoColumnBody()
    {
        var nodes = new FemNode[]
        {
            new() { NodeTag = "1", X = 1, Y = 1, Z = -3 }, new() { NodeTag = "2", X = 1, Y = 1, Z = 0 },
            new() { NodeTag = "3", X = 0, Y = 1, Z = 0 }, new() { NodeTag = "4", X = 2, Y = 1, Z = 0 },
        };
        var (mesh, result) = Run(nodes, [Bar("К1", 1, 2, section: 1), Bar("Р1", 3, 2, section: 2), Bar("Р2", 2, 4, section: 2)]);

        var master = mesh.Nodes.Single(n => n.SourceNodeTag == "2").NodeTag;
        var column = Assert.Single(result.Bodies, b => b.MasterNodeTag == master);
        Assert.Equal(TagsAt(mesh, Square(0.5, 1.5, except: (1, 1))), column.SlaveNodeTags.ToHashSet());
        Assert.Equal("К1,Р1,Р2", column.SourceElemTag);
        // Станции у кромок — свои тела ребра; ни один узел не ведом дважды, ни один ведущий не ведом.
        Assert.Equal(3, result.Bodies.Count);
        Assert.Equal(TagsAt(mesh, (0, 0.5), (0, 1.5)), result.Bodies.Single(b => b.MasterNodeTag == TagAt(mesh, 0, 1)).SlaveNodeTags.ToHashSet());
        var slaves = result.Bodies.SelectMany(b => b.SlaveNodeTags).ToList();
        Assert.Equal(slaves.Count, slaves.Distinct().Count());
        Assert.DoesNotContain(result.Bodies, b => slaves.Contains(b.MasterNodeTag));
    }

    [Fact]
    public void BlockedNodes_SupportsSpringsLineAndForeignBodies()
    {
        var nodes = new FemNode[] { new() { NodeTag = "1", DofMask = 7 }, new() { NodeTag = "2" } };
        var mesh = new FemMeshNode[]
        {
            new() { NodeTag = "10", SourceNodeTag = "1" }, new() { NodeTag = "11", SourceNodeTag = "2" },
            new() { NodeTag = "12" }, new() { NodeTag = "13" }, new() { NodeTag = "14" }, new() { NodeTag = "15" }, new() { NodeTag = "16" },
        };
        var spring = new FemSpring { TargetKind = FemSpringTargetKinds.Node, NodeTag = "2" };
        spring.SetStiffnesses([1, 0, 0, 0, 0, 0]);
        var meshSpring = new FemSpring { TargetKind = FemSpringTargetKinds.MeshNode, NodeTag = "13" };
        meshSpring.SetStiffnesses([0, 0, 1, 0, 0, 0]);
        var foreign = new FemRigidBody { MasterNodeTag = "14", Origin = FemLoadOrigin.Manual };
        foreign.SetSlaveNodeTags(["15"]);
        var own = new FemRigidBody { MasterNodeTag = "16", Origin = RigidJunctionTraceSet.Provenance };

        var blocked = FemRigidJunctionMaterializer.BlockedNodes(nodes, mesh,
            [new FemMeshNodeSupport { NodeTag = "12", Mask = 1 }, new FemMeshNodeSupport { NodeTag = "16", Mask = 0 }],
            [spring, meshSpring], [foreign, own]);

        Assert.Equal(new HashSet<string> { "10", "11", "12", "13", "14", "15" }, blocked);
    }
}
