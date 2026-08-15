using System.Text.Json;
using CScore.Fem;
using OpenCS.OpenSees.Structural;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки построения preview набора усилий по mesh-цепочке.</summary>
public class FemMemberForceSetBuilderTests
{
    [Fact]
    public void Build_TwoSequentialElements_ReturnsThreeUniqueNodesAndCumulativePositions()
    {
        var result = Build(ElementsInOrder(101, 102));

        Assert.True(result.IsSuccess);
        Assert.Equal(["10", "20", "30"], result.Preview!.Rows.Select(r => r.MeshNodeTag));
        Assert.Equal([0.0, 2.0, 5.0], result.Preview.Rows.Select(r => r.PositionS));
        Assert.Equal(101, result.Preview.Rows[0].SelectedCandidate.ElementTag);
        Assert.Equal(1, result.Preview.Rows.Count(r => r.LeftCandidate is not null && r.RightCandidate is not null));
    }

    [Fact]
    public void Build_ReversedElementOrderAndLocalNodeOrder_PreservesMemberDirectionAndSigns()
    {
        var result = Build(
            ElementsInOrder(102, 101),
            reverseLocalOrderForElement: 102);

        Assert.True(result.IsSuccess);
        Assert.Equal(["10", "20", "30"], result.Preview!.Rows.Select(r => r.MeshNodeTag));
        Assert.Equal(-1.0, result.Preview.Rows[0].SelectedCandidate.Values.N / 1000.0, 12);
        Assert.Equal(6.0, result.Preview.Rows[0].SelectedCandidate.Values.Mz / 1000.0, 12);
    }

    [Fact]
    public void Build_InternalNode_ExposesTwoCandidatesAndDefaultsToLeft()
    {
        var row = Assert.Single(Build(ElementsInOrder(101, 102)).Preview!.Rows,
            r => r.MeshNodeTag == "20");

        Assert.NotNull(row.LeftCandidate);
        Assert.NotNull(row.RightCandidate);
        Assert.Equal(FemForceSourceSide.Left, row.SelectedSource);
        Assert.Equal(row.LeftCandidate!.Values, row.SelectedCandidate.Values);
    }

    [Fact]
    public void Build_OneElement_ReturnsBothEndpointRows()
    {
        var result = Build(ElementsInOrder(101));

        Assert.True(result.IsSuccess);
        Assert.Equal(["10", "20"], result.Preview!.Rows.Select(r => r.MeshNodeTag));
        Assert.Equal([0.0, 2.0], result.Preview.Rows.Select(r => r.PositionS));
        Assert.All(result.Preview.Rows, row => Assert.Equal(FemForceSourceSide.Only, row.SelectedSource));
    }

    [Fact]
    public void Build_InternalSelectionDoesNotSumCandidates()
    {
        var row = Assert.Single(Build(ElementsInOrder(101, 102)).Preview!.Rows,
            r => r.MeshNodeTag == "20");

        Assert.NotEqual(
            row.LeftCandidate!.Values.N + row.RightCandidate!.Values.N,
            row.SelectedCandidate.Values.N);
    }

    [Fact]
    public void Build_UsesMeshSourceNodeWhenOriginalNodeIsMissing()
    {
        var result = Build(
            ElementsInOrder(101, 102),
            sourceNodes: [new FemNode { SchemaId = 1, NodeTag = "200", X = 5 }]);

        Assert.True(result.IsSuccess);
        Assert.Equal(["10", "20", "30"], result.Preview!.Rows.Select(r => r.MeshNodeTag));
    }

    [Fact]
    public void Build_FallsBackToDeterministicChainEndpointsWhenSourceTagsAreMissing()
    {
        var result = Build(
            ElementsInOrder(102, 101),
            meshNodes: DefaultMeshNodes().Select(node =>
                new FemMeshNode
                {
                    SchemaId = node.SchemaId,
                    NodeTag = node.NodeTag,
                    X = node.X,
                    Y = node.Y,
                    Z = node.Z
                }).ToArray());

        Assert.True(result.IsSuccess);
        Assert.Equal(["10", "20", "30"], result.Preview!.Rows.Select(r => r.MeshNodeTag));
    }

