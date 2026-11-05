using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Fem.Fragment.Masks;

public sealed class FragmentNodalDensityTests
{
    static PlanarVector3 V(double x, double y, double z) => new(x, y, z);

    static FragmentMaskCutInput Cut(params (PlanarVector3 P, PlanarVector3 Q)[] segments) =>
        new(new FragmentCut("W", FragmentCutKind.Edge, FragmentCutSide.Bottom, 0, null, segments), null, V(0, 1, 0));

    /// <summary>Сэмплы кромки по плотностям отрезков (s сквозная, как у экстрактора).</summary>
    static List<FragmentMaskSample> Samples(FragmentMaskCutInput cut, IReadOnlyList<FragmentNodalTerm> terms)
    {
        var projected = FragmentNodalDensity.Project(cut.Cut.Segments, terms);
        var samples = new List<FragmentMaskSample>();
        double offset = 0;
        for (int j = 0; j < cut.Cut.Segments.Count; j++)
        {
            var (a, b) = cut.Cut.Segments[j];
            foreach (var (t, f) in FragmentNodalDensity.Density((b - a).Length, (b - a).Normalize(), projected[j]))
                samples.Add(new FragmentMaskSample(offset + t, null, f));
            offset += (b - a).Length;
        }
        return samples;
    }

    static void AssertResultant(FragmentMaskCutInput cut, IReadOnlyList<FragmentNodalTerm> terms, PlanarVector3 origin)
    {
        var f = PlanarVector3.Zero;
        var m = PlanarVector3.Zero;
        foreach (var t in terms) { f += t.F; m += t.M + (t.At - origin).Cross(t.F); }
        var (rf, rm) = FragmentMaskBalance.Resultant(cut, Samples(cut, terms), origin)!.Value;
        double scale = Math.Max(f.Length, 1) * 10;
        Assert.True((rf - f).Length <= 1e-9 * scale, $"ΣF: {rf} вместо {f}");
        Assert.True((rm - m).Length <= 1e-9 * scale, $"ΣM: {rm} вместо {m}");
    }

    [Fact]
    public void InteriorEndAndMomentTerms_ResultantExact()
    {
        var cut = Cut((V(0, 0, 0), V(2, 0, 0)));
        AssertResultant(cut, [new(V(0.5, 0, 0), V(0, 0, 100), V(0, 0, 0))], V(0.3, -1, 2));
        AssertResultant(cut, [new(V(0, 0, 0), V(10, 20, 100), V(5, 0, 0))], V(0.3, -1, 2));
        AssertResultant(cut, [new(V(2, 0, 0), V(0, 0, -50), V(0, 7, 0)), new(V(1.2, 0, 0), V(3, 0, 0), V(0, 0, 0))], V(1, 1, 1));
    }

    [Fact]
    public void OffLineTerm_MomentOfArm_AndNearestSegment()
    {
        var cut = Cut((V(0, 0, 0), V(1, 0, 0)), (V(1, 0, 0), V(1, 2, 0)));
        // Точка у второго отрезка, на 0,3 м вне линии: сила уходит на него с моментом плеча.
        var terms = new[] { new FragmentNodalTerm(V(1.3, 1.0, 0), V(0, 0, 100), V(0, 0, 0)) };
        var projected = FragmentNodalDensity.Project(cut.Cut.Segments, terms);
        Assert.Empty(projected[0]);
        var single = Assert.Single(projected[1]);
        Assert.Equal(1.0, single.T, 12);
        Assert.True((single.M - V(0.3, 0, 0).Cross(V(0, 0, 100))).Length < 1e-9, $"M′ = {single.M}");
        AssertResultant(cut, terms, V(0, 0, 0));
    }

    [Fact]
    public void ClosePoints_Merged_ResultantKept()
    {
        var cut = Cut((V(0, 0, 0), V(2, 0, 0)));
        var terms = new[]
        {
            new FragmentNodalTerm(V(1, 0, 0), V(0, 0, 100), V(0, 0, 0)),
            new FragmentNodalTerm(V(1 + 5e-7, 0, 0), V(0, 50, 100), V(1, 0, 0)),
        };
        Assert.Single(FragmentNodalDensity.Project(cut.Cut.Segments, terms)[0]);
        AssertResultant(cut, terms, V(0, 0, 0));
    }

    [Fact]
    public void UniformNodalForces_GiveUniformDensity()
    {
        var cut = Cut((V(0, 0, 0), V(3, 0, 0)));
        var terms = Enumerable.Range(0, 7).Select(i => new FragmentNodalTerm(V(0.5 * i, 0, 0),
            V(0, 0, i is 0 or 6 ? 500 : 1000), V(0, 0, 0))).ToList();
        var density = FragmentNodalDensity.Density(3, V(1, 0, 0), FragmentNodalDensity.Project(cut.Cut.Segments, terms)[0]);
        Assert.All(density, d => Assert.Equal(2000, d.F.Z, 9));
        Assert.Equal(2000, FragmentNodalDensity.At(density, 1.3).Z, 9);
    }
}
