using CScore.Fem.Fragment;
using CScore.Planar;
using Xunit;
using static CScore.Tests.Fem.Fragment.StoreyFragmentFixtures;
using static CScore.Tests.Fem.Fragment.StoreyFragmentPrismFixtures;

namespace CScore.Tests.Fem.Fragment;

public sealed class StoreyFragmentPrismWallTests
{
    static double Length((PlanarVector3 P, PlanarVector3 Q) s) => (s.Q - s.P).Length;

    [Fact]
    public void CrossWallThroughFaces_Clipped_VerticalLateralEdgeCuts()
    {
        // Стена y = 0, x −3…3, z 0…6; плита на 3; призма 2 × 2.
        var d = BuildPrism(Square(1), [], [Wall("W1", 2, -3, 0, 0, 6, 6), Slab("S1", 1, 3)]);

        Assert.False(d.HasErrors);
        var walls = d.Members.Where(m => m.ParentMemberTag == "W1").ToList();
        Assert.Equal(new[] { "W1 · низ", "W1 · верх" }, walls.Select(w => w.Member.ElemTag));
        Assert.All(walls, w => Assert.Equal(3, Area(w.Region!), 6));
        var lateral = d.Cuts.Where(c => c.MemberTag.StartsWith("W1") && c.Side == FragmentCutSide.Lateral).ToList();
        Assert.Equal(4, lateral.Count);
        Assert.All(lateral, c => Assert.Equal(FragmentCutKind.Edge, c.Kind));
        Assert.All(lateral, c => Assert.Equal(1.5, c.Segments.Sum(Length), 6));
        Assert.All(lateral, c => Assert.All(c.Segments, s => Assert.Equal(Math.Abs(c.Outward.X), Math.Abs(s.P.X), 9)));
        Assert.Equal(2, lateral.Count(c => c.Outward == new PlanarVector3(1, 0, 0)));
        Assert.Equal(2, lateral.Count(c => c.Outward == new PlanarVector3(-1, 0, 0)));
        Assert.Equal(2.25, lateral.Single(c => c.MemberTag == "W1 · низ" && c.Outward.X > 0).Z, 9);
        Assert.Equal(2, d.Cuts.Single(c => c.Side == FragmentCutSide.Bottom).Segments.Sum(Length), 6);
    }

    [Fact]
    public void WallInFacePlane_Error_OutsideFace_NotIncluded()
    {
        var on = BuildPrism(Square(1), [], [Wall("W1", 2, -3, 1, 0, 6, 6), Slab("S1", 1, 3)]);
        Assert.Contains(on.Diagnostics, x => x.IsError && x.Code == "fragment_member_on_prism_face");

        var away = BuildPrism(Square(1), [], [Wall("W1", 2, 5, 1, 0, 6, 6), Slab("S1", 1, 3)]);
        Assert.False(away.HasErrors);
        Assert.DoesNotContain(away.Members, m => m.ParentMemberTag == "W1");
    }

    [Fact]
    public void WallEndingOnFace_NoLateralCut()
    {
        var d = BuildPrism(Square(1), [], [Wall("W1", 2, -1, 0, 0, 2, 6), Slab("S1", 1, 3)]);
        Assert.False(d.HasErrors);
        Assert.Equal(2, d.Members.Count(m => m.ParentMemberTag == "W1"));
        Assert.DoesNotContain(d.Cuts, c => c.MemberTag.StartsWith("W1") && c.Side == FragmentCutSide.Lateral);
    }
}
