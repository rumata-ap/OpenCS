using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Loads;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Fem.Fragment;

/// <summary>Часть нагрузки КЭ внутри границ фрагмента по виду (спека 2026-11-05 «нагрузки КЭ на гранях по виду», разд. 3).</summary>
public sealed class FragmentLoadInsideTests
{
    static PlanarVector3 V(double x, double y, double z) => new(x, y, z);

    static (PlanarVector3 F, PlanarVector3 M) Sum(IEnumerable<FragmentLoadTerm> terms)
    {
        PlanarVector3 f = PlanarVector3.Zero, m = PlanarVector3.Zero;
        foreach (var t in terms) { f += t.F; m += t.At.Cross(t.F) + t.M; }
        return (f, m);
    }

    /// <summary>Стена x ∈ [0, 1], z ∈ [0, 1] в плоскости y = 0 (хранение «1 2 4 3»).</summary>
    static FemElementGeometry Wall() => FemElementGeometry.FromPoints([V(0, 0, 0), V(1, 0, 0), V(0, 0, 1), V(1, 0, 1)])!;

    [Fact]
    public void Point_InsideOutside()
    {
        var b = new FragmentBounds(0.5, 3, null, 1e-6);
        Assert.Single(FragmentLoadInside.Terms(b, new FemPointPiece(V(0, 0, 1), V(0, 0, -1), V(0, 0, 0))));
        Assert.Empty(FragmentLoadInside.Terms(b, new FemPointPiece(V(0, 0, 0.2), V(0, 0, -1), V(0, 0, 0))));
    }

    /// <summary>Отрезок z 0…2, q_x = 1 + s, граница z₁ = 0,5: ΣFx = ∫(1+s) = 3,375; ΣM_y = ∫ s(1+s) = 4,5.</summary>
    [Fact]
    public void Line_ClippedByPlane_ExactResultant()
    {
        var b = new FragmentBounds(0.5, 3, null, 1e-6);
        var (f, m) = Sum(FragmentLoadInside.Terms(b, new FemLinePiece(V(0, 0, 0), V(0, 0, 2), V(1, 0, 0), V(3, 0, 0))));
        Assert.Equal(3.375, f.X, 12);
        Assert.Equal(4.5, m.Y, 12);
        Assert.Equal(0, f.Z, 12);
    }

    /// <summary>Стена, q_y по узлам = z, граница z₁ = 0,5: ΣFy = ∫∫ z = 0,375; ΣM_x = −∫∫ z² = −0,875/3.</summary>
    [Fact]
    public void Surface_ClippedQ4_ExactIntegral()
    {
        var g = Wall();
        var q = g.Points.Select(p => V(0, p.Z, 0)).ToArray();
        var (f, m) = Sum(FragmentLoadInside.Terms(new FragmentBounds(0.5, 2, null, 1e-6), new FemSurfacePiece(g, q)));
        Assert.Equal(0.375, f.Y, 10);
        Assert.Equal(-0.875 / 3, m.X, 10);
    }

    /// <summary>Равномерная по площади (собственный вес) — доля площади <see cref="FragmentElementShare"/> в центре части.</summary>
    [Fact]
    public void UniformSurface_EqualsShare()
    {
        var g = Wall();
        var bounds = new FragmentBounds(0.3, 2, null, 1e-6);
        var (f, m) = Sum(FragmentLoadInside.Terms(bounds, new FemSurfacePiece(g, Enumerable.Repeat(V(0, 0, -100), 4).ToArray())));
        var part = FragmentElementShare.Of(bounds, g.Contour.Select(i => g.Points[i]).ToList())!.Value;
        Assert.Equal(-100 * part.Share, f.Z, 10);
        Assert.Equal(part.Centroid.Cross(V(0, 0, -100 * part.Share)).Y, m.Y, 10);
    }
}
