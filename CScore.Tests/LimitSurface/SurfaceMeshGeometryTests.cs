using CScore.LimitSurface;
using Xunit;
using G = CScore.LimitSurface.SurfaceMeshGeometry;

namespace CScore.Tests.LimitSurface;

public class SurfaceMeshGeometryTests
{
    [Fact]
    public void Loft_StripBetweenPoles_NoDegenerate()
    {
        var tris = G.Loft([0, 2, 3, 1], [0, 1, 2, Math.PI], [0, 4, 5, 6, 1], [0, 0.5, 1.5, 2.5, Math.PI]);
        Assert.All(tris, t => Assert.True(t.A != t.B && t.B != t.C && t.A != t.C));
        Assert.Equal(5, tris.Count);   // 2 + 3 внутренних узла
    }

    [Fact]
    public void LcsPairs_FindsCommonSubsequence()
    {
        var p = G.LcsPairs<int>([1, 2, 3, 4], [2, 9, 4]);
        Assert.Equal(new List<(int, int)> { (1, 0), (3, 2) }, p);
    }

    [Fact]
    public void LoftAnchored_ConnectsMatchingKinks()
    {
        var k = new KinkTransition(new ActiveConstraint(0, -1, false), new ActiveConstraint(5, 1, false));
        // Излом на A в θ=0.4 (индекс 3), на B в θ=2.6 (индекс 13): сшивка по θ их бы не соединила
        int[] aIdx = [0, 2, 3, 4, 1];  double[] aT = [0, 0.2, 0.4, 2.0, Math.PI];
        int[] bIdx = [0, 12, 13, 14, 1]; double[] bT = [0, 1.0, 2.6, 2.9, Math.PI];
        KinkTransition?[] aTr = [null, null, k, null, null];
        KinkTransition?[] bTr = [null, null, k, null, null];
        var tris = G.LoftAnchored(aIdx, aT, aTr, bIdx, bT, bTr);
        Assert.Contains(tris, t => new[] { t.A, t.B, t.C }.Contains(3) && new[] { t.A, t.B, t.C }.Contains(13));
    }

    [Fact]
    public void PointTriangleDistance_VoronoiRegions()
    {
        ForcePoint a = new(0, 0, 0), b = new(1, 0, 0), c = new(0, 1, 0);
        Assert.Equal(1.0, G.PointTriangleDistance(new(0.2, 0.2, 1), a, b, c), 12);          // грань
        Assert.Equal(1.0, G.PointTriangleDistance(new(2, 0, 0), a, b, c), 12);             // вершина b
        Assert.Equal(Math.Sqrt(2), G.PointTriangleDistance(new(-1, -1, 0), a, b, c), 12);  // вершина a
        Assert.Equal(0.2 / Math.Sqrt(2), G.PointTriangleDistance(new(0.6, 0.6, 0), a, b, c), 12); // ребро bc
        Assert.Equal(1.0, G.PointTriangleDistance(new(0.5, -1, 0), a, b, c), 12);          // ребро ab
    }

    [Fact]
    public void PointTriangleDistance_DegenerateTriangle_UsesSegments()
    {
        ForcePoint a = new(0, 0, 0), b = new(1, 0, 0), c = new(2, 0, 0);
        Assert.Equal(1.0, G.PointTriangleDistance(new(1, 1, 0), a, b, c), 12);
    }

    /// <summary>Совпавшие вершины давали 0/0 → NaN, и адаптация по φ молча прекращала дробить интервал.</summary>
    [Fact]
    public void PointTriangleDistance_CoincidentVertices_Finite()
    {
        ForcePoint a = new(0, 0, 0), c = new(0, 1, 0);
        Assert.Equal(1.0, G.PointTriangleDistance(new(0.5, 0, 1), a, a, c), 12);
        Assert.Equal(1.0, G.PointTriangleDistance(new(0, 0.5, 1), a, c, c), 12);
        Assert.Equal(Math.Sqrt(2), G.PointTriangleDistance(new(1, 1, 0), a, a, a), 12);
    }

    [Fact]
    public void SignedVolume_UnitTetrahedronOutward()
    {
        ForcePoint[] v = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)];
        (int, int, int)[] t = [(0, 2, 1), (0, 1, 3), (0, 3, 2), (1, 2, 3)];
        Assert.Equal(1.0 / 6, G.SignedVolume(v, t), 12);
    }
}
