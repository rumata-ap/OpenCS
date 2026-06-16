using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>
/// 4-узловой билинейный оболочечный элемент MITC4 (Dvorkin–Bathe).
/// Мембрана и изгиб — полная 2×2 интеграция, поперечный сдвиг — тай-точки
/// середин рёбер (полностью снимает shear locking).
/// Локальный DOF-вектор: [u,v,w,θx,θy] на узел, всего 20.
/// Координаты <c>xy</c> — локальные (4,2). Порт <c>fea/core.py: Shell4</c>.
/// </summary>
public static class Shell4
{
    /// <summary>Число узлов.</summary>
    public const int NNodes = 4;

    /// <summary>Число локальных DOF.</summary>
    public const int NDof = 20;

    /// <summary>Функции формы и производные по (ξ, η).</summary>
    public static (double[] N, double[,] DNdxi) Shape(double xi, double eta)
    {
        var n = new[]
        {
            0.25 * (1 - xi) * (1 - eta),
            0.25 * (1 + xi) * (1 - eta),
            0.25 * (1 + xi) * (1 + eta),
            0.25 * (1 - xi) * (1 + eta),
        };
        var dn = new[,]
        {
            { 0.25 * -(1 - eta), 0.25 * -(1 - xi) },
            { 0.25 *  (1 - eta), 0.25 * -(1 + xi) },
            { 0.25 *  (1 + eta), 0.25 *  (1 + xi) },
            { 0.25 * -(1 + eta), 0.25 *  (1 - xi) },
        };
        return (n, dn);
    }

    /// <summary>Якобиан и физические производные функций формы.</summary>
    public static (double[,] DNdx, double DetJ, double[,] J, double[,] InvJ)
        Jacobian(double[,] xy, double[,] dNdxi)
    {
        // J = dN_dxi^T · xy  (2,2)
        var j = new double[2, 2];
        for (int a = 0; a < 2; a++)
            for (int b = 0; b < 2; b++)
            {
                double s = 0.0;
                for (int i = 0; i < 4; i++)
                    s += dNdxi[i, a] * xy[i, b];
                j[a, b] = s;
            }
        var invJ = DenseLinAlg.Inverse2x2(j, out double detJ);
        if (detJ <= 0.0)
            throw new ArgumentException("Отрицательный якобиан (плохая форма элемента).");
        // dN_dx = dN_dxi · invJ  (4,2)
        var dNdx = new double[4, 2];
        for (int i = 0; i < 4; i++)
            for (int k = 0; k < 2; k++)
                dNdx[i, k] = dNdxi[i, 0] * invJ[0, k] + dNdxi[i, 1] * invJ[1, k];
        return (dNdx, detJ, j, invJ);
    }

    /// <summary>Мембранная B_m (3,20) и изгибная B_b (3,20) в точке (ξ, η).</summary>
    public static (double[,] Bm, double[,] Bb, double DetJ)
        BMatricesBendingMembrane(double[,] xy, double xi, double eta)
    {
        var (_, dNdxi) = Shape(xi, eta);
        var (dNdx, detJ, _, _) = Jacobian(xy, dNdxi);
        var bm = new double[3, 20];
        var bb = new double[3, 20];
        for (int i = 0; i < 4; i++)
        {
            double nx = dNdx[i, 0], ny = dNdx[i, 1];
            int c = 5 * i;
            bm[0, c + 0] = nx;
            bm[1, c + 1] = ny;
            bm[2, c + 0] = ny;
            bm[2, c + 1] = nx;
            bb[0, c + 4] = nx;   // θy,x
            bb[1, c + 3] = -ny;  // -θx,y
            bb[2, c + 3] = -nx;  // -θx,x
            bb[2, c + 4] = ny;   // θy,y
        }
        return (bm, bb, detJ);
    }

    /// <summary>Сдвиговая B_s (2,20) по MITC4 (тай-точки середин рёбер).</summary>
    public static double[,] BMatrixShear(double[,] xy, double xi, double eta)
    {
        var (_, dN0) = Shape(xi, eta);
        var (_, _, _, invJ) = Jacobian(xy, dN0);

        // Естественные γ (B_ξz, B_ηz) в произвольной тай-точке.
        double[,] GammaNatural(double xis, double etas)
        {
            var (n, dNs) = Shape(xis, etas);
            var (dNdxs, _, js, _) = Jacobian(xy, dNs);
            var bsPhys = new double[2, 20];
            for (int i = 0; i < 4; i++)
            {
                double nx = dNdxs[i, 0], ny = dNdxs[i, 1];
                double ni = n[i];
                int c = 5 * i;
                bsPhys[0, c + 2] = nx;   // γ_xz = w,x + θy
                bsPhys[0, c + 4] = ni;
                bsPhys[1, c + 2] = ny;   // γ_yz = w,y - θx
                bsPhys[1, c + 3] = -ni;
            }
            // в естественную: γ_nat = J^T · γ_phys
            return Dense.MatTMul(js, bsPhys); // (2,20)
        }

        var bsA = GammaNatural(0.0, -1.0);  // γ_ξz (строка 0)
        var bsC = GammaNatural(0.0, 1.0);
        var bsB = GammaNatural(1.0, 0.0);   // γ_ηz (строка 1)
        var bsD = GammaNatural(-1.0, 0.0);

        var bsNat = new double[2, 20];
        for (int k = 0; k < 20; k++)
        {
            bsNat[0, k] = 0.5 * (1.0 - eta) * bsA[0, k] + 0.5 * (1.0 + eta) * bsC[0, k];
            bsNat[1, k] = 0.5 * (1.0 - xi) * bsD[1, k] + 0.5 * (1.0 + xi) * bsB[1, k];
        }
        // в физические: γ_phys = J^{-T} · γ_nat
        return Dense.MatTMul(invJ, bsNat);
    }

    /// <summary>G (2,20): [w,x; w,y] = G · u_loc.</summary>
    public static (double[,] G, double DetJ) GMatrixW(double[,] xy, double xi, double eta)
    {
        var (_, dNdxi) = Shape(xi, eta);
        var (dNdx, detJ, _, _) = Jacobian(xy, dNdxi);
        var g = new double[2, 20];
        for (int i = 0; i < 4; i++)
        {
            g[0, 5 * i + 2] = dNdx[i, 0];
            g[1, 5 * i + 2] = dNdx[i, 1];
        }
        return (g, detJ);
    }

    /// <summary>Точки и веса 2×2 квадратуры Гаусса.</summary>
    public static (double[][] Pts, double[] Wts) Gauss2x2()
    {
        double g = 1.0 / Math.Sqrt(3.0);
        var pts = new[]
        {
            new[] { -g, -g },
            new[] { g, -g },
            new[] { g, g },
            new[] { -g, g },
        };
        var wts = new[] { 1.0, 1.0, 1.0, 1.0 };
        return (pts, wts);
    }
}
