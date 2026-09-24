using CScore.Planar;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class StripSupportGeometryTests
{
    const double Tol = 1e-6;

    // Плита 0..12 × −1..1 с отверстием 4..6 × −0,5..0,5; контуры — с дублированной замыкающей вершиной.
    static Contour Hull(bool closed = true) => closed
        ? new Contour { X = [0, 12, 12, 0, 0], Y = [-1, -1, 1, 1, -1] }
        : new Contour { X = [0, 12, 12, 0], Y = [-1, -1, 1, 1] };

    static Contour Hole() => new() { X = [4, 6, 6, 4, 4], Y = [-0.5, -0.5, 0.5, 0.5, -0.5] };

    static PlanarPoint2D P(double u, double v) => new(u, v);

    [Fact]
    public void DistanceToFootprint_PointAndPolyline()
    {
        var point = new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Point, [P(1, 1)]);
        var curve = new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve, [P(0, 0), P(4, 0)]);

        Assert.Equal(5.0, StripSupportGeometry.DistanceToFootprint(P(4, 5), point), 12);
        Assert.Equal(3.0, StripSupportGeometry.DistanceToFootprint(P(2, 3), curve), 12);
        Assert.Equal(5.0, StripSupportGeometry.DistanceToFootprint(P(7, 4), curve), 12);
    }

    [Theory]
    [InlineData(1.0, 0.0, true)]      // внутри
    [InlineData(13.0, 0.0, false)]    // снаружи
    [InlineData(0.0, 0.3, true)]      // на ребре hull
    [InlineData(0.0, -1.0, true)]     // в вершине hull
    [InlineData(5.0, 0.0, false)]     // строго внутри отверстия
    [InlineData(4.0, 0.0, true)]      // на границе отверстия
    [InlineData(4.0, 0.5, true)]      // в вершине отверстия
    public void IsInsideRegion_IsBoundaryInclusive(double u, double v, bool expected)
    {
        Assert.Equal(expected, StripSupportGeometry.IsInsideRegion(P(u, v), Hull(), [Hole()], Tol));
        Assert.Equal(expected, StripSupportGeometry.IsInsideRegion(P(u, v), Hull(closed: false), [Hole()], Tol));
    }

    [Fact]
    public void Clip_SegmentInside_IsKept()
    {
        var pieces = StripSupportGeometry.ClipSegmentToRegion(P(2, -1), P(2, 1), Hull(), [Hole()], Tol);

        var piece = Assert.Single(pieces);
        AssertPoint(P(2, -1), piece.A);
        AssertPoint(P(2, 1), piece.B);
    }

    [Fact]
    public void Clip_SegmentPartlyOutside_IsTrimmed()
    {
        var pieces = StripSupportGeometry.ClipSegmentToRegion(P(2, -3), P(2, 3), Hull(), [], Tol);

        var piece = Assert.Single(pieces);
        AssertPoint(P(2, -1), piece.A);
        AssertPoint(P(2, 1), piece.B);
    }

    [Fact]
    public void Clip_SegmentOnHullEdge_IsKeptWhole()
    {
        var pieces = StripSupportGeometry.ClipSegmentToRegion(P(0, -1), P(0, 1), Hull(), [Hole()], Tol);

        var piece = Assert.Single(pieces);
        AssertPoint(P(0, -1), piece.A);
        AssertPoint(P(0, 1), piece.B);
    }

    [Fact]
    public void Clip_SegmentThroughHole_SplitsInTwo()
    {
        var pieces = StripSupportGeometry.ClipSegmentToRegion(P(5, -1), P(5, 1), Hull(), [Hole()], Tol);

        Assert.Equal(2, pieces.Count);
        AssertPoint(P(5, -1), pieces[0].A);
        AssertPoint(P(5, -0.5), pieces[0].B);
        AssertPoint(P(5, 0.5), pieces[1].A);
        AssertPoint(P(5, 1), pieces[1].B);
    }

    [Fact]
    public void Clip_SegmentAlongHoleBoundary_IsKept()
    {
        var pieces = StripSupportGeometry.ClipSegmentToRegion(P(4, -0.5), P(4, 0.5), Hull(), [Hole()], Tol);

        var piece = Assert.Single(pieces);
        AssertPoint(P(4, -0.5), piece.A);
        AssertPoint(P(4, 0.5), piece.B);
    }

    [Fact]
    public void Clip_SegmentEnteringThroughHullVertex_StartsAtVertex()
    {
        // Прямая v = u − 1 входит в плиту ровно через вершину (0, −1).
        var pieces = StripSupportGeometry.ClipSegmentToRegion(P(-1, -2), P(1, 0), Hull(), [], Tol);

        var piece = Assert.Single(pieces);
        AssertPoint(P(0, -1), piece.A);
        AssertPoint(P(1, 0), piece.B);

        var outside = StripSupportGeometry.ClipSegmentToRegion(P(-1, -2), P(1, -2), Hull(), [], Tol);
        Assert.Empty(outside);
    }

    [Fact]
    public void IsOnHullBoundary_DistinguishesEdgeAndInterior()
    {
        var edge = new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve, [P(0, -1), P(0, 1)]);
        var interior = new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve, [P(6, -1), P(6, 1)]);
        var corner = new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Point, [P(12, 1)]);

        Assert.True(StripSupportGeometry.IsOnHullBoundary(edge, Hull(), Tol));
        Assert.False(StripSupportGeometry.IsOnHullBoundary(interior, Hull(), Tol));
        Assert.True(StripSupportGeometry.IsOnHullBoundary(corner, Hull(), Tol));
    }

    static void AssertPoint(PlanarPoint2D expected, PlanarPoint2D actual)
    {
        Assert.Equal(expected.U, actual.U, 9);
        Assert.Equal(expected.V, actual.V, 9);
    }
}
