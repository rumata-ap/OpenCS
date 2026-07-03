using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>
/// Внутренние усилия F_int(u) и полная тангенциальная матрица K_T = dF_int/du
/// оболочечного элемента в приближении фон Кармана. Поддерживает полиморфное
/// сечение <see cref="IShellSectionResponse"/>. Порт элементной части
/// <c>fea/assembly.py</c> (<c>_F_internal_*</c>, <c>_K_tangent_*</c>,
/// <c>element_F_internal_global</c>, <c>element_K_tangent_global</c>).
/// </summary>
public static class ShellElementForces
{
    /// <summary>Локальный вектор внутренних сил (5n) при u_loc.</summary>
    public static double[] FInternalLocal(double[,] xy, IShellSectionResponse section, double[] uLoc)
    {
        int n = xy.GetLength(0);
        if (n == 4)
        {
            var f = new double[20];
            var (pts, wts) = Shell4.Gauss2x2();
            for (int gp = 0; gp < pts.Length; gp++)
            {
                var (bm, bb, detJ) = Shell4.BMatricesBendingMembrane(xy, pts[gp][0], pts[gp][1]);
                var bs = Shell4.BMatrixShear(xy, pts[gp][0], pts[gp][1]);
                var (gMat, _) = Shell4.GMatrixW(xy, pts[gp][0], pts[gp][1]);
                AccumulateFInternal(f, bm, bb, bs, gMat, section, uLoc, wts[gp] * detJ);
            }
            return f;
        }
        if (n == 3)
        {
            var (bm, bb, area) = Shell3.BMatricesBendingMembrane(xy);
            var bs = Shell3.BMatrixShear(xy);
            var (gMat, _) = Shell3.GMatrixW(xy);
            var f = new double[15];
            AccumulateFInternal(f, bm, bb, bs, gMat, section, uLoc, area);
            return f;
        }
        throw new ArgumentException("Поддерживаются только 3 или 4 узла.");
    }

    private static void AccumulateFInternal(double[] f, double[,] bm, double[,] bb, double[,] bs,
                                            double[,] gMat, IShellSectionResponse section,
                                            double[] uLoc, double scale)
    {
        var g = Dense.MatVec(gMat, uLoc);
        var epsL = Dense.MatVec(bm, uLoc);
        var epsNL = new[] { 0.5 * g[0] * g[0], 0.5 * g[1] * g[1], g[0] * g[1] };
        var epsM = Dense.AddV(epsL, epsNL);
        var kappa = Dense.MatVec(bb, uLoc);
        var gamma = Dense.MatVec(bs, uLoc);
        var (nForce, mForce, qForce) = section.Forces(epsM, kappa, gamma);
        var bmTot = BmTotal(bm, gMat, g);
        // F += scale·(Bm_tot^T·N + Bb^T·M + Bs^T·Q)
        AddMatTVecScaled(f, bmTot, nForce, scale);
        AddMatTVecScaled(f, bb, mForce, scale);
        AddMatTVecScaled(f, bs, qForce, scale);
    }

    /// <summary>Локальная тангенциальная K_T (5n x 5n) при u_loc.</summary>
    public static double[,] KTangentLocal(double[,] xy, IShellSectionResponse section, double[] uLoc)
    {
        int n = xy.GetLength(0);
        if (n == 4)
        {
            var k = new double[20, 20];
            var (pts, wts) = Shell4.Gauss2x2();
            for (int gp = 0; gp < pts.Length; gp++)
            {
                var (bm, bb, detJ) = Shell4.BMatricesBendingMembrane(xy, pts[gp][0], pts[gp][1]);
                var bs = Shell4.BMatrixShear(xy, pts[gp][0], pts[gp][1]);
                var (gMat, _) = Shell4.GMatrixW(xy, pts[gp][0], pts[gp][1]);
                AccumulateKTangent(k, bm, bb, bs, gMat, section, uLoc, wts[gp] * detJ);
            }
            return k;
        }
        if (n == 3)
        {
            var (bm, bb, area) = Shell3.BMatricesBendingMembrane(xy);
            var bs = Shell3.BMatrixShear(xy);
            var (gMat, _) = Shell3.GMatrixW(xy);
            var k = new double[15, 15];
            AccumulateKTangent(k, bm, bb, bs, gMat, section, uLoc, area);
            return k;
        }
        throw new ArgumentException("Поддерживаются только 3 или 4 узла.");
    }

