using System.Text.Json;
using CScore.Fem;
using Xunit;

namespace CScore.Tests;

public sealed class FemMeshDiscretizerTests
{
    [Fact]
    public void PlainBeamProducesOneElementAndTwoSourceNodes()
    {
        var nodes = Nodes((1, "1", 0), (2, "2", 3));
        var members = new[] { Member("7", "[1,2]") };

        var mesh = FemMeshDiscretizer.Discretize(4, nodes, members, null);

        Assert.Equal(2, mesh.Nodes.Count);
        Assert.Single(mesh.Elements);
        Assert.All(mesh.Nodes, node => Assert.True(int.Parse(node.NodeTag) > 0));
        Assert.Equal("[1,2]", mesh.Elements[0].NodeIdsJson);
    }

    [Fact]
    public void ExistingNodeOnBeamIsEmbeddedAndSplitsElement()
    {
        var nodes = Nodes((1, "1", 0), (2, "2", 10), (3, "3", 4));
        var members = new[] { Member("7", "[1,2]") };

        var mesh = FemMeshDiscretizer.Discretize(4, nodes, members, null);

        Assert.Equal(3, mesh.Nodes.Count);
        Assert.Equal(2, mesh.Elements.Count);
        Assert.Equal("[1,3]", mesh.Elements[0].NodeIdsJson);
        Assert.Equal("[3,2]", mesh.Elements[1].NodeIdsJson);
        Assert.Contains(mesh.Nodes, node => node.SourceNodeTag == "3");
    }

    [Fact]
    public void EmbeddedNodesWithinToleranceAreSortedByPosition()
    {
        var nodes = new[]
        {
            new FemNode { Id = 1, NodeTag = "1", X = 0, Y = 0 },
            new FemNode { Id = 2, NodeTag = "2", X = 10, Y = 0 },
            new FemNode { Id = 3, NodeTag = "3", X = 7, Y = 0.0009 },
            new FemNode { Id = 4, NodeTag = "4", X = 3, Y = -0.0009 }
        };

        var mesh = FemMeshDiscretizer.Discretize(4, nodes, [Member("7", "[1,2]")], null);

        Assert.Equal(3, mesh.Elements.Count);
        Assert.Equal("[1,4]", mesh.Elements[0].NodeIdsJson);
        Assert.Equal("[4,3]", mesh.Elements[1].NodeIdsJson);
        Assert.Equal("[3,2]", mesh.Elements[2].NodeIdsJson);
    }

    [Fact]
    public void EmbeddedNodeNearEndpointDoesNotCreateShortSpan()
    {
        var nodes = Nodes((1, "1", 0), (2, "2", 1), (3, "3", 0.0005));

        var mesh = FemMeshDiscretizer.Discretize(4, nodes, [Member("7", "[1,2]")], null);

        Assert.Single(mesh.Elements);
        Assert.Equal(2, mesh.Nodes.Count);
        Assert.DoesNotContain(mesh.Nodes, node => node.SourceNodeTag == "3");
        Assert.Equal("[1,2]", mesh.Elements[0].NodeIdsJson);
    }

    [Fact]
    public void TargetSubdivisionDoesNotCreateSpansShorterThanTolerance()
    {
        var nodes = Nodes((1, "1", 0), (2, "2", 0.0025));

        var mesh = FemMeshDiscretizer.Discretize(4, nodes, [Member("7", "[1,2]")], 0.0008);

        Assert.Equal(2, mesh.Elements.Count);
        Assert.Equal(3, mesh.Nodes.Count);
        Assert.All(mesh.Elements, element => Assert.True(ElementLength(element, mesh.Nodes) >= FemMeshDiscretizer.CollinearToleranceM));
    }

