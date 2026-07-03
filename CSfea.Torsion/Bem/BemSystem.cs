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
            // Детекция сингулярности: матрица постоянных граничных элементов с одним
            // условием Дирихле невырождена. Det может быть мал из-за размерности,
            // поэтому сингулярность определяем по фактической неудаче решения (NaN/Inf).
            x = DenseLinAlg.Solve(A, bvec);
            double maxX = 0; for (int i = 0; i < n; i++) maxX = Math.Max(maxX, Math.Abs(x[i]));
            singular = !double.IsFinite(maxX);
            // Проверка невязки как запасной критерий
            if (!singular)
            {
                double resid = 0, bn = 0;
                for (int i = 0; i < n; i++)
                {
                    double s = 0;
                    for (int j = 0; j < n; j++) s += A[i, j] * x[j];
                    resid += (s - bvec[i]) * (s - bvec[i]);
                    bn += bvec[i] * bvec[i];
                }
                if (bn > 0 && Math.Sqrt(resid / bn) > 1e-6) singular = true;
            }
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
}
