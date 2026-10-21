using CScore.Fem.Fields;
using Xunit;

namespace CScore.Tests.Fem.Fields;

public class KdTree3Tests
{
    [Fact]
    public void Nearest_And_KNearest_MatchBruteForce()
    {
        var rnd = new Random(7);
        var pts = Enumerable.Range(0, 500).Select(_ => new FieldVec3(rnd.NextDouble() * 50, rnd.NextDouble() * 30, rnd.NextDouble() * 5)).ToList();
        var tree = new KdTree3(pts);
        for (int q = 0; q < 50; q++)
        {
            var p = new FieldVec3(rnd.NextDouble() * 60 - 5, rnd.NextDouble() * 40 - 5, rnd.NextDouble() * 5);
            var brute = pts.Select((v, i) => (i, d: (v - p).Length)).OrderBy(x => x.d).ToList();
            var (idx, dist) = tree.Nearest(p);
            Assert.Equal(brute[0].d, dist, 12);
            Assert.Equal(brute[0].d, (pts[idx] - p).Length, 12);
            var k = tree.KNearest(p, 5);
            Assert.Equal(brute.Take(5).Select(x => x.d), k.Select(x => x.Distance), new DoubleTolerance(1e-12));
        }
    }

    [Fact]
    public void Empty_ReturnsMinusOne()
    {
        var (idx, dist) = new KdTree3([]).Nearest(new FieldVec3(0, 0, 0));
        Assert.Equal(-1, idx);
        Assert.True(double.IsPositiveInfinity(dist));
    }

    sealed class DoubleTolerance(double tol) : IEqualityComparer<double>
    {
        public bool Equals(double a, double b) => Math.Abs(a - b) <= tol;
        public int GetHashCode(double v) => 0;
    }
}
