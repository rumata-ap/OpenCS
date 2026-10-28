using CScore.Fem.Fragment.Masks;
using CScore.Planar;
using CScore.Submodel;
using Xunit;
using static CScore.Tests.Fem.Fragment.Masks.FragmentMaskFixtures;

namespace CScore.Tests.Fem.Fragment.Masks;

public sealed class FragmentShellForceRecoveryTests
{
    static readonly PlanarVector3 X = V(1, 0, 0), Z = V(0, 0, 1);

    /// <summary>
    /// Стена XZ, оси КЭ (X, Z), нормаль КЭ −Y; линия с нормалью −Z (низ): сила = (−Nxy, Qy, −Ny), момент = (My, 0, −Mxy).
    /// Тот же физический КЭ с осями (X, −Z) и пересчитанными компонентами даёт то же действие.
    /// </summary>
    [Fact]
    public void Traction_InvariantToElementOrientation()
    {
        double[] v = [10, -1400, 30, 4, 50, 6, 7, 80]; // Nx Ny Nxy Mx My Mxy Qx Qy
        var a = FragmentShellForceRecovery.Traction(new FragmentShellForces(X, Z, v), V(0, 0, -1));
        double[] flipped = [v[0], v[1], -v[2], -v[3], -v[4], v[5], -v[6], v[7]];
        var b = FragmentShellForceRecovery.Traction(new FragmentShellForces(X, Z * -1, flipped), V(0, 0, -1));

        var expected = new Dof6(-30, 80, 1400, 50, 0, -6);
        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(expected[i], a[i], 9);
            Assert.Equal(expected[i], b[i], 9);
        }
    }

    /// <summary>
    /// Линейное по высоте поле Ny(z) = −(1000 + 500 z), My(z) = 200 z, заданное в центрах КЭ: в точках рассечения
    /// z = 0,8 (не через центры 0,75 / 1,25) восстанавливается точно — значение «своего» КЭ было бы −1375, а не −1400.
    /// </summary>
    [Fact]
    public void Recover_LinearFieldExact_OffCentroid()
    {
        var (nodes, elements) = Wall(2, 2, 4, 4, flip: true);
        var g = FragmentWallCutSampler.Prepare(V(0, 0, 0.8), V(2, 0, 0.8), V(0, 1, 0), true, Shells(nodes, elements), 1e-3);
        var forces = g.Plane.ToDictionary(e => e.Tag, e =>
        {
            double z = e.Centroid.Z;
            var (ax, ay, az) = e.Geometry.ShellFrame();
            double sn = az.Dot(V(0, -1, 0)); // ±1: нормаль КЭ
            // Физика: σ_zz-ресурс −(1000+500z), изгиб вокруг X: My(физ., ζ по −Y) = 200 z.
            return new FragmentShellForces(ax, ay, [0, -(1000 + 500 * z), 0, 0, 200 * z * sn, 0, 0, 0]);
        });

        foreach (var p in g.Points)
        {
            var t = FragmentShellForceRecovery.Recover(g, p, tag => FragmentShellForceRecovery.Traction(forces[tag], g.CutNormal),
                out bool degenerate)!;
            Assert.False(degenerate);
            Assert.Equal(1000 + 500 * 0.8, t.Z, 9);
            Assert.Equal(200 * 0.8, t.Rx, 9);
            Assert.Equal(0, t.X, 9);
        }
    }

    /// <summary>Стена в один КЭ по высоте: патч вырожден (центры на одной прямой) → значение своего КЭ, флаг.</summary>
    [Fact]
    public void Recover_DegeneratePatch_FallsBack()
    {
        var (nodes, elements) = Wall(2, 1, 4, 1);
        var g = FragmentWallCutSampler.Prepare(V(0, 0, 0.3), V(2, 0, 0.3), V(0, 1, 0), true, Shells(nodes, elements), 1e-3);
        var t = FragmentShellForceRecovery.Recover(g, g.Points[3], _ => new Dof6(0, 0, 5, 0, 0, 0), out bool degenerate)!;
        Assert.True(degenerate);
        Assert.Equal(5, t.Z, 12);
    }

    /// <summary>Нет усилий КЭ, содержащего точку, → null.</summary>
    [Fact]
    public void Recover_NoForces_Null()
    {
        var (nodes, elements) = Wall(1, 1, 2, 2);
        var g = FragmentWallCutSampler.Prepare(V(0, 0, 0.3), V(1, 0, 0.3), V(0, 1, 0), true, Shells(nodes, elements), 1e-3);
        Assert.Null(FragmentShellForceRecovery.Recover(g, g.Points[0], _ => null, out _));
    }
}
