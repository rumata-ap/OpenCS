using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>
/// 3-узловой плоско-линейный оболочечный элемент: CST-мембрана, линейный
/// изгиб (постоянные κ) и MITC3-подобный тай-точечный сдвиг.
/// Локальный DOF-вектор: [u,v,w,θx,θy] на узел, всего 15.
/// Координаты <c>xy</c> — локальные (3,2). Порт <c>fea/core.py: Shell3</c>.
/// </summary>
public static class Shell3
{
    /// <summary>Число узлов.</summary>
    public const int NNodes = 3;

    /// <summary>Число локальных DOF.</summary>
    public const int NDof = 15;

    /// <summary>Барицентрические функции формы и производные по (L2, L3).</summary>
    public static (double[] N, double[,] DN) Shape(double l2, double l3)
    {
        double l1 = 1.0 - l2 - l3;
        var n = new[] { l1, l2, l3 };
        var dn = new[,] { { -1.0, -1.0 }, { 1.0, 0.0 }, { 0.0, 1.0 } };
        return (n, dn);
    }

    /// <summary>Якобиан и физические производные функций формы (постоянные).</summary>
    public static (double[,] DNdx, double Area, double[,] J) Geometry(double[,] xy)
    {
        var (_, dn) = Shape(1.0 / 3.0, 1.0 / 3.0);
        // J = dN^T · xy (2,2)
        var j = new double[2, 2];
        for (int a = 0; a < 2; a++)
            for (int b = 0; b < 2; b++)
            {
                double s = 0.0;
                for (int i = 0; i < 3; i++)
                    s += dn[i, a] * xy[i, b];
                j[a, b] = s;
            }
        var invJ = DenseLinAlg.Inverse2x2(j, out double detJ);
        if (detJ <= 0.0)
            throw new ArgumentException("Отрицательный якобиан в Shell3.");
        double area = 0.5 * detJ;
        // dN_dx = dN · invJ (3,2)
        var dNdx = new double[3, 2];
        for (int i = 0; i < 3; i++)
            for (int k = 0; k < 2; k++)
                dNdx[i, k] = dn[i, 0] * invJ[0, k] + dn[i, 1] * invJ[1, k];
        return (dNdx, area, j);
    }

    /// <summary>Мембранная B_m (3,15) и изгибная B_b (3,15).</summary>
    public static (double[,] Bm, double[,] Bb, double Area) BMatricesBendingMembrane(double[,] xy)
    {
        var (dNdx, area, _) = Geometry(xy);
        var bm = new double[3, 15];
        var bb = new double[3, 15];
        for (int i = 0; i < 3; i++)
        {
            double nx = dNdx[i, 0], ny = dNdx[i, 1];
            int c = 5 * i;
            bm[0, c + 0] = nx;
            bm[1, c + 1] = ny;
            bm[2, c + 0] = ny;
            bm[2, c + 1] = nx;
            bb[0, c + 4] = nx;
            bb[1, c + 3] = -ny;
            bb[2, c + 3] = -nx;
            bb[2, c + 4] = ny;
        }
        return (bm, bb, area);
    }

    /// <summary>MITC3-подобный тай-точечный сдвиг B_s (2,15).</summary>
    public static double[,] BMatrixShear(double[,] xy)
    {
        var edges = new[] { (0, 1), (1, 2), (2, 0) };
        var ts = new double[3][];
        var ls = new double[3];
        for (int ke = 0; ke < 3; ke++)
        {
            var (i, j) = edges[ke];
            var d = new[] { xy[j, 0] - xy[i, 0], xy[j, 1] - xy[i, 1] };
            double l = Math.Sqrt(d[0] * d[0] + d[1] * d[1]);
            ts[ke] = new[] { d[0] / l, d[1] / l };
            ls[ke] = l;
        }

        // T (3,2): строки — касательные рёбер; Tpinv (2,3).
        var tMat = new double[3, 2];
        for (int ke = 0; ke < 3; ke++)
        {
            tMat[ke, 0] = ts[ke][0];
            tMat[ke, 1] = ts[ke][1];
        }
        var tPinv = DenseLinAlg.PseudoInverse(tMat); // (2,3)

        var rows = new double[3, 15];
        for (int ke = 0; ke < 3; ke++)
        {
            var (i, j) = edges[ke];
            double l = ls[ke];
            double tx = ts[ke][0], ty = ts[ke][1];
            int ci = 5 * i, cj = 5 * j;
            rows[ke, ci + 2] += -1.0 / l;
            rows[ke, cj + 2] += 1.0 / l;
            rows[ke, ci + 3] += 0.5 * (-ty);
            rows[ke, ci + 4] += 0.5 * tx;
            rows[ke, cj + 3] += 0.5 * (-ty);
            rows[ke, cj + 4] += 0.5 * tx;
        }
        // Bs = Tpinv · rows (2,15)
        return Dense.MatMul(tPinv, rows);
    }

    /// <summary>G (2,15): [w,x; w,y] = G · u_loc.</summary>
    public static (double[,] G, double Area) GMatrixW(double[,] xy)
    {
        var (dNdx, area, _) = Geometry(xy);
        var g = new double[2, 15];
        for (int i = 0; i < 3; i++)
        {
            g[0, 5 * i + 2] = dNdx[i, 0];
            g[1, 5 * i + 2] = dNdx[i, 1];
        }
        return (g, area);
    }
}