    [Fact]
    public void ExtremelySmallTargetLengthUsesToleranceBoundWithoutOverflow()
    {
        var nodes = Nodes((1, "1", 0), (2, "2", 0.0025));

        var mesh = FemMeshDiscretizer.Discretize(4, nodes, [Member("7", "[1,2]")], 1e-10);

        Assert.Equal(2, mesh.Elements.Count);
        Assert.All(mesh.Elements, element => Assert.True(ElementLength(element, mesh.Nodes) >= FemMeshDiscretizer.CollinearToleranceM));
    }

    [Fact]
    public void DefaultTargetLengthSubdividesIntoEqualPartsWithNegativeSyntheticTags()
    {
        var nodes = Nodes((1, "1", 0), (2, "2", 6));
        var members = new[] { Member("7", "[1,2]") };

        var mesh = FemMeshDiscretizer.Discretize(4, nodes, members, 2);

        Assert.Equal(4, mesh.Nodes.Count);
        Assert.Equal(3, mesh.Elements.Count);
        var syntheticNodes = mesh.Nodes.Where(node => node.SourceNodeTag is null).ToArray();
        Assert.Equal(2, syntheticNodes.Length);
        Assert.All(syntheticNodes, node => Assert.True(int.Parse(node.NodeTag) < 0));
        Assert.Equal(new[] { 2d, 4d }, syntheticNodes.Select(node => node.X).ToArray());
    }

    [Fact]
    public void MemberTargetLengthOverridesDefaultTargetLength()
    {
        var nodes = Nodes((1, "1", 0), (2, "2", 10));
        var members = new[] { Member("7", "[1,2]", targetLength: 4) };

        var mesh = FemMeshDiscretizer.Discretize(4, nodes, members, 3);

        Assert.Equal(3, mesh.Elements.Count);
        Assert.Equal(2, mesh.Nodes.Count(node => node.SourceNodeTag is null));
    }

    [Fact]
    public void NonPositiveMemberTargetLengthSuppressesDefaultSubdivision()
    {
        var nodes = Nodes((1, "1", 0), (2, "2", 10));
        var members = new[] { Member("7", "[1,2]", targetLength: 0) };

        var mesh = FemMeshDiscretizer.Discretize(4, nodes, members, 2);

        Assert.Single(mesh.Elements);
        Assert.Equal(2, mesh.Nodes.Count);
    }

    [Fact]
    public void ZeroLengthBeamIsSkipped()
    {
        var nodes = Nodes((1, "1", 2), (2, "2", 2));
        var members = new[] { Member("7", "[1,2]") };

        var mesh = FemMeshDiscretizer.Discretize(4, nodes, members, null);

        Assert.Empty(mesh.Nodes);
        Assert.Empty(mesh.Elements);
    }

    [Fact]
    public void CrossingBeamsWithoutSharedNodeRemainDisconnected()
    {
        var nodes = new[]
        {
            new FemNode { Id = 1, NodeTag = "1", X = -1, Y = 0 },
            new FemNode { Id = 2, NodeTag = "2", X = 1, Y = 0 },
            new FemNode { Id = 3, NodeTag = "3", X = 0, Y = -1 },
            new FemNode { Id = 4, NodeTag = "4", X = 0, Y = 1 }
        };
        var members = new[]
        {
            Member("7", "[1,2]"),
            Member("8", "[3,4]")
        };

        var mesh = FemMeshDiscretizer.Discretize(4, nodes, members, null);

        Assert.Equal(4, mesh.Nodes.Count);
        Assert.Equal(2, mesh.Elements.Count);
        var first = NodeTags(mesh.Elements[0]);
        var second = NodeTags(mesh.Elements[1]);
        Assert.Empty(first.Intersect(second));
    }

