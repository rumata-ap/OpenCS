using CScore.Fem.Fragment;
using Xunit;
using static CScore.Tests.Fem.Fragment.StoreyFragmentFixtures;

namespace CScore.Tests.Fem.Fragment;

public sealed class StoreyFragmentBarTests
{
    [Fact]
    public void Column_ThroughSlab_TwoPartsTwoCuts()
    {
        var d = Build(1.5, 4.5, [N("1", 0, 0, 0), N("2", 0, 0, 6)], [(Bar("C1", "1", "2"), null), Slab("S1", 1, 3)]);

        Assert.False(d.HasErrors);
        var bars = d.Members.Where(m => m.Member.ElemType == "beam").Select(m => m.Member.ElemTag).ToList();
        Assert.Equal(new[] { "C1 · низ", "C1 · верх" }, bars);
        Assert.All(d.Members, m => Assert.Equal("generated", m.Member.MeshSource));
        Assert.Contains(d.Nodes, n => Math.Abs(n.Z - 3) < 1e-9 && n.X == 0);
        var cuts = d.Cuts.OrderBy(c => c.Z).ToList();
        Assert.Equal(2, cuts.Count);
        Assert.Equal((FragmentCutKind.End, FragmentCutSide.Bottom, 1.5, "C1 · низ"), (cuts[0].Kind, cuts[0].Side, cuts[0].Z, cuts[0].MemberTag));
        Assert.Equal((FragmentCutSide.Top, 4.5, "C1 · верх"), (cuts[1].Side, cuts[1].Z, cuts[1].MemberTag));
        Assert.Equal(1.5, d.Nodes.Single(n => n.NodeTag == cuts[0].NodeTag).Z, 9);
    }

    [Fact]
    public void ColumnsMeetingAtSlab_ShareNode()
    {
        var d = Build(1.5, 4.5, [N("1", 0, 0, 0), N("2", 0, 0, 3), N("3", 0, 0, 6)],
            [(Bar("C1", "1", "2"), null), (Bar("C2", "2", "3"), null), Slab("S1", 1, 3)]);

        Assert.Equal(new[] { "C1", "C2" }, d.Members.Where(m => m.Member.ElemType == "beam").Select(m => m.Member.ElemTag));
        Assert.Equal(3, d.Nodes.Count);
    }

    [Fact]
    public void Column_OutsideSlabContour_NotSplit()
    {
        var d = Build(1.5, 4.5, [N("1", 5, 0, 0), N("2", 5, 0, 6)], [(Bar("C1", "1", "2"), null), Slab("S1", 1, 3)]);
        Assert.Equal(new[] { "C1" }, d.Members.Where(m => m.Member.ElemType == "beam").Select(m => m.Member.ElemTag));
    }

    [Fact]
    public void BeamOnCutPlane_Error()
    {
        var d = Build(1.5, 4.5, [N("1", 0, 0, 1.5), N("2", 3, 0, 1.5)], [(Bar("B1", "1", "2"), null), Slab("S1", 1, 3)]);
        Assert.Contains(d.Diagnostics, x => x.IsError && x.Code == "fragment_member_on_cut_plane");
    }

    [Fact]
    public void SlabOnCutPlane_Error()
    {
        var d = Build(3, 6, [], [Slab("S1", 1, 3)]);
        Assert.Contains(d.Diagnostics, x => x.IsError && x.Code == "fragment_member_on_cut_plane");
    }

    [Fact]
    public void BandEmpty_NoSlab_NoMesh_Errors()
    {
        Assert.Contains(Build(4.5, 1.5, [], [Slab("S1", 1, 3)]).Diagnostics, x => x.Code == "fragment_band_empty");
        Assert.Contains(Build(1.5, 4.5, [], [Slab("S1", 1, 10)]).Diagnostics, x => x.Code == "fragment_no_slab");
        Assert.Contains(Build(1.5, 4.5, [], [Slab("S1", 1, 3)], meshElements: []).Diagnostics, x => x.Code == "fragment_parent_no_mesh");
    }

    [Fact]
    public void ShallowBar_Warning()
    {
        var d = Build(1.5, 4.5, [N("1", -20, 10, 0), N("2", 20, 10, 3)], [(Bar("K1", "1", "2"), null), Slab("S1", 1, 3)]);
        Assert.Contains(d.Diagnostics, x => !x.IsError && x.Code == "fragment_bar_shallow_cut");
    }

    [Fact]
    public void SupportAtCutPlane_NotCut_DofKept()
    {
        var d = Build(1.5, 4.5, [N("1", 0, 0, 1.5, dof: 63), N("2", 0, 0, 3)], [(Bar("C1", "1", "2"), null), Slab("S1", 1, 3)]);
        Assert.Empty(d.Cuts);
        Assert.Equal(63, d.Nodes.Single(n => n.Z == 1.5).DofMask);
    }
}
