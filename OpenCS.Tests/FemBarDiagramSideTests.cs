using System.Windows.Media.Media3D;
using CSfea.Core;
using OpenCS.OpenSees.Structural;
using OpenCS.ViewModels;
using OpenCS.ViewModels.FemResult;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Эпюры моментов результата на стержнях откладываются в сторону растянутых волокон.</summary>
public class FemBarDiagramSideTests
{
    /// <summary>
    /// Консоль вдоль местной X, заделка в узле i, сила на конце j по местной оси. У заделки растянуты волокна со
    /// стороны, противоположной силе, — туда и должна смотреть ордината момента.
    /// </summary>
    [Theory]
    [InlineData(0, -1, 0, FemForceComponent.Mz)]
    [InlineData(0, 1, 0, FemForceComponent.Mz)]
    [InlineData(0, 0, -1, FemForceComponent.My)]
    [InlineData(0, 0, 1, FemForceComponent.My)]
    public void MomentOrdinateAtClamp_PointsToTensionSide(double fx, double fy, double fz, FemForceComponent component)
    {
        const double l = 3;
        var k = BeamElements.Beam3dKLocal(BeamElements.EnsureResponse(new BeamSection(30e9, 0.12, 0.0016, 0.0009, 0.0005)), l);
        double[] load = [fx * 1e4, fy * 1e4, fz * 1e4, 0, 0, 0];

        // Перемещения свободного конца: K_jj · u_j = F_j (узел i закреплён).
        var a = new double[6, 7];
        for (int r = 0; r < 6; r++)
        {
            for (int c = 0; c < 6; c++) a[r, c] = k[6 + r, 6 + c];
            a[r, 6] = load[r];
        }
        var uj = Solve(a);
        var f = new double[12];
        for (int r = 0; r < 12; r++)
            for (int c = 0; c < 6; c++) f[r] += k[r, 6 + c] * uj[c];

        var forces = new FemElementEndForces(1, f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10], f[11]);
        var start = FemForceEndpointConverter.Convert(forces, FemForceEndpointSignPolicy.OpenSeesDefault).Start;
        double m = FemForceEndpointConverter.ReadComponent(start, component);
        Assert.True(Math.Abs(m) > 0.99 * 1e4 * l, $"момент в заделке {m}");

        var ordinate = FemResultLayer.DiagramAxis(new Vector3D(0, 1, 0), new Vector3D(0, 0, 1), component) * Math.Sign(m);
        var tensionSide = new Vector3D(-fx, -fy, -fz);
        Assert.True(Vector3D.DotProduct(ordinate, tensionSide) > 0.99,
            $"{component}: ордината {ordinate}, растянутая сторона {tensionSide}");
    }

    static double[] Solve(double[,] a)
    {
        int n = a.GetLength(0);
        for (int c = 0; c < n; c++)
        {
            int p = c;
            for (int r = c + 1; r < n; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[p, c])) p = r;
            for (int j = 0; j <= n; j++) (a[c, j], a[p, j]) = (a[p, j], a[c, j]);
            for (int r = 0; r < n; r++)
            {
                if (r == c) continue;
                double q = a[r, c] / a[c, c];
                for (int j = c; j <= n; j++) a[r, j] -= q * a[c, j];
            }
        }
        var x = new double[n];
        for (int i = 0; i < n; i++) x[i] = a[i, n] / a[i, i];
        return x;
    }
}