    [Fact]
    public void MultipleMembersReuseSourceNodesAndKeepGeneratedTagsUnique()
    {
        var nodes = Nodes((1, "1", 0), (2, "2", 5), (3, "3", 10));
        var members = new[]
        {
            Member("7", "[1,2]", targetLength: 2),
            Member("8", "[2,3]", targetLength: 2)
        };

        var mesh = FemMeshDiscretizer.Discretize(4, nodes, members, null);

        Assert.Equal(7, mesh.Nodes.Count);
        Assert.Equal(6, mesh.Elements.Count);
        Assert.Equal(mesh.Nodes.Count, mesh.Nodes.Select(node => node.NodeTag).Distinct().Count());
        Assert.Equal(mesh.Elements.Count, mesh.Elements.Select(element => element.ElemTag).Distinct().Count());
        Assert.Single(mesh.Nodes, node => node.SourceNodeTag == "2");
        var sharedNodeTag = int.Parse(mesh.Nodes.Single(node => node.SourceNodeTag == "2").NodeTag);
        Assert.Contains(mesh.Elements.Take(3), element => NodeTags(element).Contains(sharedNodeTag));
        Assert.Contains(mesh.Elements.Skip(3), element => NodeTags(element).Contains(sharedNodeTag));
    }

    [Fact]
    public void ElementsPropagateMemberTagAndSectionMetadata()
    {
        var nodes = Nodes((1, "1", 0), (2, "2", 5));
        var members = new[]
        {
            Member("member-7", "[1,2]", targetLength: 2,
                crossSectionId: 11, gjStrategy: "saint_venant", gjManualValue: 12.5,
                torsionTaskId: 13)
        };

        var mesh = FemMeshDiscretizer.Discretize(9, nodes, members, null);

        Assert.Equal(3, mesh.Elements.Count);
        Assert.All(mesh.Elements, element =>
        {
            Assert.Equal(9, element.SchemaId);
            Assert.Equal("member-7", element.SourceMemberTag);
            Assert.Equal(11, element.CrossSectionId);
            Assert.Equal("saint_venant", element.GjStrategy);
            Assert.Equal(12.5, element.GjManualValue);
            Assert.Equal(13, element.GjTorsionTaskId);
            Assert.True(int.Parse(element.ElemTag) > 0);
        });
    }

    [Fact]
    public void NonBeamMalformedAndMissingMembersAreSkipped()
    {
        var nodes = Nodes((1, "1", 0), (2, "2", 1));
        var members = new[]
        {
            Member("1", "[1,2]", elemType: "shell"),
            Member("2", "[1]"),
            Member("3", "[1,99]"),
            Member("4", "not-json")
        };

        var mesh = FemMeshDiscretizer.Discretize(4, nodes, members, null);

        Assert.Empty(mesh.Nodes);
        Assert.Empty(mesh.Elements);
    }

    static FemNode[] Nodes(params (int Id, string Tag, double X)[] sourceNodes) =>
        sourceNodes.Select(node => new FemNode
        {
            Id = node.Id,
            NodeTag = node.Tag,
            X = node.X,
            SchemaId = 4
        }).ToArray();

    static FemMember Member(
        string elemTag,
        string nodeIdsJson,
        double? targetLength = null,
        string elemType = "beam",
        int? crossSectionId = null,
        string gjStrategy = "manual",
        double? gjManualValue = null,
        int? torsionTaskId = null) => new()
        {
            ElemTag = elemTag,
            ElemType = elemType,
            NodeIdsJson = nodeIdsJson,
            TargetMeshLengthM = targetLength,
            CrossSectionId = crossSectionId,
            GjStrategy = gjStrategy,
            GjManualValue = gjManualValue,
            GjTorsionTaskId = torsionTaskId
        };

    static int[] NodeTags(FemElement element) =>
        JsonSerializer.Deserialize<int[]>(element.NodeIdsJson)!;

    static double ElementLength(FemElement element, IReadOnlyList<FemMeshNode> nodes)
    {
        var tags = NodeTags(element);
        var first = nodes.Single(node => int.Parse(node.NodeTag) == tags[0]);
        var second = nodes.Single(node => int.Parse(node.NodeTag) == tags[1]);
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        var dz = second.Z - first.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
