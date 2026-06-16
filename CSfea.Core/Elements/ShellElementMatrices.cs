using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>
/// Локальные и глобальные матрицы жёсткости оболочечного элемента:
/// линейная K_L и геометрическая K_σ (фон Карман). Порт матричной части
/// <c>fea/core.py</c> (<c>K_linear</c>, <c>K_geometric</c>,
/// <c>element_K_linear_global</c>, <c>element_K_geometric_global</c>).
/// </summary>
public static class ShellElementMatrices
{
    /// <summary>Линейная локальная матрица K_e (5n x 5n) по ламинату.</summary>
    public static double[,] KLinear(double[,] xy, Laminate laminate)
    {
        var (a, b, d, ash) = laminate.ABDAs();
        int n = xy.GetLength(0);
        if (n == 4)
        {
            var k = new double[20, 20];
            var (pts, wts) = Shell4.Gauss2x2();
            for (int g = 0; g < pts.Length; g++)
            {
                var (bm, bb, detJ) = Shell4.BMatricesBendingMembrane(xy, pts[g][0], pts[g][1]);
                var bs = Shell4.BMatrixShear(xy, pts[g][0], pts[g][1]);
                AccumulateLinear(k, bm, bb, bs, a, b, d, ash, wts[g] * detJ);
            }
            return k;
        }
        if (n == 3)
        {
            var (bm, bb, area) = Shell3.BMatricesBendingMembrane(xy);
            var bs = Shell3.BMatrixShear(xy);
            var k = new double[15, 15];
            AccumulateLinear(k, bm, bb, bs, a, b, d, ash, area);
            return k;
        }
        throw new ArgumentException("Поддерживаются только 3 или 4 узла.");
    }

    /// <summary>K += scale · (Bmᵀ A Bm + Bmᵀ B Bb + Bbᵀ B Bm + Bbᵀ D Bb + Bsᵀ As Bs).</summary>
    private static void AccumulateLinear(double[,] k, double[,] bm, double[,] bb, double[,] bs,
                                         double[,] a, double[,] b, double[,] d, double[,] ash,
                                         double scale)
    {
        Dense.AddScaledInPlace(k, Dense.MatTMul(bm, Dense.MatMul(a, bm)), scale);
        Dense.AddScaledInPlace(k, Dense.MatTMul(bm, Dense.MatMul(b, bb)), scale);
        Dense.AddScaledInPlace(k, Dense.MatTMul(bb, Dense.MatMul(b, bm)), scale);
        Dense.AddScaledInPlace(k, Dense.MatTMul(bb, Dense.MatMul(d, bb)), scale);
        Dense.AddScaledInPlace(k, Dense.MatTMul(bs, Dense.MatMul(ash, bs)), scale);
    }

    /// <summary>Геометрическая локальная K_σ (фон Карман) при перемещении u_loc.</summary>
    public static double[,] KGeometric(double[,] xy, Laminate laminate, double[] uLoc)
    {
        var (a, b, _, _) = laminate.ABDAs();
        int n = xy.GetLength(0);
        if (n == 4)
        {
            var k = new double[20, 20];
            var (pts, wts) = Shell4.Gauss2x2();
            for (int g = 0; g < pts.Length; g++)
            {
                var (bm, bb, _) = Shell4.BMatricesBendingMembrane(xy, pts[g][0], pts[g][1]);
                var (gMat, detJ) = Shell4.GMatrixW(xy, pts[g][0], pts[g][1]);
                AccumulateGeometric(k, bm, bb, gMat, a, b, uLoc, wts[g] * detJ);
            }
            return k;
        }
        if (n == 3)
        {
            var (bm, bb, area) = Shell3.BMatricesBendingMembrane(xy);
            var (gMat, _) = Shell3.GMatrixW(xy);
            var k = new double[15, 15];
            AccumulateGeometric(k, bm, bb, gMat, a, b, uLoc, area);
            return k;
        }
        throw new ArgumentException("Поддерживаются только 3 или 4 узла.");
    }

    private static void AccumulateGeometric(double[,] k, double[,] bm, double[,] bb, double[,] gMat,
                                            double[,] a, double[,] b, double[] uLoc, double scale)
    {
        var epsM = Dense.MatVec(bm, uLoc);
        var kappa = Dense.MatVec(bb, uLoc);
        // N = A·eps_m + B·kappa
        var n = Dense.AddV(Dense.MatVec(a, epsM), Dense.MatVec(b, kappa));
        var sigma = new[,] { { n[0], n[2] }, { n[2], n[1] } };
        // G^T · sigma · G
        var sg = Dense.MatMul(sigma, gMat);
        Dense.AddScaledInPlace(k, Dense.MatTMul(gMat, sg), scale);
    }

    /// <summary>Линейная глобальная K элемента (6n x 6n) по координатам узлов.</summary>
    public static double[,] ElementKLinearGlobal(double[][] coords, Laminate laminate)
    {
        var (xy, _, _) = ShellGeometry.ProjectToLocal(coords);
        var kLoc = KLinear(xy, laminate);
        return ShellGeometry.AssembleGlobal(kLoc, coords).KGlobal;
    }

    /// <summary>Геометрическая глобальная K_σ элемента (6n x 6n) при u_global.</summary>
    public static double[,] ElementKGeometricGlobal(double[][] coords, Laminate laminate, double[] uGlobal)
    {
        var (xy, _, _) = ShellGeometry.ProjectToLocal(coords);
        int n = coords.Length;
        var r = ShellGeometry.LocalFrame(coords);
        var t = ShellGeometry.BuildTMatrix(r, n);
        var uLoc = Dense.MatTVec(t, uGlobal);  // u_loc = T^T · u_global
        var ksgLoc = KGeometric(xy, laminate, uLoc);
        // K = T · Ksg_loc · T^T
        return Dense.MatMul(Dense.MatMul(t, ksgLoc), Dense.Transpose(t));
    }
}
