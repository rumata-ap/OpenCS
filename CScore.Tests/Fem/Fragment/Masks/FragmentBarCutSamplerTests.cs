using CScore.Fem;
using CScore.Fem.Fragment.Masks;
using CScore.Planar;
using CScore.Submodel;
using Xunit;

namespace CScore.Tests.Fem.Fragment.Masks;

public sealed class FragmentBarCutSamplerTests
{
    static PlanarVector3 V(double x, double y, double z) => new(x, y, z);

    static void Near(Dof6 expected, Dof6 actual, double tol)
    {
        for (int i = 0; i < 6; i++) Assert.True(Math.Abs(expected[i] - actual[i]) <= tol, $"DOF {i}: {expected[i]} ≠ {actual[i]}");
    }

    /// <summary>Жёсткое смещение и поворот: u(p) = t + θ × (p − o) воспроизводится точно в любой точке.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.3)]
    [InlineData(1.0)]
    public void RigidMotion_ReproducedExactly(double xi)
    {
        var o = V(1, 2, 0);
        var t = V(1e-3, -2e-3, 5e-4);
        var th = V(2e-4, -1e-4, 3e-4);
        Dof6 At(PlanarVector3 p) => Dof6.FromParts(t + th.Cross(p - o), th);
        PlanarVector3 pi = V(0, 0, 0), pj = V(0.3, 0.4, 3);

        var u = FragmentBarCutSampler.Displacement(pi, pj, 25, At(pi), At(pj), xi);

        Near(At(pi + (pj - pi) * xi), u, 1e-12);
    }

    /// <summary>Консоль EI под силой на конце: прогиб кубический — Эрмит точен; поворот — производная.</summary>
    [Fact]
    public void CubicDeflection_Exact()
    {
        // Ось X, L = 2; w(x) = P x²(3L − x)/(6EI), θ = P x(2L − x)/(2EI) — направление местной оси Y (глобальная Z).
        const double L = 2, k = 1e-3; // k = P/EI
        double W(double x) => k * x * x * (3 * L - x) / 6;
        double Th(double x) => k * x * (2 * L - x) / 2;
        PlanarVector3 pi = V(0, 0, 0), pj = V(L, 0, 0);
        // Местные оси: x = X, y = Z, z = x × y = −Y; прогиб вдоль y = Z, поворот вокруг местной z = −Y:
        // θz_local = dw/dx → глобальный поворот вокруг Y = −θ.
        var uj = new Dof6(0, 0, W(L), 0, -Th(L), 0);

        var u = FragmentBarCutSampler.Displacement(pi, pj, 0, Dof6.Zero, uj, 0.4);

        Near(new Dof6(0, 0, W(0.8), 0, -Th(0.8), 0), u, 1e-15);
    }
}
