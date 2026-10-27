using CScore.Fem;
using CScore.Fem.Fragment;
using Xunit;
using static CScore.Tests.Fem.Fragment.StoreyFragmentLoadFixtures;

namespace CScore.Tests.Fem.Fragment;

/// <summary>Пружины и закрепления узлов сетки родителя во фрагменте (срез 4).</summary>
public sealed class StoreyFragmentBoundaryTests
{
    static StoreyFragmentParentSnapshot With(StoreyFragmentParentSnapshot s, FemSpring[] springs, FemMeshNodeSupport[] supports,
        FemNode[]? extraNodes = null) => new()
    {
        Nodes = [.. s.Nodes, .. extraNodes ?? []], Members = s.Members, Regions = s.Regions, MeshNodes = s.MeshNodes,
        MeshElements = s.MeshElements, LoadCases = s.LoadCases, Springs = springs, MeshNodeSupports = supports,
    };

    static FemSpring Spring(string kind, string tag, double kz, double kx = 0) =>
        new() { TargetKind = kind, NodeTag = tag, Kz = kz, Kx = kx, Origin = "import:scad", SourceElemTag = "51" };

    // Сетка плиты 4×4: узел 1001 + j·5 + i в (−3 + 1,5·i, −3 + 1,5·j, 3); колонна — узлы 101…105 по z 0…6.
    [Fact]
    public void MeshNodeSprings_BecomeSchemaNodeSprings_SummedPerNode()
    {
        var d = Build(With(Snapshot(),
        [
            Spring(FemSpringTargetKinds.MeshNode, "1007", 2e6),
            Spring(FemSpringTargetKinds.MeshNode, "1007", 1e6, kx: 5e5),
            Spring(FemSpringTargetKinds.MeshNode, "101", 9e9),          // колонна, z = 0 — вне полосы
            Spring(FemSpringTargetKinds.MeshNode, "1008", 0),           // нулевая — не переносится
        ], []));

        var s = Assert.Single(d.Springs);
        Assert.Equal(FemSpringTargetKinds.Node, s.TargetKind);
        var node = d.Nodes.Single(n => n.NodeTag == s.NodeTag);
        Assert.Equal((-1.5, -1.5, 3.0), (node.X, node.Y, node.Z));
        Assert.Equal((3e6, 5e5, "import:scad", "51"), (s.Kz, s.Kx, s.Origin, s.SourceElemTag));
    }

    [Fact]
    public void SchemaNodeSpring_InBand_Transferred()
    {
        var d = Build(With(Snapshot(), [Spring(FemSpringTargetKinds.Node, "11", 4e6), Spring(FemSpringTargetKinds.Node, "12", 1)], [],
            [new FemNode { Id = 11, NodeTag = "11", X = 1, Y = 2, Z = 3 }, new FemNode { Id = 12, NodeTag = "12", X = 1, Y = 2, Z = 5 }]));

        var s = Assert.Single(d.Springs);
        var node = d.Nodes.Single(n => n.NodeTag == s.NodeTag);
        Assert.Equal((1.0, 2.0, 3.0, 4e6), (node.X, node.Y, node.Z, s.Kz));
    }

    [Fact]
    public void MeshNodeSupports_InBand_BecomeDofMask()
    {
        var d = Build(With(Snapshot(), [],
        [
            new FemMeshNodeSupport { NodeTag = "1013", Mask = 7 },
            new FemMeshNodeSupport { NodeTag = "1014", Mask = 0 },
            new FemMeshNodeSupport { NodeTag = "105", Mask = 63 },      // колонна, z = 6 — вне полосы
        ]));

        Assert.Empty(d.Springs);
        var node = Assert.Single(d.Nodes, n => n.X == 0 && n.Y == 0 && n.Z == 3);
        Assert.Equal(7, node.DofMask & 7);
        Assert.DoesNotContain(d.Nodes, n => n.X == 1.5 && n.Y == 0 && n.Z == 3);
        Assert.DoesNotContain(d.Nodes, n => n.Z == 6);
    }

    [Fact]
    public void Fingerprint_SeesSpringsAndSupports()
    {
        var a = With(Snapshot(), [Spring(FemSpringTargetKinds.MeshNode, "1007", 2e6)], []);
        var b = With(Snapshot(), [Spring(FemSpringTargetKinds.MeshNode, "1007", 3e6)], []);
        var c = With(Snapshot(), [Spring(FemSpringTargetKinds.MeshNode, "1007", 2e6)], [new FemMeshNodeSupport { NodeTag = "1013", Mask = 7 }]);

        Assert.Equal(StoreyFragmentFingerprint.Compute(a), StoreyFragmentFingerprint.Compute(With(Snapshot(), [Spring(FemSpringTargetKinds.MeshNode, "1007", 2e6)], [])));
        Assert.NotEqual(StoreyFragmentFingerprint.Compute(a), StoreyFragmentFingerprint.Compute(b));
        Assert.NotEqual(StoreyFragmentFingerprint.Compute(a), StoreyFragmentFingerprint.Compute(c));
    }
}
