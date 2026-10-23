using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public sealed class PlanarHalfPlaneClipTests
{
    static List<PlanarPoint2D> Poly(params double[] uv) =>
        Enumerable.Range(0, uv.Length / 2).Select(i => new PlanarPoint2D(uv[2 * i], uv[2 * i + 1])).ToList();

    static PlanarPolygonWithHoles Box(double u0, double v0, double u1, double v1, params IReadOnlyList<PlanarPoint2D>[] holes) =>
        new(Poly(u0, v0, u1, v0, u1, v1, u0, v1), holes);

    static readonly PlanarHalfPlane Above1 = new(0, 1, 1); // v ≥ 1

    [Fact]
    public void Square_KeepUpperPart()
    {
        var piece = Assert.Single(PlanarHalfPlaneClip.Clip(Box(0, 0, 2, 2), Above1, 1e-9));
        Assert.Equal(2, piece.Area, 9);
        Assert.Empty(piece.Holes);
        Assert.True(PlanarPolygonOps.SignedArea(piece.Outer) > 0);
    }

    [Fact]
    public void HoleInsideKeptPart_StaysHole()
    {
        var piece = Assert.Single(PlanarHalfPlaneClip.Clip(Box(0, 0, 4, 4, Poly(1, 2.5, 2, 2.5, 2, 3.5, 1, 3.5)), Above1, 1e-9));
        Assert.Single(piece.Holes);
        Assert.Equal(11, piece.Area, 9);
        Assert.True(PlanarPolygonOps.SignedArea(piece.Holes[0]) < 0);
    }

    [Fact]
    public void HoleCrossingLine_BecomesNotch()
    {
        var piece = Assert.Single(PlanarHalfPlaneClip.Clip(Box(0, 0, 4, 4, Poly(1, 0.5, 3, 0.5, 3, 1.5, 1, 1.5)), Above1, 1e-9));
        Assert.Empty(piece.Holes);
        Assert.Equal(11, piece.Area, 9);
    }

    [Fact]
    public void UShape_SplitsIntoTwoPieces()
    {
        var u = new PlanarPolygonWithHoles(Poly(0, 0, 3, 0, 3, 3, 2, 3, 2, 1, 1, 1, 1, 3, 0, 3), []);
        var pieces = PlanarHalfPlaneClip.Clip(u, new PlanarHalfPlane(0, 1, 2), 1e-9);
        Assert.Equal(2, pieces.Count);
        Assert.All(pieces, p => Assert.Equal(1, p.Area, 9));
        Assert.True(pieces[0].Outer.Min(p => p.U) < pieces[1].Outer.Min(p => p.U));
    }

    [Fact]
    public void EntirelyOutside_Empty_EntirelyInside_Same()
    {
        Assert.Empty(PlanarHalfPlaneClip.Clip(Box(0, -3, 2, -1), Above1, 1e-9));
        Assert.Equal(4, Assert.Single(PlanarHalfPlaneClip.Clip(Box(0, 2, 2, 4), Above1, 1e-9)).Area, 9);
    }

    [Fact]
    public void EdgeOnLine_KeptOnlyFromInside()
    {
        var tri = new PlanarPolygonWithHoles(Poly(0, 0, 2, 0, 1, 2), []);
        Assert.Equal(2, Assert.Single(PlanarHalfPlaneClip.Clip(tri, new PlanarHalfPlane(0, 1, 0), 1e-9)).Area, 9);
        Assert.Empty(PlanarHalfPlaneClip.Clip(tri, new PlanarHalfPlane(0, -1, 0), 1e-9));
    }

    [Fact]
    public void InclinedLine_HalfSquare()
    {
        var piece = Assert.Single(PlanarHalfPlaneClip.Clip(Box(0, 0, 1, 1), new PlanarHalfPlane(1, 1, 1), 1e-9));
        Assert.Equal(0.5, piece.Area, 9);
    }
}
