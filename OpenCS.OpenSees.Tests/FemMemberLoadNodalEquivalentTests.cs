using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

public sealed class FemMemberLoadNodalEquivalentTests
{
    const double L = 4.0;

    static FemLinearDistributedLoad Uniform(double wx, double wy, double wz) => new(1, wy, wz, wx, wy, wz, wx, 0, 1);

    [Fact]
    public void UniformWy_GivesHalfShearAndTwelfthMoments()
    {
        const double q = -3_000;

        var p = FemMemberLoadNodalEquivalent.Distributed(Uniform(0, q, 0), L);

        Assert.Equal(q * L / 2, p[1], 9);
        Assert.Equal(q * L / 2, p[7], 9);
        Assert.Equal(q * L * L / 12, p[5], 9);
        Assert.Equal(-q * L * L / 12, p[11], 9);
        Assert.Equal(0, p[2], 12);
        Assert.Equal(0, p[4], 12);
    }

    [Fact]
    public void UniformWz_MomentsHaveOppositeSign()
    {
        const double q = 2_500;

        var p = FemMemberLoadNodalEquivalent.Distributed(Uniform(0, 0, q), L);

        Assert.Equal(q * L / 2, p[2], 9);
        Assert.Equal(q * L / 2, p[8], 9);
        Assert.Equal(-q * L * L / 12, p[4], 9);
        Assert.Equal(q * L * L / 12, p[10], 9);
        Assert.Equal(0, p[5], 12);
    }

    [Fact]
    public void UniformWx_SplitsAxialHalves()
    {
        var p = FemMemberLoadNodalEquivalent.Distributed(Uniform(1_000, 0, 0), L);

        Assert.Equal(2_000, p[0], 9);
        Assert.Equal(2_000, p[6], 9);
    }

    [Fact]
    public void PartialTrapezoid_MatchesIndependentSimpsonIntegral()
    {
        var load = new FemLinearDistributedLoad(1, WyStart: 1_000, WzStart: -700, WxStart: 300,
            WyEnd: 2_600, WzEnd: 400, WxEnd: -100, AOverL: 0.2, BOverL: 0.7);

        var p = FemMemberLoadNodalEquivalent.Distributed(load, L);

        var expected = Simpson(load, 2000);
        for (int k = 0; k < 12; k++)
            Assert.True(Math.Abs(p[k] - expected[k]) <= 1e-10 * Math.Max(1, Math.Abs(expected[k])), $"k={k}: {p[k]} vs {expected[k]}");
    }

    [Fact]
    public void PointLoad_MatchesClassicFixedEndFormulas()
    {
        const double P = -10_000, a = 1.0, b = L - a;
        var load = new FemLinearPointLoad(1, Py: P, Pz: 0, Px: 0, XOverL: a / L);

        var p = FemMemberLoadNodalEquivalent.Point(load, L);

        Assert.Equal(P * b * b * (3 * a + b) / (L * L * L), p[1], 9);
        Assert.Equal(P * a * b * b / (L * L), p[5], 9);
        Assert.Equal(P * a * a * (a + 3 * b) / (L * L * L), p[7], 9);
        Assert.Equal(-P * a * a * b / (L * L), p[11], 9);
    }

    [Theory]
    [InlineData(0, 0, 0, 5, 0, 0, 0)]
    [InlineData(0, 0, 0, 3, 2, 1, 0)]
    [InlineData(0, 0, 0, 3, 2, 1, 30)]
    [InlineData(1, 2, 0, 1, 2, 4, 0)]
    [InlineData(1, 2, 0, 1, 2, 4, 30)]
    public void LocalAxes_MatchFemLocalAxisFrame(double xi, double yi, double zi, double xj, double yj, double zj, double beta)
    {
        var i = new FemLinearNode(1, xi, yi, zi, new bool[6]);
        var j = new FemLinearNode(2, xj, yj, zj, new bool[6]);

        var (x, y, z) = FemMemberLoadNodalEquivalent.LocalAxes(i, j, FemLocalAxis.Vecxz(i, j, beta));
        var expected = FemLocalAxis.LocalFrame(i, j, beta);

        AssertVector(expected.X, x);
        AssertVector(expected.Y, y);
        AssertVector(expected.Z, z);
    }

