using CScore;
using CScore.Fem;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки перевода preview в сохраняемый ForceSet.</summary>
public class FemMemberForceSetFactoryTests
{
    [Fact]
    public void Create_SetsOpenSeesMetadataAndMapsRowsInSOrder()
    {
        var preview = PreviewWithRowsInSOrder();
        var selected = new FemMemberForceSetSelection("OpenSees M1 step 2", "Описание", preview.Rows);

        var forceSet = FemMemberForceSetFactory.Create(
            new FemSchema { Id = 3, Tag = "Схема" },
            new FemMember { Id = 11, ElemTag = "M1" },
            preview,
            selected,
            []);

        Assert.Equal(1, forceSet.Num);
        Assert.Equal("bar", forceSet.Kind);
        Assert.Equal("fea", forceSet.SourceType);
        Assert.Equal(3, forceSet.SourceSchemaId);
        Assert.Equal(11, forceSet.SourceMemberId);
        Assert.Equal("M1", forceSet.SourceElementTag);
        Assert.Equal(["node 10", "node 20", "node 30"], forceSet.Items.Select(i => i.Label));
        Assert.Equal([1, 2, 3], forceSet.Items.Select(i => i.Num));
    }

    [Fact]
    public void Create_UsesMaximumExistingNumPlusOne()
    {
        var existing = new[]
        {
            new ForceSet { Num = 2 },
            new ForceSet { Num = 17 },
            new ForceSet { Num = 5 }
        };

        var forceSet = FemMemberForceSetFactory.Create(
            Schema(), Member(), PreviewWithRowsInSOrder(), Selection(), existing);

        Assert.Equal(18, forceSet.Num);
    }

    [Fact]
    public void Create_UsesSelectedRightCandidateWithoutSumming()
    {
        var preview = PreviewWithInternalNode();
        preview.Rows.Single(r => r.MeshNodeTag == "20").SelectedSource = FemForceSourceSide.Right;

        var forceSet = FemMemberForceSetFactory.Create(
            Schema(), Member(), preview, Selection(preview), []);

        Assert.Equal(
            preview.Rows.Single(r => r.MeshNodeTag == "20").RightCandidate!.Values.N / 1000.0,
            forceSet.Items.Single(i => i.Label == "node 20").N,
            12);
    }

    [Fact]
    public void Create_RejectsEmptyTag()
    {
        Assert.Throws<ArgumentException>(() => FemMemberForceSetFactory.Create(
            Schema(), Member(), PreviewWithRowsInSOrder(),
            new FemMemberForceSetSelection("  ", null, PreviewWithRowsInSOrder().Rows), []));
    }

    [Fact]
    public void Create_TrimsDescriptionAndSortsRowsByPosition()
    {
        var preview = PreviewWithRowsInSOrder();
        var selection = new FemMemberForceSetSelection(
            "  OS M1  ", "   ", preview.Rows.Reverse().ToArray());

        var forceSet = FemMemberForceSetFactory.Create(
            Schema(), Member(), preview, selection, []);

        Assert.Equal("OS M1", forceSet.Tag);
        Assert.Null(forceSet.Description);
        Assert.Equal(["node 10", "node 20", "node 30"], forceSet.Items.Select(i => i.Label));
    }

    static FemSchema Schema() => new() { Id = 3, Tag = "Схема" };

    static FemMember Member() => new() { Id = 11, SchemaId = 3, ElemTag = "M1" };

    static FemMemberForceSetPreview PreviewWithRowsInSOrder() =>
        new(3, "Схема", 11, "M1", 1, "step 1", [
            Row("10", 0, Candidate(101, 1000), null, FemForceSourceSide.Only),
            Row("20", 2, Candidate(101, 2000), Candidate(102, 3000), FemForceSourceSide.Left),
            Row("30", 5, null, Candidate(102, 4000), FemForceSourceSide.Only)]);

    static FemMemberForceSetPreview PreviewWithInternalNode() => PreviewWithRowsInSOrder();

    static FemMemberForceSetSelection Selection(FemMemberForceSetPreview? preview = null)
    {
        preview ??= PreviewWithRowsInSOrder();
        return new("OS M1", "step 1", preview.Rows);
    }

    static FemMemberForceSetPreviewRow Row(
        string nodeTag,
        double s,
        FemMemberForceCandidate? left,
        FemMemberForceCandidate? right,
        FemForceSourceSide selected) =>
        new(nodeTag, s, left, right, selected);

    static FemMemberForceCandidate Candidate(int elementTag, double n) =>
        new(elementTag, new FemForceEndpointValues(n, 0, 0, 0, 0, 0));
}
