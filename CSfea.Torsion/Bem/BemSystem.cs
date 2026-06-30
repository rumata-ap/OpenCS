using CSfea.Sparse;

namespace CSfea.Torsion;

/// <summary>Граничные условия Неймана задачи депланации + сборка/решение СЛАУ.</summary>
public static class BemSystem
{
    /// <summary>
    /// Решает задачу для функции депланации ω. Возвращает ub (ω на центрах) и unb (∂ω/∂n),
    /// флаг сингулярности. Порт _neumann_bc/_abmatr/_solve_system/_reorder.
    /// Граничное условие: ∂ω/∂n = y·nx − x·ny на всех элементах, кроме последнего
    /// (n−1) — там ω=0 (нормировка задачи Неймана).
    /// </summary>
    public static (double[] ub, double[] unb, bool singular) Solve(
        double[,] G, double[,] H, double[] xm, double[] ym, double[] enx, double[] eny,
        BoundaryDiscrete d)
    {
        int n = xm.Length;
        // Граничные условия: index[i]=1 (Нейман) для всех, кроме последнего (n−1): там index=0 (Дирихле ω=0)
        var index = new int[n];        // 0 = Дирихле (u), 1 = Нейман (un)
        var bc = new double[n];        // предписанные значения
        for (int i = 0; i < n; i++)
        {
            index[i] = 1;
            bc[i] = ym[i] * enx[i] - xm[i] * eny[i];   // ∂ω/∂n = y·nx − x·ny
        }
        index[n - 1] = 0;
        bc[n - 1] = 0.0;               // ω = 0 (нормировка)

        // ABMATR
        var A = new double[n, n];
        var bvec = new double[n];
        for (int j = 0; j < n; j++)
        {
            if (index[j] == 0)         // Дирихле: u предписан
            {
                for (int i = 0; i < n; i++) { A[i, j] = -G[i, j]; bvec[i] -= H[i, j] * bc[j]; }
            }
            else                       // Нейман: un предписан
            {
                for (int i = 0; i < n; i++) { A[i, j] = H[i, j]; bvec[i] += G[i, j] * bc[j]; }
            }
        }

        // SOLVE
        bool singular;
        double[] x;
        try
        {
            double det = DenseLinAlg.Det(A);
            singular = Math.Abs(det) < 1e-12 * Scale(A, n);
            x = singular ? new double[n] : DenseLinAlg.Solve(A, bvec);
        }
        catch
        {
            singular = true;
            x = new double[n];
        }

        // REORDER
        var ub = new double[n];
        var unb = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (index[i] == 0) { ub[i] = bc[i]; unb[i] = x[i]; }   // u предписан, un — из решения
            else               { ub[i] = x[i]; unb[i] = bc[i]; }   // un предписан, u — из решения
        }
        return (ub, unb, singular);
    }

    private static double Scale(double[,] A, int n)
    {
        double mx = 0.0;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++) mx = Math.Max(mx, Math.Abs(A[i, j]));
        return mx == 0.0 ? 1.0 : mx;
    }
}
