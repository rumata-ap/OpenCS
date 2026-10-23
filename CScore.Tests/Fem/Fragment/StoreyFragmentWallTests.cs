using CScore.Fem.Fragment;
using CScore.PlateRebar;
using Xunit;
using static CScore.Tests.Fem.Fragment.StoreyFragmentFixtures;

namespace CScore.Tests.Fem.Fragment;

public sealed class StoreyFragmentWallTests
{
    static double Length((CScore.Planar.PlanarVector3 P, CScore.Planar.PlanarVector3 Q) s) => (s.Q - s.P).Length;

    [Fact]
    public void WallUnderAndOverSlab_SplitAtSlab_EdgeCuts()
    {
        // Стена по кромке плиты y = −3, x −3…3, z 0…6; плита на 3.
        var d = Build(1.5, 4.5, [], [Wall("W1", 2, -3, -3, 0, 6, 6), Slab("S1", 1, 3)]);

        Assert.False(d.HasErrors);
        var walls = d.Members.Where(m => m.ParentMemberTag == "W1").ToList();
        Assert.Equal(new[] { "W1 · низ", "W1 · верх" }, walls.Select(w => w.Member.ElemTag));
        Assert.All(walls, w => Assert.Equal(9, Area(w.Region!), 6));
        var bottom = Assert.Single(d.Cuts, x => x.Side == FragmentCutSide.Bottom);
        Assert.Equal((FragmentCutKind.Edge, "W1 · низ", 1.5), (bottom.Kind, bottom.MemberTag, bottom.Z));
        Assert.Equal(6, bottom.Segments.Sum(Length), 6);
        Assert.All(bottom.Segments, s => Assert.Equal(1.5, s.P.Z, 9));
        Assert.Equal("W1 · верх", Assert.Single(d.Cuts, x => x.Side == FragmentCutSide.Top).MemberTag);
    }

    [Fact]
    public void WallWithoutAdjacentSlab_NotSplit()
    {
        var d = Build(1.5, 4.5, [], [Wall("W2", 2, -3, 10, 0, 6, 6), Slab("S1", 1, 3)]);
        var wall = Assert.Single(d.Members, m => m.ParentMemberTag == "W2");
        Assert.Equal("W2", wall.Member.ElemTag);
        Assert.Equal(18, Area(wall.Region!), 6);
    }

    [Fact]
    public void OpeningCrossingZ1_BecomesNotch()
    {
        // Проём u 2…3, v 0,5…2,5 — пересекает z₁ = 1,5.
        var d = Build(1.5, 4.5, [], [Wall("W1", 2, -3, -3, 0, 6, 6, Box(2, 0.5, 3, 2.5, "door")), Slab("S1", 1, 3)]);

        var low = d.Members.Single(m => m.Member.ElemTag == "W1 · низ").Region!;
        Assert.Empty(low.Holes);
        Assert.Equal(8, Area(low), 6);
        var bottom = Assert.Single(d.Cuts, x => x.Side == FragmentCutSide.Bottom);
        Assert.Equal(2, bottom.Segments.Count);
        Assert.Equal(5, bottom.Segments.Sum(Length), 6);
    }

    [Fact]
    public void RebarZone_ClippedPerPiece()
    {
        var wall = Wall("W1", 2, -3, -3, 0, 6, 6);
        wall.Region.RebarZones.Add(new RebarZone
        {
            Name = "Z",
            Polygon = [new() { U = 0, V = 0 }, new() { U = 6, V = 0 }, new() { U = 6, V = 6 }, new() { U = 0, V = 6 }],
        });
        var d = Build(1.5, 4.5, [], [wall, Slab("S1", 1, 3)]);

        foreach (var piece in d.Members.Where(m => m.ParentMemberTag == "W1"))
        {
            var zone = Assert.Single(piece.Region!.RebarZones);
            var pts = zone.Polygon.Select(p => new CScore.Planar.PlanarPoint2D(p.U, p.V)).ToList();
            Assert.Equal(9, Math.Abs(CScore.Planar.PlanarPolygonOps.SignedArea(pts)), 6);
        }
    }
}
