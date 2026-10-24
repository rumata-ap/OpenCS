using CScore.LimitSurface;
using Xunit;

namespace CScore.Tests.LimitSurface;

public class StrainDomainMesherTests
{
    static readonly LimitSurfaceOptions Coarse = new() { InitialMeridians = 12, ThetaIntervals = 12, RefineTol = 0 };

    static LimitSurfaceMesh Build(LimitSurfaceOptions? o = null, CrossSection? s = null) =>
        new StrainDomainMesher(s ?? TestSections.RectWithBottomRebar(), new UlsStrainCriterion(CalcType.C, ten: false), o).Build();

    [Fact]
    public void ClosedManifold_EulerTwo_Outward()
    {
        var m = Build(Coarse);
        var edges = new Dictionary<(int, int), int>();
        foreach (var (a, b, c) in m.Triangles)
        {
            Assert.True(a != b && b != c && a != c);
            foreach (var (i, j) in new[] { (a, b), (b, c), (c, a) })
            {
                var k = i < j ? (i, j) : (j, i);
                edges[k] = edges.GetValueOrDefault(k) + 1;
            }
        }
        Assert.All(edges.Values, n => Assert.Equal(2, n));
        Assert.Equal(2, m.Vertices.Count - edges.Count + m.Triangles.Count);
        Assert.True(SurfaceMeshGeometry.SignedVolume(m.Vertices, m.Triangles) > 0);
    }

    [Fact]
    public void KinksInserted_AndAnchoredBetweenMeridians()
    {
        var m = Build(Coarse);
        Assert.True(m.Vertices.Count > 2 + 12 * 11);
        var edgeSet = new HashSet<(int, int)>();
        foreach (var (a, b, c) in m.Triangles)
            foreach (var (i, j) in new[] { (a, b), (b, c), (c, a) }) edgeSet.Add(i < j ? (i, j) : (j, i));
        int checkedPairs = 0;
        for (int k = 0; k < m.MeridianCount; k++)
        {
            int k2 = (k + 1) % m.MeridianCount;
            var on = (int mer) => Enumerable.Range(0, m.Vertices.Count)
                .Where(v => m.MeridianOf[v] == mer && m.Transitions[v] is not null)
                .GroupBy(v => m.Transitions[v]!.Value).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
            var a = on(k); var b = on(k2);
            foreach (var (tr, va) in a)
                if (b.TryGetValue(tr, out var vb)) { Assert.Contains(va < vb ? (va, vb) : (vb, va), edgeSet); checkedPairs++; }
        }
        Assert.True(checkedPairs > 0);
    }

    [Fact]
    public void ParallelAndSequential_Identical()
    {
        var a = Build(new LimitSurfaceOptions { InitialMeridians = 12, ThetaIntervals = 12, Parallel = true });
        var b = Build(new LimitSurfaceOptions { InitialMeridians = 12, ThetaIntervals = 12, Parallel = false });
        Assert.Equal(a.Vertices, b.Vertices);
        Assert.Equal(a.Triangles, b.Triangles);
    }

    [Fact]
    public void Poles_UniformStrains()
    {
        var section = TestSections.RectWithBottomRebar();
        var m = Build(Coarse, section);
        var cc = section.Areas[0].Material!.GetChars(CalcType.C)!;
        Assert.Equal(cc.Ec0, m.StrainPlanes[1].e0, 12);                 // сжатие: εb0 по (8.53) при r = 1
        Assert.Equal(0, m.StrainPlanes[1].ky); Assert.Equal(0, m.StrainPlanes[1].kz);
        var rc = section.Areas[1].Material!.GetChars(CalcType.C)!;
        double epsSu = double.IsFinite(rc.Et2) && rc.Et2 > 0 ? rc.Et2 : 0.025;
        Assert.Equal(epsSu, m.StrainPlanes[0].e0, 12);                  // растяжение: εs,ult
        double asTot = section.Areas[1].Fibers.Sum(f => f.Area);
        Assert.Equal(rc.Ft * asTot, m.Vertices[0].N, 6);                // σs на площадке = Ft, бетон не работает (ten=false)
        var r1 = section.Compute(m.StrainPlanes[1], CalcType.C, false, computeStiffness: false);
        Assert.Equal(r1.N, m.Vertices[1].N, 9);
    }

    [Fact]
    public void Vertices_LieOnSurface()
    {
        var m = Build();
        double mRef = m.Vertices.Max(v => Math.Sqrt(v.Mx * v.Mx + v.My * v.My));
        double nMin = m.Vertices.Min(v => v.N), nMax = m.Vertices.Max(v => v.N);
        int exact = 0, total = 0;
        foreach (var v in m.Vertices.Skip(2))
        {
            double r = Math.Sqrt(v.Mx * v.Mx + v.My * v.My);
            if (r < 1e-3 * Math.Max(1, Math.Abs(v.N)) || v.N <= nMin || v.N >= nMax) continue;
            var u = m.Utilization(v.N, 0.999 * v.Mx, 0.999 * v.My, UtilizationMode.Moment);
            // Арматура только снизу: при растяжении (N, 0, 0) вне тела (Mx ≈ −0,21·N), и для вершин
            // ближней стороны луч до точки поверхность не пересекает — Кисп = ∞ верно по построению.
            if (!u.RayOriginInside) continue;
            total++;
            if (u.Inside && Math.Abs(u.Ratio - 0.999) <= 1e-6) { exact++; continue; }
            Assert.True(double.IsFinite(u.Ratio) && u.Ratio > 0);
            Assert.True(Math.Abs(1 - 0.999 / u.Ratio) * r < 0.02 * mRef, $"N={v.N} M=({v.Mx};{v.My}) Кисп={u.Ratio}");
        }
        Assert.True(exact >= 0.9 * total, $"{exact}/{total}");
    }

    [Fact]
    public void ThetaAndPhiRefinement_AddNodes()
    {
        var plain = Build(Coarse);
        var refined = Build(new LimitSurfaceOptions { InitialMeridians = 12, ThetaIntervals = 12, RefineTol = 0.002 });
        Assert.NotEmpty(refined.RefineLog);
        Assert.True(refined.MeridianCount > 12);
        // адаптация по θ: средний меридиан длиннее, чем у того же φ без адаптации
        double PerMer(LimitSurfaceMesh m) => (double)(m.Vertices.Count - 2) / m.MeridianCount;
        Assert.True(PerMer(refined) > PerMer(plain));
    }

    [Fact]
    public void NoRebar_Throws()
    {
        var s = TestSections.RectWithBottomRebar();
        var noBars = new CrossSection { Areas = [s.Areas[0]] };
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new StrainDomainMesher(noBars, new UlsStrainCriterion(), Coarse).Build());
        Assert.Contains("не замкнута", ex.Message);
    }

    [Fact]
    public void Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            new StrainDomainMesher(TestSections.RectWithBottomRebar(), new UlsStrainCriterion(), Coarse).Build(cts.Token));
    }
}
