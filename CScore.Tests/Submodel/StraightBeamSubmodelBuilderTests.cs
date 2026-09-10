using CScore.Fem;
using CScore.Planar;
using CScore.Submodel;
using Xunit;

namespace CScore.Tests.Submodel;

public sealed class StraightBeamSubmodelBuilderTests
{
    static IReadOnlyList<FemMeshNode> Nodes() =>
    [
        new() { Id = 1, SchemaId = 7, NodeTag = "1", X = 0, Y = 0, Z = 0, SourceNodeTag = "101" },
        new() { Id = 2, SchemaId = 7, NodeTag = "2", X = 1, Y = 0, Z = 0, SourceNodeTag = "102" },
        new() { Id = 3, SchemaId = 7, NodeTag = "3", X = 2, Y = 0, Z = 0, SourceNodeTag = "103" }
    ];

    static IReadOnlyList<FemElement> Elements() =>
    [
        new() { Id = 10, SchemaId = 7, ElemTag = "a", ElemType = "beam", NodeIdsJson = "[1,2]", SourceMemberTag = "M1" },
        new() { Id = 20, SchemaId = 7, ElemTag = "b", ElemType = "beam", NodeIdsJson = "[2,3]", SourceMemberTag = "M1" }
    ];

    static StraightBeamChainAnalysis Analysis()
    {
        var first = new BeamSegmentInput("a", new PlanarVector3(0, 0, 0), new PlanarVector3(1, 0, 0), 0, BetaSource.Member, "M1");
        var second = new BeamSegmentInput("b", new PlanarVector3(1, 0, 0), new PlanarVector3(2, 0, 0), 10, BetaSource.Member, "M1");
        var chain = new StraightBeamChain(
            [
                new OrderedBeamSegment(first, false, 0, 1, 1, 0),
                new OrderedBeamSegment(second, true, 1, 2, 1, 0)
            ],
            [
                new ChainNode(new PlanarVector3(0, 0, 0), 0, true, ["a"], 0),
                new ChainNode(new PlanarVector3(1, 0, 0), 1, false, ["a", "b"], 0),
                new ChainNode(new PlanarVector3(2, 0, 0), 2, true, ["b"], 0)
            ],
            new PlanarVector3(0, 0, 0), new PlanarVector3(1, 0, 0), 2, [], []);
        var tolerances = ChainTolerances.Default.Resolve(2);
        return new StraightBeamChainAnalysis(
            ChainVerdict.Extractable, chain, [], [], tolerances,
            new ChainMetrics(null, null, null, null, null, 2, 1));
    }

    [Fact]
    public void Build_ExtractableChain_ClonesOnlyChainMeshAndProvenance()
    {
        var result = StraightBeamSubmodelBuilder.Build(7, Analysis(), Elements(), Nodes());

        var draft = Assert.IsType<SubmodelExtractionDraft>(result.Draft);
        Assert.True(result.IsSuccess);
        Assert.Equal(["1", "2", "3"], draft.MeshNodes.Select(x => x.NodeTag));
        Assert.Equal(["a", "b"], draft.MeshElements.Select(x => x.ElemTag));
        Assert.All(draft.MeshNodes, x => Assert.Equal(0, x.Id));
        Assert.All(draft.MeshElements, x => Assert.Equal(0, x.Id));
        Assert.Equal([0, 1], draft.Segments.Select(x => x.Ordinal));
        Assert.True(draft.Segments[1].IsReversed);
        Assert.Equal(2, draft.Nodes.Single(x => x.ParentNodeTag == "2").ParentNodeId);
    }

    [Fact]
    public void Build_NotExtractableAnalysis_ReturnsDiagnosticWithoutDraft()
    {
        var analysis = Analysis() with { Verdict = ChainVerdict.NotExtractable, Chain = null };

        var result = StraightBeamSubmodelBuilder.Build(7, analysis, Elements(), Nodes());

        Assert.False(result.IsSuccess);
        Assert.Null(result.Draft);
        Assert.Contains(result.Diagnostics, x => x.Code == "submodel_draft_chain_not_extractable" && x.IsError);
    }

    [Fact]
    public void Build_MissingSourceMeshElement_ReturnsDiagnosticWithoutDraft()
    {
        var result = StraightBeamSubmodelBuilder.Build(7, Analysis(), Elements().Where(x => x.ElemTag == "a").ToList(), Nodes());

        Assert.False(result.IsSuccess);
        Assert.Null(result.Draft);
        Assert.Contains(result.Diagnostics, x => x.Code == "submodel_draft_element_missing" && (x.SourceKeys ?? []).Contains("b"));
    }

    [Fact]
    public void Build_DuplicateParentNodeTag_ReturnsAmbiguousDiagnostic()
    {
        var nodes = Nodes().Append(new FemMeshNode { Id = 99, SchemaId = 7, NodeTag = "2", X = 1, Y = 0, Z = 0 }).ToList();

        var result = StraightBeamSubmodelBuilder.Build(7, Analysis(), Elements(), nodes);

        Assert.Null(result.Draft);
        Assert.Contains(result.Diagnostics, x => x.Code == "submodel_draft_node_ambiguous");
    }

    [Fact]
    public void Build_DoesNotMutateParentMeshObjects()
    {
        var elements = Elements();
        var nodes = Nodes();

        _ = StraightBeamSubmodelBuilder.Build(7, Analysis(), elements, nodes);

        Assert.Equal([10, 20], elements.Select(x => x.Id));
        Assert.Equal([1, 2, 3], nodes.Select(x => x.Id));
        Assert.All(elements, x => Assert.Equal(7, x.SchemaId));
    }
}
