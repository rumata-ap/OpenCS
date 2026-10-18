using CScore.ShellSimpl;
using Xunit;

namespace CScore.Tests.ShellSimpl;

/// <summary>Линейная задача ортогональной сетки: <see cref="OrthogonalMeshLp"/>.</summary>
public sealed class OrthogonalMeshLpTests
{
    static (double, double)[] Facets(Func<double, double> r, double step = 10.0)
    {
        int n = (int)Math.Round(180.0 / step);
        return Enumerable.Range(0, n).Select(i => (i * step, r(i * step))).ToArray();
    }

    static double Provided(double asx, double asy, double thetaDeg)
    {
        double a = thetaDeg * Math.PI / 180.0, c = Math.Cos(a), s = Math.Sin(a);
        return asx * c * c + asy * s * s;
    }

    // Перебор по сетке x: при заданном x наименьший допустимый y = max (r − c²·x)/s².
    static double GridMinimum((double Theta, double R)[] d, double xMax, int n)
    {
        double best = double.PositiveInfinity;
        for (int i = 0; i <= n; i++)
        {
            double x = xMax * i / n, y = 0.0;
            bool ok = true;
            foreach (var (theta, r) in d)
            {
                double a = theta * Math.PI / 180.0, c2 = Math.Cos(a) * Math.Cos(a), s2 = Math.Sin(a) * Math.Sin(a);
                double rest = r - c2 * x;
                if (rest <= 0) continue;
                if (s2 < 1e-12) { ok = false; break; }
                y = Math.Max(y, rest / s2);
            }
            if (ok) best = Math.Min(best, x + y);
        }
        return best;
    }

    [Fact]
    public void OnlyZeroFacet_GivesXOnly()
    {
        var s = OrthogonalMeshLp.Solve(Facets(t => t == 0.0 ? 5.0 : 0.0));
        Assert.Equal(5.0, s.Asx, 9);
        Assert.Equal(0.0, s.Asy, 9);
        Assert.Equal([0.0], s.ActiveThetas);
    }

    [Fact]
    public void ConstantDemand_GivesEqualAreas()
    {
        var s = OrthogonalMeshLp.Solve(Facets(_ => 3.0));
        Assert.Equal(3.0, s.Asx, 9);
        Assert.Equal(3.0, s.Asy, 9);
        Assert.Contains(0.0, s.ActiveThetas);
        Assert.Contains(90.0, s.ActiveThetas);
    }

    [Fact]
    public void NoDemand_GivesZero()
    {
        var s = OrthogonalMeshLp.Solve(Facets(_ => -1.0));
        Assert.Equal(0.0, s.Asx);
        Assert.Equal(0.0, s.Asy);
        Assert.Empty(s.ActiveThetas);
    }

    // Только диагональная фасетка: ребро оптимума параллельно целевой — берётся равномерная сетка.
    [Fact]
    public void DiagonalFacet_GivesUniformMesh()
    {
        var s = OrthogonalMeshLp.Solve([(45.0, 2.0)]);
        Assert.Equal(4.0, s.Asx + s.Asy, 9);
        Assert.Equal(s.Asx, s.Asy, 9);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void RandomDemands_MatchGridSearch_AndAreMinimal(int seed)
    {
        var rnd = new Random(seed);
        // Огибающая требований вида a + b·cos2θ + c·sin2θ + шум, часть фасеток без требований.
        double a = rnd.NextDouble() * 5, b = rnd.NextDouble() * 6 - 3, c = rnd.NextDouble() * 6 - 3;
        var d = Facets(t =>
        {
            double r = t * Math.PI / 180.0;
            return a + b * Math.Cos(2 * r) + c * Math.Sin(2 * r) + rnd.NextDouble() - 0.5;
        }, rnd.Next(2) == 0 ? 10.0 : 5.0);

        var s = OrthogonalMeshLp.Solve(d);
        foreach (var (theta, r) in d)
            Assert.True(Provided(s.Asx, s.Asy, theta) >= r - 1e-9, $"θ = {theta}: не выполнено");

        double grid = GridMinimum(d, 20.0, 200_000);
        Assert.True(s.Asx + s.Asy <= grid + 1e-9, $"ЛП {s.Asx + s.Asy} хуже перебора {grid}");
        Assert.True(s.Asx + s.Asy >= grid - 1e-3, $"ЛП {s.Asx + s.Asy} лучше перебора {grid} — нарушение");

        // Минимальность: уменьшение любой ненулевой компоненты на 1 % нарушает хотя бы одно требование.
        if (s.Asx > 1e-9)
            Assert.Contains(d, f => Provided(0.99 * s.Asx, s.Asy, f.Item1) < f.Item2 - 1e-12);
        if (s.Asy > 1e-9)
            Assert.Contains(d, f => Provided(s.Asx, 0.99 * s.Asy, f.Item1) < f.Item2 - 1e-12);
        Assert.NotEmpty(s.ActiveThetas);
    }
}