    [Fact]
    public void Build_AllowsSmallDeviationFromOriginalAxis()
    {
        var meshNodes = DefaultMeshNodes().ToArray();
        meshNodes[1].Y = 0.0001;

        var result = Build(ElementsInOrder(101, 102), meshNodes: meshNodes);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Build_SortsEqualSWithoutMergingDifferentNodes()
    {
        var meshNodes = new[]
        {
            new FemMeshNode { SchemaId = 1, NodeTag = "10", X = 0, SourceNodeTag = "100" },
            new FemMeshNode { SchemaId = 1, NodeTag = "20", X = 1 },
            new FemMeshNode { SchemaId = 1, NodeTag = "30", X = 1, Y = 1, SourceNodeTag = "200" }
        };

        var result = Build(ElementsInOrder(101, 102), meshNodes: meshNodes);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Preview!.Rows.Count);
        Assert.Equal(["10", "20", "30"], result.Preview.Rows.Select(row => row.MeshNodeTag));
    }

    [Fact]
    public void Build_RejectsMissingMeshNode()
    {
        var result = Build(
            [new FemElement { ElemTag = "101", SourceMemberTag = "M1", NodeIdsJson = "[10,99]" }]);

        Assert.Equal(FemMemberForceSetBuildError.MissingMeshNode, result.Error);
    }

    [Fact]
    public void Build_RejectsMissingForce()
    {
        var result = Build(ElementsInOrder(101, 102), forces: [
            new FemElementEndForces(101, 1000, 0, 0, 0, 0, 0, 1000, 0, 0, 0, 0, 0)]);

        Assert.Equal(FemMemberForceSetBuildError.MissingElementForce, result.Error);
    }

    [Fact]
    public void Build_RejectsNonFiniteForce()
    {
        var result = Build(ElementsInOrder(101), forces: [
            new FemElementEndForces(101, double.NaN, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)]);

        Assert.Equal(FemMemberForceSetBuildError.NonFiniteForce, result.Error);
    }

    [Fact]
    public void Build_RejectsDisconnectedChain()
    {
        var elements = new[]
        {
            new FemElement { ElemTag = "101", SourceMemberTag = "M1", NodeIdsJson = "[10,20]" },
            new FemElement { ElemTag = "103", SourceMemberTag = "M1", NodeIdsJson = "[40,50]" }
        };
        var meshNodes = DefaultMeshNodes().Concat([
            new FemMeshNode { SchemaId = 1, NodeTag = "40", X = 10 },
            new FemMeshNode { SchemaId = 1, NodeTag = "50", X = 11 }]).ToArray();

        var result = Build(elements, meshNodes: meshNodes);

        Assert.Equal(FemMemberForceSetBuildError.InvalidTopology, result.Error);
    }

    [Fact]
    public void Build_RejectsBranchingChain()
    {
        var elements = new[]
        {
            new FemElement { ElemTag = "101", SourceMemberTag = "M1", NodeIdsJson = "[10,20]" },
            new FemElement { ElemTag = "102", SourceMemberTag = "M1", NodeIdsJson = "[20,30]" },
            new FemElement { ElemTag = "103", SourceMemberTag = "M1", NodeIdsJson = "[20,40]" }
        };
        var meshNodes = DefaultMeshNodes().Append(
            new FemMeshNode { SchemaId = 1, NodeTag = "40", X = 2, Y = 1 }).ToArray();

        var result = Build(elements, meshNodes: meshNodes);

        Assert.Equal(FemMemberForceSetBuildError.InvalidTopology, result.Error);
    }

    [Fact]
    public void Build_RejectsRepeatedElement()
    {
        var result = Build(ElementsInOrder(101, 101));

        Assert.Equal(FemMemberForceSetBuildError.ReusedElement, result.Error);
    }

    [Fact]
    public void Build_RejectsDuplicateElementPair()
    {
        var result = Build([
            new FemElement { ElemTag = "101", SourceMemberTag = "M1", NodeIdsJson = "[10,20]" },
            new FemElement { ElemTag = "102", SourceMemberTag = "M1", NodeIdsJson = "[10,20]" }]);

        Assert.Equal(FemMemberForceSetBuildError.DuplicateElementPair, result.Error);
    }

