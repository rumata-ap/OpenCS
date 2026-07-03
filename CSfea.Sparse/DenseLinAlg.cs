namespace CSfea.Sparse;

/// <summary>
/// Прямые методы плотной линейной алгебры для малых матриц:
/// LU-разложение с частичным выбором ведущего элемента (замена
/// <c>np.linalg.solve</c>, <c>np.linalg.inv</c>, <c>np.linalg.det</c>) и
/// псевдообратная (<c>np.linalg.pinv</c>) для матриц полного столбцового ранга
/// (нужна для тай-точечного сдвига <c>Shell3</c>).
/// </summary>
public static class DenseLinAlg
{
    /// <summary>
    /// LU-разложение квадратной матрицы с частичным выбором ведущего элемента.
    /// Матрица <paramref name="a"/> перезаписывается множителями L/U.
    /// </summary>
    /// <returns>Знак перестановки (для определителя): +1 или -1; 0 — вырождение.</returns>
    public static int LuDecompose(double[,] a, int[] piv)
    {
        int n = a.GetLength(0);
        if (a.GetLength(1) != n)
            throw new ArgumentException("LU-разложение определено только для квадратной матрицы.");
        for (int i = 0; i < n; i++) piv[i] = i;
        int sign = 1;
        for (int k = 0; k < n; k++)
        {
            int p = k;
            double max = Math.Abs(a[k, k]);
            for (int i = k + 1; i < n; i++)
            {
                double v = Math.Abs(a[i, k]);
                if (v > max) { max = v; p = i; }
            }
            if (max == 0.0) return 0;
            if (p != k)
            {
                for (int j = 0; j < n; j++)
                    (a[k, j], a[p, j]) = (a[p, j], a[k, j]);
                (piv[k], piv[p]) = (piv[p], piv[k]);
                sign = -sign;
            }
            double akk = a[k, k];
            for (int i = k + 1; i < n; i++)
            {
                double f = a[i, k] / akk;
                a[i, k] = f;
                for (int j = k + 1; j < n; j++)
                    a[i, j] -= f * a[k, j];
            }
        }
        return sign;
    }

    /// <summary>Решает систему A·x = b. A не изменяется.</summary>
    public static double[] Solve(double[,] a, double[] b)
    {
        int n = a.GetLength(0);
        var lu = (double[,])a.Clone();
        var piv = new int[n];
        if (LuDecompose(lu, piv) == 0)
            throw new InvalidOperationException("Матрица вырождена (LU).");
        return LuSolve(lu, piv, b);
    }

    /// <summary>Решает A·X = B (несколько правых частей). A не изменяется.</summary>
    public static double[,] Solve(double[,] a, double[,] b)
    {
        int n = a.GetLength(0);
        int m = b.GetLength(1);
        var lu = (double[,])a.Clone();
        var piv = new int[n];
        if (LuDecompose(lu, piv) == 0)
            throw new InvalidOperationException("Матрица вырождена (LU).");
        var x = new double[n, m];
        var col = new double[n];
        for (int j = 0; j < m; j++)
        {
            for (int i = 0; i < n; i++) col[i] = b[i, j];
            var xj = LuSolve(lu, piv, col);
            for (int i = 0; i < n; i++) x[i, j] = xj[i];
        }
        return x;
    }

    /// <summary>Прямая/обратная подстановка по готовому LU-разложению.</summary>
    public static double[] LuSolve(double[,] lu, int[] piv, double[] b)
    {
        int n = lu.GetLength(0);
        var x = new double[n];
        for (int i = 0; i < n; i++) x[i] = b[piv[i]];
        for (int i = 0; i < n; i++)
        {
            double s = x[i];
            for (int j = 0; j < i; j++)
                s -= lu[i, j] * x[j];
            x[i] = s;
        }
        for (int i = n - 1; i >= 0; i--)
        {
            double s = x[i];
            for (int j = i + 1; j < n; j++)
                s -= lu[i, j] * x[j];
            x[i] = s / lu[i, i];
        }
        return x;
    }

    /// <summary>Обратная матрица.</summary>
    public static double[,] Inverse(double[,] a)
    {
        int n = a.GetLength(0);
        var id = new double[n, n];
        for (int i = 0; i < n; i++) id[i, i] = 1.0;
        return Solve(a, id);
    }

    /// <summary>Определитель через LU-разложение.</summary>
    public static double Det(double[,] a)
    {
        int n = a.GetLength(0);
        var lu = (double[,])a.Clone();
        var piv = new int[n];
        int sign = LuDecompose(lu, piv);
        if (sign == 0) return 0.0;
        double det = sign;
        for (int i = 0; i < n; i++) det *= lu[i, i];
        return det;
    }

    /// <summary>Инверсия матрицы 2x2 (без LU, для горячего пути якобиана).</summary>
    public static double[,] Inverse2x2(double[,] a, out double det)
    {
        det = a[0, 0] * a[1, 1] - a[0, 1] * a[1, 0];
        if (det == 0.0)
            throw new InvalidOperationException("Вырожденная матрица 2x2.");
        double inv = 1.0 / det;
        return new[,]
        {
            { a[1, 1] * inv, -a[0, 1] * inv },
            { -a[1, 0] * inv, a[0, 0] * inv },
        };
    }

    /// <summary>
    /// Псевдообратная Мура–Пенроуза для матрицы полного столбцового ранга
    /// (m ≥ n): A⁺ = (AᵀA)⁻¹Aᵀ, размер (n×m). Используется для тай-точечного
    /// восстановления сдвига в <c>Shell3</c> (T 3×2 → A⁺ 2×3).
    /// </summary>
    public static double[,] PseudoInverse(double[,] a)
    {
        int m = a.GetLength(0);
        int n = a.GetLength(1);
        if (m < n)
            throw new ArgumentException("PseudoInverse: ожидается m ≥ n (полный столбцовый ранг).");
        var ata = Dense.MatTMul(a, a);          // (n×n)
        var ataInv = Inverse(ata);              // (n×n)
        var at = Dense.Transpose(a);            // (n×m)
        return Dense.MatMul(ataInv, at);        // (n×m)
    }
}
