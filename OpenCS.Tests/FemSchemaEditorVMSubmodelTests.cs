using CScore.Fem;
using CScore.Planar;
using CScore.Submodel;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

public sealed class SubmodelExtractGateTests
{
    [Theory]
    [InlineData(ChainVerdict.Extractable, true, true, true, "SubmodelExtractBlockDirty")]
    [InlineData(null, true, true, false, "SubmodelExtractBlockCheckChain")]
    [InlineData(ChainVerdict.Extractable, false, true, false, "SubmodelExtractBlockCheckChain")]
    [InlineData(ChainVerdict.NotExtractable, true, true, false, "SubmodelExtractBlockNotExtractable")]
    [InlineData(ChainVerdict.Extractable, true, false, false, "SubmodelExtractBlockNoParentAnalysis")]
    [InlineData(ChainVerdict.Extractable, true, true, false, null)]
    [InlineData(ChainVerdict.ExtractableWithWarnings, true, true, false, null)]
    public void Reason_CoversEveryBlock(ChainVerdict? verdict, bool selectionMatches, bool hasParent, bool isDirty, string? expected) =>
        Assert.Equal(expected, SubmodelExtractGate.Reason(verdict, selectionMatches, hasParent, isDirty));

    static OrderedBeamSegment Segment(string key, string? member) =>
        new(new BeamSegmentInput(key, new PlanarVector3(0, 0, 0), new PlanarVector3(1, 0, 0), 0, BetaSource.Absent, member),
            false, 0, 1, 1, 0);

    static StraightBeamChain Chain(params OrderedBeamSegment[] segments) =>
        new(segments, [], new PlanarVector3(0, 0, 0), new PlanarVector3(1, 0, 0), segments.Length, [], []);

    [Fact]
    public void DefaultTag_UsesMemberTagsOfEndSegments()
    {
        Assert.Equal("P: B1..B3", SubmodelExtractGate.DefaultTag("{0}: {1}..{2}", "{0}: {1}", "P",
            Chain(Segment("11", "B1"), Segment("12", "B2"), Segment("13", "B3"))));
        Assert.Equal("P: B", SubmodelExtractGate.DefaultTag("{0}: {1}..{2}", "{0}: {1}", "P",
            Chain(Segment("11", "B"), Segment("12", "B"))));
        Assert.Equal("P: 11..12", SubmodelExtractGate.DefaultTag("{0}: {1}..{2}", "{0}: {1}", "P",
            Chain(Segment("11", null), Segment("12", null))));
    }
}

[Collection("FEM schema editor VM")]
public sealed class FemSchemaEditorVMSubmodelTests(FemSchemaEditorVMFixture fixture)
{
    static FemMeshNode Node(int tag, double x) => new() { Id = 100 + tag, NodeTag = tag.ToString(), X = x };

    static FemElement Beam(int tag, int i, int j) => new()
    {
        Id = 200 + tag, ElemTag = tag.ToString(), ElemType = "beam", NodeIdsJson = $"[{i},{j}]", SourceMemberTag = "B"
    };

    [Fact]
    public void SelectionChange_ResetsChainAnalysis()
    {
        var editor = new FemSchemaEditorVM(new FemSchema { Id = 1, Tag = "P" }, fixture.App);
        var nodes = new[] { Node(1, 0), Node(2, 1), Node(3, 2) };
        var elements = new[] { Beam(11, 1, 2), Beam(12, 2, 3) };
        editor.Selection.ToggleElement("11", additive: false);
        editor.Selection.ToggleElement("12", additive: true);

        editor.AnalyzeStraightChain(elements, nodes, []);

        Assert.NotNull(editor.ChainAnalysis);
        Assert.NotEqual(ChainVerdict.NotExtractable, editor.ChainAnalysis!.Verdict);
        Assert.NotEqual("SubmodelExtractBlockCheckChain", editor.ExtractBlockReason);

        editor.Selection.ToggleElement("12", additive: true);

        Assert.Null(editor.ChainAnalysis);
        Assert.Equal("", editor.ChainVerdictText);
        Assert.Equal("SubmodelExtractBlockCheckChain", editor.ExtractBlockReason);
        Assert.False(editor.ExtractSubmodelCommand.CanExecute(null));
    }
}