    [Fact]
    public void Convert_RemovesElementLoadsAndKeepsEquilibrium()
    {
        var i = new FemLinearNode(1, 0, 0, 0, [true, true, true, true, true, true]);
        var j = new FemLinearNode(2, 3, 2, 1, new bool[6]);
        var vecxz = FemLocalAxis.Vecxz(i, j, 25);
        double length = Math.Sqrt(14);
        var distributed = new FemLinearDistributedLoad(7, 1_000, -500, 200, 1_500, 800, 200, 0.1, 0.9);
        var point = new FemLinearPointLoad(7, Py: -2_000, Pz: 900, Px: 300, XOverL: 0.35);
        var model = new FemNonlinearModel
        {
            Nodes = [i, j],
            Sections = new Dictionary<int, OpenSeesSectionModel> { [1] = new() },
            Elements = [new FemNonlinearElement(7, 1, 2, 1, 5, vecxz)],
            Stages =
            [
                new FemNonlinearStage { Tag = "s0", Loads = [new FemLinearNodalLoad(2, 5, 0, 0, 0, 0, 0)], DistributedLoads = [distributed] },
                new FemNonlinearStage { Tag = "s1", PointLoads = [point], MaxLoadFactor = 1 }
            ],
            GeomTransfKind = "Corotational"
        };

        var (converted, equivalents) = FemMemberLoadNodalEquivalent.Convert(model);

        Assert.All(converted.Stages, s => { Assert.Empty(s.DistributedLoads); Assert.Empty(s.PointLoads); });
        Assert.Equal("Corotational", converted.GeomTransfKind);
        Assert.Equal(1.0, converted.Stages[1].MaxLoadFactor);
        Assert.Equal([0, 1], equivalents.Select(e => e.StageIndex));
        Assert.All(equivalents, e => Assert.Equal(7, e.ElementTag));
        Assert.Contains(converted.Stages[0].Loads, l => l.NodeTag == 2 && l.Fx == 5);

        var (x, y, z) = FemMemberLoadNodalEquivalent.LocalAxes(i, j, vecxz);
        // Стадия 0: равнодействующая трапеции и её момент относительно узла i.
        AssertEquilibrium(converted.Stages[0].Loads.Skip(1).ToList(), i, j,
            Resultants(distributed, length, x, y, z));
        // Стадия 1: сосредоточенная сила.
        var force = Combine(point.Px, point.Py, point.Pz, x, y, z);
        AssertEquilibrium(converted.Stages[1].Loads, i, j,
            (force, Cross(Scale(Sub(j, i), point.XOverL), force)));
    }

    [Fact]
    public void Convert_WithoutElementLoads_ReturnsSameModel()
    {
        var model = new FemNonlinearModel { Stages = [new FemNonlinearStage { Loads = [new FemLinearNodalLoad(1, 1, 0, 0, 0, 0, 0)] }] };

        var (converted, equivalents) = FemMemberLoadNodalEquivalent.Convert(model);

        Assert.Same(model, converted);
        Assert.Empty(equivalents);
    }

