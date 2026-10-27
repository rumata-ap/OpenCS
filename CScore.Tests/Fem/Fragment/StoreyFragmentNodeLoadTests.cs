using CScore.Fem;
using Xunit;
using static CScore.Tests.Fem.Fragment.StoreyFragmentLoadFixtures;

namespace CScore.Tests.Fem.Fragment;

public sealed class StoreyFragmentNodeLoadTests
{
    // Сетка плиты 4×4: узел 1001 + j·5 + i в (−3 + 1,5·i, −3 + 1,5·j, 3).
    [Fact]
    public void MeshNodeLoads_InBand_BecomeNodeLoads_SummedPerNode()
    {
        var d = Build(Snapshot(meshNodeLoads:
        [
            new FemMeshNodeLoad { LoadCaseId = 7, MeshNodeTag = "1007", Fz = -1000 },
            new FemMeshNodeLoad { LoadCaseId = 7, MeshNodeTag = "1007", Fz = -500, Mx = 20 },
            new FemMeshNodeLoad { LoadCaseId = 7, MeshNodeTag = "101", Fz = -9999 },   // колонна, z = 0 — вне полосы
        ]));

        var nl = Assert.Single(d.NodeLoads);
        var node = d.Nodes.Single(n => n.NodeTag == nl.NodeTag);
        Assert.Equal((-1.5, -1.5, 3.0), (node.X, node.Y, node.Z));
        Assert.Equal((7, -1500.0, 20.0), (nl.Load.LoadCaseId, nl.Load.Fz, nl.Load.Mx));
    }

    [Fact]
    public void SchemaNodeLoadsAndKinematics_ByParentNodeId()
    {
        var s = Snapshot(
            nodeLoads: [new FemNodeLoad { LoadCaseId = 7, NodeId = 11, Fx = 300 }, new FemNodeLoad { LoadCaseId = 7, NodeId = 12, Fx = 1 }],
            kinematicLoads: [new FemKinematicLoad { LoadCaseId = 7, NodeId = 11, Dof = 3, Value = -0.002 }]);
        var nodes = s.Nodes.ToList();
        nodes.Add(new FemNode { Id = 11, NodeTag = "11", X = 1, Y = 2, Z = 3 });
        nodes.Add(new FemNode { Id = 12, NodeTag = "12", X = 1, Y = 2, Z = 5 });   // вне полосы
        var d = Build(new CScore.Fem.Fragment.StoreyFragmentParentSnapshot
        {
            Nodes = nodes, Members = s.Members, Regions = s.Regions, MeshNodes = s.MeshNodes, MeshElements = s.MeshElements,
            LoadCases = s.LoadCases, NodeLoads = s.NodeLoads, KinematicLoads = s.KinematicLoads,
        });

        var nl = Assert.Single(d.NodeLoads);
        var node = d.Nodes.Single(n => n.NodeTag == nl.NodeTag);
        Assert.Equal((1.0, 2.0, 3.0, 300.0), (node.X, node.Y, node.Z, nl.Load.Fx));
        var k = Assert.Single(d.KinematicLoads);
        Assert.Equal((nl.NodeTag, 3, -0.002), (k.NodeTag, k.Load.Dof, k.Load.Value));
    }
}