    private static void AccumulateKTangent(double[,] k, double[,] bm, double[,] bb, double[,] bs,
                                           double[,] gMat, IShellSectionResponse section,
                                           double[] uLoc, double scale)
    {
        var g = Dense.MatVec(gMat, uLoc);
        var epsL = Dense.MatVec(bm, uLoc);
        var epsNL = new[] { 0.5 * g[0] * g[0], 0.5 * g[1] * g[1], g[0] * g[1] };
        var epsM = Dense.AddV(epsL, epsNL);
        var kappa = Dense.MatVec(bb, uLoc);
        var gamma = Dense.MatVec(bs, uLoc);
        var (nForce, _, _) = section.Forces(epsM, kappa, gamma);
        var (aT, bT, dT, asT) = section.Tangent(epsM, kappa, gamma);
        var bmTot = BmTotal(bm, gMat, g);
        var sigma = new[,] { { nForce[0], nForce[2] }, { nForce[2], nForce[1] } };

        // Bm_tot^T·A_T·Bm_tot
        Dense.AddScaledInPlace(k, Dense.MatTMul(bmTot, Dense.MatMul(aT, bmTot)), scale);
        // Bm_tot^T·B_T·Bb + Bb^T·B_T^T·Bm_tot
        Dense.AddScaledInPlace(k, Dense.MatTMul(bmTot, Dense.MatMul(bT, bb)), scale);
        Dense.AddScaledInPlace(k, Dense.MatTMul(bb, Dense.MatMul(Dense.Transpose(bT), bmTot)), scale);
        // Bb^T·D_T·Bb
        Dense.AddScaledInPlace(k, Dense.MatTMul(bb, Dense.MatMul(dT, bb)), scale);
        // Bs^T·As_T·Bs
        Dense.AddScaledInPlace(k, Dense.MatTMul(bs, Dense.MatMul(asT, bs)), scale);
        // G^T·Sigma·G (геометрическая часть)
        Dense.AddScaledInPlace(k, Dense.MatTMul(gMat, Dense.MatMul(sigma, gMat)), scale);
    }

    /// <summary>B_m_tot = B_m + B_NL, где B_NL = A_θ(g)·G.</summary>
    private static double[,] BmTotal(double[,] bm, double[,] gMat, double[] g)
    {
        int cols = bm.GetLength(1);
        // A_θ = [[g0,0],[0,g1],[g1,g0]]; B_NL = A_θ·G (3,cols)
        var bmTot = (double[,])bm.Clone();
        for (int j = 0; j < cols; j++)
        {
            double g0j = gMat[0, j];
            double g1j = gMat[1, j];
            bmTot[0, j] += g[0] * g0j;
            bmTot[1, j] += g[1] * g1j;
            bmTot[2, j] += g[1] * g0j + g[0] * g1j;
        }
        return bmTot;
    }

    private static void AddMatTVecScaled(double[] target, double[,] a, double[] v, double scale)
    {
        // target += scale · A^T·v
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        for (int i = 0; i < rows; i++)
        {
            double vi = v[i];
            if (vi == 0.0) continue;
            double sv = scale * vi;
            for (int j = 0; j < cols; j++)
                target[j] += a[i, j] * sv;
        }
    }

    /// <summary>Глобальный вектор внутренних сил элемента (6n) при u_global.</summary>
    public static double[] ElementFInternalGlobal(double[][] coords, IShellSectionResponse section,
                                                  double[] uGlobal)
    {
        var (xy, _, _) = ShellGeometry.ProjectToLocal(coords);
        int n = coords.Length;
        var r = ShellGeometry.LocalFrame(coords);
        var t = ShellGeometry.BuildTMatrix(r, n);
        var uLoc = Dense.MatTVec(t, uGlobal);
        var fLoc = FInternalLocal(xy, section, uLoc);
        return Dense.MatVec(t, fLoc);
    }

    /// <summary>Глобальная тангенциальная K_T элемента (6n x 6n) при u_global.</summary>
    public static double[,] ElementKTangentGlobal(double[][] coords, IShellSectionResponse section,
                                                  double[] uGlobal,
                                                  double drilling = ShellGeometry.DefaultDrilling)
    {
        var (xy, _, _) = ShellGeometry.ProjectToLocal(coords);
        int n = coords.Length;
        var r = ShellGeometry.LocalFrame(coords);
        var t = ShellGeometry.BuildTMatrix(r, n);
        var uLoc = Dense.MatTVec(t, uGlobal);
        var ktLoc = KTangentLocal(xy, section, uLoc);
        var kGlobal = Dense.MatMul(Dense.MatMul(t, ktLoc), Dense.Transpose(t));
        double kRef = Dense.MaxAbs(Dense.Diagonal(ktLoc)) * drilling;
        ShellGeometry.AddDrilling(kGlobal, r, n, kRef);
        return kGlobal;
    }
}