    [Fact]
    public void Equivalent_ValidatesAndCopiesInput()
    {
        Assert.Throws<ArgumentException>(() => new FemElementLoadEquivalent(0, 1, new double[5], new double[6]));
        Assert.Throws<ArgumentException>(() => new FemElementLoadEquivalent(0, 1, [0, 0, double.NaN, 0, 0, 0], new double[6]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FemElementLoadEquivalent(-1, 1, new double[6], new double[6]));

        var source = new double[] { 1, 2, 3, 4, 5, 6 };
        var equivalent = new FemElementLoadEquivalent(0, 1, source, new double[6]);
        source[0] = 99;
        Assert.Equal(1, equivalent.LocalI[0]);
    }

    // ── помощники ───────────────────────────────────────────────────────────

    /// <summary>Независимый путь: составная формула Симпсона для ∫q·N по [a·L, b·L].</summary>
    static double[] Simpson(FemLinearDistributedLoad load, int intervals)
    {
        var p = new double[12];
        double a = load.AOverL * L, b = load.BOverL * L, h = (b - a) / intervals;
        for (int k = 0; k <= intervals; k++)
        {
            double xx = a + k * h, t = (xx - a) / (b - a), xi = xx / L;
            double w = (k == 0 || k == intervals ? 1 : k % 2 == 1 ? 4 : 2) * h / 3;
            double qx = load.WxStart + (load.WxEnd - load.WxStart) * t;
            double qy = load.WyStart + (load.WyEnd - load.WyStart) * t;
            double qz = load.WzStart + (load.WzEnd - load.WzStart) * t;
            double h1 = 1 - 3 * xi * xi + 2 * xi * xi * xi, h2 = L * (xi - 2 * xi * xi + xi * xi * xi);
            double h3 = 3 * xi * xi - 2 * xi * xi * xi, h4 = L * (xi * xi * xi - xi * xi);
            p[0] += w * qx * (1 - xi); p[6] += w * qx * xi;
            p[1] += w * qy * h1; p[5] += w * qy * h2; p[7] += w * qy * h3; p[11] += w * qy * h4;
            p[2] += w * qz * h1; p[4] -= w * qz * h2; p[8] += w * qz * h3; p[10] -= w * qz * h4;
        }
        return p;
    }

    /// <summary>Равнодействующая распределённой нагрузки (глобальные оси) и её момент относительно узла i.</summary>
    static ((double X, double Y, double Z) Force, (double X, double Y, double Z) Moment) Resultants(
        FemLinearDistributedLoad load, double length,
        (double X, double Y, double Z) x, (double X, double Y, double Z) y, (double X, double Y, double Z) z)
    {
        (double X, double Y, double Z) force = (0, 0, 0), moment = (0, 0, 0);
        const int n = 2000;
        double a = load.AOverL, b = load.BOverL, d = (b - a) / n;
        for (int k = 0; k < n; k++)
        {
            double t = (k + 0.5) / n, xi = a + (b - a) * t;
            var q = Combine(load.WxStart + (load.WxEnd - load.WxStart) * t, load.WyStart + (load.WyEnd - load.WyStart) * t,
                load.WzStart + (load.WzEnd - load.WzStart) * t, x, y, z);
            var dq = Scale(q, d * length);
            force = Add(force, dq);
            moment = Add(moment, Cross(Scale(x, xi * length), dq));
        }
        return (force, moment);
    }

    static void AssertEquilibrium(IReadOnlyList<FemLinearNodalLoad> loads, FemLinearNode i, FemLinearNode j,
        ((double X, double Y, double Z) Force, (double X, double Y, double Z) Moment) expected)
    {
        (double X, double Y, double Z) force = (0, 0, 0), moment = (0, 0, 0);
        foreach (var l in loads)
        {
            var f = (l.Fx, l.Fy, l.Fz);
            force = Add(force, f);
            var arm = l.NodeTag == j.Tag ? Sub(j, i) : (0.0, 0.0, 0.0);
            moment = Add(moment, Add((l.Mx, l.My, l.Mz), Cross(arm, f)));
        }
        AssertVector(expected.Force, force, 1e-6);
        AssertVector(expected.Moment, moment, 1e-6);
    }

    static void AssertVector((double X, double Y, double Z) expected, (double X, double Y, double Z) actual, double tol = 1e-12)
    {
        double scale = Math.Max(1, Math.Max(Math.Abs(expected.X), Math.Max(Math.Abs(expected.Y), Math.Abs(expected.Z))));
        Assert.True(Math.Abs(expected.X - actual.X) <= tol * scale && Math.Abs(expected.Y - actual.Y) <= tol * scale
            && Math.Abs(expected.Z - actual.Z) <= tol * scale, $"ожидалось {expected}, получено {actual}");
    }

    static (double X, double Y, double Z) Combine(double a, double b, double c,
        (double X, double Y, double Z) x, (double X, double Y, double Z) y, (double X, double Y, double Z) z) =>
        (a * x.X + b * y.X + c * z.X, a * x.Y + b * y.Y + c * z.Y, a * x.Z + b * y.Z + c * z.Z);

    static (double X, double Y, double Z) Sub(FemLinearNode j, FemLinearNode i) => (j.X - i.X, j.Y - i.Y, j.Z - i.Z);
    static (double X, double Y, double Z) Add((double X, double Y, double Z) a, (double X, double Y, double Z) b) => (a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    static (double X, double Y, double Z) Scale((double X, double Y, double Z) a, double k) => (a.X * k, a.Y * k, a.Z * k);
    static (double X, double Y, double Z) Cross((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
        (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}
