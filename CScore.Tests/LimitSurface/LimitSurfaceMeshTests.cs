using CScore.LimitSurface;
using Xunit;

namespace CScore.Tests.LimitSurface;

public class LimitSurfaceMeshTests
{
    /// <summary>Куб [lo, hi]³ (оси N, Mx, My), 12 треугольников наружу.</summary>
    internal static LimitSurfaceMesh Box(double nLo = -1, double nHi = 1, double m = 1)
    {
        var v = new List<ForcePoint>();
        foreach (var n in new[] { nLo, nHi }) foreach (var x in new[] { -m, m }) foreach (var y in new[] { -m, m })
            v.Add(new ForcePoint(n, x, y));
        int[][] quads = [[0, 1, 3, 2], [4, 6, 7, 5], [0, 4, 5, 1], [2, 3, 7, 6], [0, 2, 6, 4], [1, 5, 7, 3]];
        var t = new List<(int, int, int)>();
        foreach (var q in quads) { t.Add((q[0], q[1], q[2])); t.Add((q[0], q[2], q[3])); }
        if (SurfaceMeshGeometry.SignedVolume(v, t) < 0) t = t.Select(x => (x.Item1, x.Item3, x.Item2)).ToList();
        return new LimitSurfaceMesh(v, t);
    }

    /// <summary>Призма вдоль N из невыпуклого многоугольника (Mx, My), звёздного от (0, 0).</summary>
    internal static LimitSurfaceMesh Prism((double Mx, double My)[] poly, double n0 = -1, double n1 = 1)
    {
        int k = poly.Length;
        var v = new List<ForcePoint>();
        foreach (var n in new[] { n0, n1 }) foreach (var (x, y) in poly) v.Add(new ForcePoint(n, x, y));
        v.Add(new ForcePoint(n0, 0, 0)); v.Add(new ForcePoint(n1, 0, 0));
        var t = new List<(int, int, int)>();
        for (int i = 0; i < k; i++)
        {
            int j = (i + 1) % k;
            t.Add((i, j, k + j)); t.Add((i, k + j, k + i));
            t.Add((2 * k, j, i)); t.Add((2 * k + 1, k + i, k + j));
        }
        if (SurfaceMeshGeometry.SignedVolume(v, t) < 0) t = t.Select(x => (x.Item1, x.Item3, x.Item2)).ToList();
        return new LimitSurfaceMesh(v, t);
    }

    static double Area(IReadOnlyList<ForcePoint> loop, Func<ForcePoint, (double, double)> proj)
    {
        double s = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            var (x1, y1) = proj(loop[i]); var (x2, y2) = proj(loop[(i + 1) % loop.Count]);
            s += x1 * y2 - x2 * y1;
        }
        return Math.Abs(s) / 2;
    }

    [Fact]
    public void SliceAtN_Box_IsSquare()
    {
        var loops = Box().SliceAtN(0.3);
        Assert.Single(loops);
        Assert.Equal(4.0, Area(loops[0], p => (p.Mx, p.My)), 9);
    }

    [Fact]
    public void Slice_FacePlane_IsEmpty() => Assert.Empty(Box().SliceAtN(-1));

    [Fact]
    public void SliceNM_DiagonalThroughVertices()
    {
        var loops = Box().SliceNM(Math.PI / 4);   // плоскость через ось N и направление (1, 1)
        Assert.Single(loops);
        Assert.Equal(2 * 2 * Math.Sqrt(2), Area(loops[0], p => (p.N, (p.Mx + p.My) / Math.Sqrt(2))), 9);
    }

    [Fact]
    public void Utilization_All_Inside()
    {
        var u = Box().Utilization(0.5, 0, 0, UtilizationMode.All);
        Assert.True(u.Inside); Assert.True(u.RayOriginInside);
        Assert.Equal(0.5, u.Ratio, 12);
        Assert.Equal(1.0, u.Capacity!.Value.N, 12);
    }

    [Fact]
    public void Utilization_Moment_ThroughEdge_CountsOnce()
    {
        // Луч из (0.5,0,0) по (0,1,1) упирается в ребро Mx=My=1 при t=2
        var u = Box().Utilization(0.5, 0.5, 0.5, UtilizationMode.Moment);
        Assert.True(u.Inside);
        Assert.Equal(0.5, u.Ratio, 12);
    }

    [Fact]
    public void Utilization_All_ThroughVertex_CountsOnce()
    {
        var u = Box().Utilization(0.5, 0.5, 0.5, UtilizationMode.All);   // вершина (1,1,1) при t=2
        Assert.True(u.Inside);
        Assert.Equal(0.5, u.Ratio, 12);
    }

    [Fact]
    public void Utilization_OriginOutside()
    {
        var box = Box(nLo: 0.5, nHi: 2);
        var a = box.Utilization(1, 0, 0, UtilizationMode.All);
        Assert.True(a.Inside); Assert.False(a.RayOriginInside); Assert.Equal(0.5, a.Ratio, 12);
        var b = box.Utilization(3, 0, 0, UtilizationMode.All);
        Assert.False(b.Inside); Assert.Equal(1.5, b.Ratio, 12);
        var c = box.Utilization(0.25, 0, 0, UtilizationMode.All);
        Assert.False(c.Inside); Assert.True(double.IsPositiveInfinity(c.Ratio)); Assert.Null(c.Capacity);
        var d = box.Utilization(-1, 0, 0, UtilizationMode.All);
        Assert.False(d.Inside); Assert.True(double.IsPositiveInfinity(d.Ratio));
    }

    [Fact]
    public void Utilization_Axial()
    {
        var u = Box().Utilization(-0.25, 0.3, 0.3, UtilizationMode.Axial);   // из (0,0.3,0.3) вдоль −N до N=−1
        Assert.True(u.Inside);
        Assert.Equal(0.25, u.Ratio, 12);
    }

    [Fact]
    public void Utilization_Nonconvex_Prism()
    {
        var star = Prism([(2, 0), (0.5, 0.5), (0, 2), (-2, 0), (0, -2), (0.5, -0.5)]);
        var a = star.Utilization(0, 1, 0, UtilizationMode.Moment);   // через выпуклую вершину (2,0)
        Assert.True(a.Inside); Assert.Equal(0.5, a.Ratio, 12);
        var b = star.Utilization(0, 1, 1, UtilizationMode.Moment);   // через вогнутую вершину (0.5,0.5)
        Assert.False(b.Inside); Assert.Equal(2.0, b.Ratio, 12);
        var loops = star.SliceAtN(0.1);
        Assert.Single(loops);
        Assert.Equal(12, loops[0].Count);   // 6 вертикальных рёбер + 6 диагоналей боковых граней
    }

    [Fact]
    public void Utilization_ZeroDirection()
    {
        var u = Box().Utilization(0.5, 0, 0, UtilizationMode.Moment);
        Assert.True(u.Inside); Assert.Equal(0, u.Ratio);
    }
}