    [Fact]
    public void Build_RejectsEqualElementNodes()
    {
        var result = Build([
            new FemElement { ElemTag = "101", SourceMemberTag = "M1", NodeIdsJson = "[10,10]" }]);

        Assert.Equal(FemMemberForceSetBuildError.EqualElementNodes, result.Error);
    }

    [Fact]
    public void Build_RejectsZeroLengthElement()
    {
        var meshNodes = DefaultMeshNodes().Append(
            new FemMeshNode { SchemaId = 1, NodeTag = "40", X = 0, Y = 0, Z = 0 }).ToArray();
        var result = Build([
            new FemElement { ElemTag = "101", SourceMemberTag = "M1", NodeIdsJson = "[10,40]" }],
            meshNodes: meshNodes);

        Assert.Equal(FemMemberForceSetBuildError.ZeroLengthElement, result.Error);
    }

    [Fact]
    public void Build_RejectsNotConvergedStep()
    {
        var result = Build(ElementsInOrder(101), stepConverged: false);

        Assert.Equal(FemMemberForceSetBuildError.NotConvergedStep, result.Error);
    }

    static FemMemberForceSetBuildResult Build(
        IReadOnlyList<FemElement> elements,
        int? reverseLocalOrderForElement = null,
        IReadOnlyList<FemElementEndForces>? forces = null,
        IReadOnlyList<FemNode>? sourceNodes = null,
        IReadOnlyList<FemMeshNode>? meshNodes = null,
        bool stepConverged = true)
    {
        var orderedElements = elements
            .Select(element =>
            {
                if (!int.TryParse(element.ElemTag, out int tag) || reverseLocalOrderForElement != tag)
                    return element;

                var ids = JsonSerializer.Deserialize<int[]>(element.NodeIdsJson)!;
                return new FemElement
                {
                    ElemTag = element.ElemTag,
                    SourceMemberTag = element.SourceMemberTag,
                    NodeIdsJson = JsonSerializer.Serialize(new[] { ids[1], ids[0] })
                };
            })
            .ToList();

        var input = new FemMemberForceSetBuildInput(
            new FemSchema { Id = 1, Tag = "Схема" },
            new FemMember { Id = 10, SchemaId = 1, ElemTag = "M1", NodeIdsJson = "[100,200]" },
            sourceNodes ?? [
                new FemNode { SchemaId = 1, NodeTag = "100", X = 0 },
                new FemNode { SchemaId = 1, NodeTag = "200", X = 5 }],
            meshNodes ?? DefaultMeshNodes(),
            orderedElements,
            forces ?? [
                new FemElementEndForces(101, 1000, 2000, 3000, 4000, 5000, 6000,
                                        7000, 8000, 9000, 10000, 11000, 12000),
                new FemElementEndForces(102, 1300, 1400, 1500, 1600, 1700, 1800,
                                        1900, 2000, 2100, 2200, 2300, 2400)],
            0,
            "step 1",
            stepConverged);
        return FemMemberForceSetBuilder.Build(input);
    }

    static IReadOnlyList<FemMeshNode> DefaultMeshNodes() => [
        new FemMeshNode { SchemaId = 1, NodeTag = "10", X = 0, SourceNodeTag = "100" },
        new FemMeshNode { SchemaId = 1, NodeTag = "20", X = 2 },
        new FemMeshNode { SchemaId = 1, NodeTag = "30", X = 5, SourceNodeTag = "200" }];

    static IReadOnlyList<FemElement> ElementsInOrder(params int[] tags) =>
        tags.Select(tag => tag switch
        {
            101 => new FemElement { ElemTag = "101", SourceMemberTag = "M1", NodeIdsJson = "[10,20]" },
            102 => new FemElement { ElemTag = "102", SourceMemberTag = "M1", NodeIdsJson = "[20,30]" },
            _ => throw new ArgumentOutOfRangeException(nameof(tags), tag, "Unknown test element")
        }).ToArray();
}
