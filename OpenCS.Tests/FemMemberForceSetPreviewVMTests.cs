using CScore;
using OpenCS.Services;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки выбора кандидата в preview VM.</summary>
public class FemMemberForceSetPreviewVMTests
{
    [Fact]
    public void SelectSource_ChangesOnlySelectedInternalRow()
    {
        var preview = PreviewWithTwoInternalCandidates();
        var vm = new FemMemberForceSetPreviewVM(preview, "Набор", "Описание");
        var target = vm.Rows.Single(row => row.MeshNodeTag == "20");
        var other = vm.Rows.Single(row => row.MeshNodeTag == "30");
        double otherN = other.SelectedCandidate.Values.N;

        target.SelectedSource = FemForceSourceSide.Right;

        Assert.Equal(target.Model.RightCandidate!.Values.N, target.SelectedCandidate.Values.N);
        Assert.Equal(otherN, other.SelectedCandidate.Values.N);
    }

    [Fact]
    public void BuildSelection_TrimsFieldsAndKeepsAllRows()
    {
        var vm = new FemMemberForceSetPreviewVM(
            PreviewWithTwoInternalCandidates(), "  M1  ", "  desc  ");

        var result = vm.BuildSelection();

        Assert.Equal("M1", result.Tag);
        Assert.Equal("desc", result.Description);
        Assert.Equal(4, result.Rows.Count);
    }

    [Fact]
    public void FreeTag_SkipsTakenNames()
    {
        ForceSet[] existing = [new() { Tag = "OpenSees M1, step 1" }, new() { Tag = " OpenSees M1, step 1 (2) " }];

        Assert.Equal("OpenSees M1, step 1 (3)", ForceSetAuthoring.FreeTag(existing, "OpenSees M1, step 1"));
        Assert.Equal("Другое", ForceSetAuthoring.FreeTag(existing, " Другое "));
    }

    [Fact]
    public void TagWarning_ShownOnlyForTakenName()
    {
        ForceSet[] existing = [new() { Tag = "Набор" }, new() { Tag = "Набор " }];
        var vm = new FemMemberForceSetPreviewVM(PreviewWithTwoInternalCandidates(), "Набор (2)", null, existing);
        Assert.False(vm.HasTagWarning);

        vm.Tag = " Набор";

        Assert.True(vm.HasTagWarning);
        Assert.Equal(2, vm.SameTagCount);
        Assert.True(vm.CanSave);
    }

    internal static FemMemberForceSetPreview PreviewWithTwoInternalCandidates() =>
        new(3, "Схема", 11, "M1", 1, "step 1", [
            Row("10", 0, null, Candidate(101, 1000), FemForceSourceSide.Only),
            Row("20", 2, Candidate(101, 2000), Candidate(102, 3000), FemForceSourceSide.Left),
            Row("30", 5, Candidate(102, 4000), Candidate(103, 5000), FemForceSourceSide.Left),
            Row("40", 7, Candidate(103, 6000), null, FemForceSourceSide.Only)]);

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
