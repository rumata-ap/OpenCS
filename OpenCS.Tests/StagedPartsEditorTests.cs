using CScore;
using OpenCS.LoadHistory;
using Xunit;

namespace OpenCS.Tests;

public sealed class StagedPartsEditorTests
{
    [Fact]
    public void Add_Move_Remove_KeepIndicesAndUnion()
    {
        var beam = ReportFixtures.BuildBeam();
        var s = new StagedSection();
        var p0 = StagedPartsEditor.AddLinkedPart(s, beam, "балка");
        var p1 = StagedPartsEditor.AddOwnPart(s, "обойма");
        p1.Areas.Add(ReportFixtures.BuildBeam().Areas[0]);
        StagedPartsEditor.Move(s, p1, -1);
        Assert.Equal([p1, p0], s.Parts);
        Assert.Equal([0, 1], s.Parts.Select(p => p.Index));
        Assert.Same(beam.Areas, p0.Areas);
        Assert.Equal(beam.Areas.Count + 1, s.Areas.Count);
        StagedPartsEditor.Remove(s, p1);
        Assert.Equal(0, p0.Index);
        Assert.Equal(beam.Areas.Count, s.Areas.Count);
    }

    [Fact]
    public void Detach_CopiesAreasAndDropsLink()
    {
        var beam = ReportFixtures.BuildBeam();
        var s = new StagedSection();
        var p = StagedPartsEditor.AddLinkedPart(s, beam, "балка");
        StagedPartsEditor.Detach(p);
        Assert.Null(p.SourceSectionId);
        Assert.NotSame(beam.Areas, p.Areas);
        Assert.Equal(beam.Areas, p.Areas);
    }

    [Fact]
    public void SourceCandidates_ExcludeStaged()
    {
        var beam = ReportFixtures.BuildBeam();
        var s = new StagedSection();
        Assert.Equal([beam], StagedPartsEditor.SourceCandidates([beam, s, new StagedSection()], s));
    }
}
