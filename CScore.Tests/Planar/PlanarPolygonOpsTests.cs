using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public sealed class PlanarPolygonOpsTests
{
    static PlanarPoint2D P(double u, double v) => new(u, v);

    static IReadOnlyList<PlanarPoint2D> Rect(double u0, double v0, double u1, double v1) =>
        [P(u0, v0), P(u1, v0), P(u1, v1), P(u0, v1)];

    static double TotalArea(IEnumerable<IReadOnlyList<PlanarPoint2D>> pieces) =>
        pieces.Sum(piece => Math.Abs(PlanarPolygonOps.SignedArea(piece)));

    static PlanarPoint2D Center(IReadOnlyList<PlanarPoint2D> p) =>
        P(p.Average(q => q.U), p.Average(q => q.V));

    [Fact]
    public void SignedArea_SignFollowsWinding()
    {
        var square = Rect(0, 0, 1, 1);
        Assert.Equal(1.0, PlanarPolygonOps.SignedArea(square), 12);
        Assert.Equal(-1.0, PlanarPolygonOps.SignedArea(square.Reverse().ToList()), 12);
        Assert.Equal(1.0, PlanarPolygonOps.SignedArea(PlanarPolygonOps.Ccw(square.Reverse().ToList())), 12);
    }

    [Fact]
    public void ConvexHull_OfIBeam_IsBoundingRectangleWithoutCollinearPoints()
    {
        // Двутавр 0,2 × 0,4: полки 0,02, стенка 0,01; плюс точка на середине нижней грани.
        double b = 0.1, h = 0.2, t = 0.02, s = 0.005;
        PlanarPoint2D[] ibeam =
        [
            P(-b, -h), P(0, -h), P(b, -h), P(b, -h + t), P(s, -h + t), P(s, h - t), P(b, h - t), P(b, h),
            P(-b, h), P(-b, h - t), P(-s, h - t), P(-s, -h + t), P(-b, -h + t)
        ];

        var hull = PlanarPolygonOps.ConvexHull(ibeam);

        Assert.Equal(4, hull.Count);
        Assert.True(PlanarPolygonOps.SignedArea(hull) > 0);
        Assert.Equal(2 * b * 2 * h, PlanarPolygonOps.SignedArea(hull), 12);
    }

    [Fact]
    public void Contains_InsideOnEdgeAndOutside()
    {
        var square = Rect(0, 0, 1, 1);
        Assert.True(PlanarPolygonOps.Contains(square, P(0.5, 0.5), 1e-9));
        Assert.True(PlanarPolygonOps.Contains(square, P(1.0, 0.3), 1e-9));
        Assert.False(PlanarPolygonOps.Contains(square, P(1.1, 0.3), 1e-9));
    }

    [Fact]
    public void ClipHalfPlane_SquareByDiagonal_GivesHalf()
    {
        var left = PlanarPolygonOps.ClipHalfPlane(Rect(0, 0, 1, 1), P(0, 0), P(1, 1), keepLeft: true);
        var right = PlanarPolygonOps.ClipHalfPlane(Rect(0, 0, 1, 1), P(0, 0), P(1, 1), keepLeft: false);

        Assert.Equal(0.5, PlanarPolygonOps.SignedArea(left), 12);
        Assert.Equal(0.5, PlanarPolygonOps.SignedArea(right), 12);
        Assert.True(left.All(q => q.V >= q.U - 1e-12));
    }

    [Fact]
    public void ClipByConvex_LShapeByWindowAtReentrantCorner()
    {
        // L: квадрат 2×2 без верхнего правого квадранта; окно 1×1 с центром во входящем углу (1,1).
        PlanarPoint2D[] l = [P(0, 0), P(2, 0), P(2, 1), P(1, 1), P(1, 2), P(0, 2)];

        var clipped = PlanarPolygonOps.ClipByConvex(l, Rect(0.5, 0.5, 1.5, 1.5));

        Assert.Equal(0.75, Math.Abs(PlanarPolygonOps.SignedArea(clipped)), 12);
    }

    [Fact]
    public void Triangulate_LShape_CoversArea()
    {
        PlanarPoint2D[] l = [P(0, 0), P(2, 0), P(2, 1), P(1, 1), P(1, 2), P(0, 2)];

        var triangles = PlanarPolygonOps.Triangulate(l);

        Assert.Equal(4, triangles.Count);
        Assert.All(triangles, tri => Assert.True(PlanarPolygonOps.SignedArea(tri) > 0));
        Assert.Equal(3.0, TotalArea(triangles), 12);
    }

    [Fact]
    public void SubtractFromConvex_CornerHole_LeavesConvexPiecesOutsideHole()
    {
        var trace = Rect(0, 0, 2, 2);
        var hole = Rect(1, 1, 3, 3);

        var pieces = PlanarPolygonOps.SubtractFromConvex(trace, hole, 1e-9);

        Assert.Equal(3.0, TotalArea(pieces), 12);
        Assert.All(pieces, piece => Assert.True(PlanarPolygonOps.IsConvex(piece)));
        Assert.All(pieces, piece => Assert.False(PlanarPolygonOps.Contains(hole, Center(piece), -1e-9)));
    }

    [Fact]
    public void SubtractFromConvex_HoleStripSplitsTraceInTwo()
    {
        var pieces = PlanarPolygonOps.SubtractFromConvex(Rect(0, 0, 2, 2), Rect(0.75, -1, 1.25, 3), 1e-9);

        Assert.Equal(3.0, TotalArea(pieces), 12);
        Assert.Contains(pieces, piece => piece.All(q => q.U <= 0.75 + 1e-12));
        Assert.Contains(pieces, piece => piece.All(q => q.U >= 1.25 - 1e-12));
        Assert.DoesNotContain(pieces, piece => piece.Any(q => q.U > 0.75 + 1e-9 && q.U < 1.25 - 1e-9));
    }

    [Fact]
    public void SubtractFromConvex_HoleSharingEdge_NoSlivers()
    {
        // Отверстие примыкает к правому ребру следа и перекрывает его половину.
        var pieces = PlanarPolygonOps.SubtractFromConvex(Rect(0, 0, 2, 2), Rect(1, 0, 2, 2), 1e-9);

        Assert.Equal(2.0, TotalArea(pieces), 12);
        Assert.All(pieces, piece => Assert.True(Math.Abs(PlanarPolygonOps.SignedArea(piece)) >= 1e-9));
    }

    [Fact]
    public void SubtractFromConvex_DisjointAndCoveringHoles()
    {
        var trace = Rect(0, 0, 1, 1);

        var disjoint = PlanarPolygonOps.SubtractFromConvex(trace, Rect(5, 5, 6, 6), 1e-9);
        var covered = PlanarPolygonOps.SubtractFromConvex(trace, Rect(-1, -1, 2, 2), 1e-9);

        Assert.Single(disjoint);
        Assert.Equal(1.0, TotalArea(disjoint), 12);
        Assert.Empty(covered);
    }

    [Fact]
    public void SubtractFromConvex_NonConvexHole()
    {
        // L-отверстие: квадрат (1..3)² без верхнего правого квадранта (2..3)²; след (0..2)².
        PlanarPoint2D[] lHole = [P(1, 1), P(3, 1), P(3, 2), P(2, 2), P(2, 3), P(1, 3)];

        var pieces = PlanarPolygonOps.SubtractFromConvex(Rect(0, 0, 2, 2), lHole, 1e-9);

        // L ∩ след = (1..2)² — площадь 1.
        Assert.Equal(3.0, TotalArea(pieces), 12);
        Assert.All(pieces, piece => Assert.True(PlanarPolygonOps.IsConvex(piece)));
    }
}
