using CScore.Fem;
using Xunit;

namespace CScore.Tests.Fem;

public sealed class FemIsolatedSupportNodesTests
{
    static FemNode N(string tag, double x, double y, double z, int dof = 0) => new() { NodeTag = tag, X = x, Y = y, Z = z, DofMask = dof };

    static FemSpring NodeSpring(string tag, double kz) => new()
    {
        TargetKind = FemSpringTargetKinds.Node, NodeTag = tag, Kz = kz,
    };

    [Fact]
    public void SupportedNodeWithoutElements_GetsMeshNodeAtItsPoint()
    {
        var meshNodes = new List<FemMeshNode> { new() { NodeTag = "10", X = 0, Y = 0, Z = 0, SourceNodeTag = "1" } };
        var nodes = new[] { N("1", 0, 0, 0), N("2", 3, 4, 4.4, dof: 3) };

        int added = FemIsolatedSupportNodes.Add(meshNodes, nodes, [], 7);

        Assert.Equal(1, added);
        var created = meshNodes.Single(n => n.SourceNodeTag == "2");
        Assert.Equal("11", created.NodeTag);
        Assert.Equal((3.0, 4.0, 4.4), (created.X, created.Y, created.Z));
        Assert.Equal(7, created.SchemaId);
    }

    [Fact]
    public void SpringOnlyNode_GetsMeshNode_InactiveSpringDoesNot()
    {
        var meshNodes = new List<FemMeshNode>();
        var nodes = new[] { N("5", 1, 1, 2), N("6", 2, 2, 2) };
        var springs = new[] { NodeSpring("5", 1e6), NodeSpring("6", 0) };

        Assert.Equal(1, FemIsolatedSupportNodes.Add(meshNodes, nodes, springs, 1));
        Assert.Equal("5", Assert.Single(meshNodes).SourceNodeTag);
    }

    [Fact]
    public void NodeAlreadyMapped_OrCoincidentWithUnmappedMeshNode_NotDuplicated()
    {
        var meshNodes = new List<FemMeshNode>
        {
            new() { NodeTag = "10", X = 1, Y = 1, Z = 2, SourceNodeTag = "5" },
            // Узел сетки пластины в той же точке, без источника: становится узлом схемы, дубля нет.
            new() { NodeTag = "11", X = 2, Y = 2, Z = 2 },
        };
        var nodes = new[] { N("5", 1, 1, 2, dof: 63), N("6", 2, 2, 2, dof: 63) };

        Assert.Equal(0, FemIsolatedSupportNodes.Add(meshNodes, nodes, [], 1));
        Assert.Equal(2, meshNodes.Count);
        Assert.Equal("6", meshNodes.Single(n => n.NodeTag == "11").SourceNodeTag);
    }

    [Fact]
    public void NodeWithoutSupportOrSpring_Ignored()
    {
        var meshNodes = new List<FemMeshNode>();
        var nodes = new[] { N("1", 0, 0, 0), N("2", 5, 5, 5) };

        Assert.Equal(0, FemIsolatedSupportNodes.Add(meshNodes, nodes, [], 1));
        Assert.Empty(meshNodes);
    }
}
