namespace CScore.PlateStrip;

/// <summary>Метод Гаусса с частичным выбором ведущего элемента для плотной системы n×n.
/// Внутренний расчётный примитив Среза 6 (см.
/// docs/superpowers/specs/2026-09-06-plate-strip-equivalent-beam-load-recovery-design.md,
/// «Численный аппарат»): KKT-матрица задачи восстановления нагрузки симметрична, но
/// знаконеопределённа, поэтому Холецкий неприменим, а размер задачи (десятки станций на три
/// компоненты) делает плотный Гаусс заведомо достаточным.
///
/// Существующие GaussSolve в PinnedEquilibriumNewton/CrackWidthSolver/LimitSectionStrainSolver
/// захардкожены на 3×3 и здесь не переиспользуются.</summary>
public static class DensePivotSolver
{
    /// <summary>Относительный порог ведущего элемента: сравнение с максимумом модуля по всей
    /// матрице, а не с абсолютным нулём — иначе плохо обусловленная система вернула бы
    /// огромные коэффициенты вместо признака вырожденности.</summary>
    public const double RelativePivotTolerance = 1e-12;

    /// <summary>Решает a·x = b. Входные массивы не изменяются. Возвращает false при
    /// вырожденной или почти вырожденной матрице и при нечисловых входных данных.</summary>
    public static bool Solve(double[,] a, double[] b, out double[] x)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        int n = a.GetLength(0);
        if (a.GetLength(1) != n)
            throw new ArgumentException("Матрица системы должна быть квадратной.", nameof(a));
        if (b.Length != n)
            throw new ArgumentException("Длина правой части должна совпадать с порядком матрицы.", nameof(b));

        x = new double[n];
        if (n == 0)
            return true;

        double maxMagnitude = 0.0;
        for (int i = 0; i < n; i++)
        {
            if (!double.IsFinite(b[i]))
                return false;
            for (int j = 0; j < n; j++)
            {
                double value = a[i, j];
                if (!double.IsFinite(value))
                    return false;
                maxMagnitude = Math.Max(maxMagnitude, Math.Abs(value));
            }
        }

        if (maxMagnitude == 0.0)
            return false;

        double threshold = RelativePivotTolerance * maxMagnitude;
        var m = (double[,])a.Clone();
        var v = (double[])b.Clone();

        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            for (int row = col + 1; row < n; row++)
                if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col]))
                    pivot = row;

            if (Math.Abs(m[pivot, col]) <= threshold)
                return false;

            if (pivot != col)
            {
                for (int k = 0; k < n; k++)
                    (m[col, k], m[pivot, k]) = (m[pivot, k], m[col, k]);
                (v[col], v[pivot]) = (v[pivot], v[col]);
            }

            for (int row = col + 1; row < n; row++)
            {
                double factor = m[row, col] / m[col, col];
                if (factor == 0.0)
                    continue;
                for (int k = col; k < n; k++)
                    m[row, k] -= factor * m[col, k];
                v[row] -= factor * v[col];
            }
        }

        for (int row = n - 1; row >= 0; row--)
        {
            double sum = v[row];
            for (int k = row + 1; k < n; k++)
                sum -= m[row, k] * x[k];
            x[row] = sum / m[row, row];
            if (!double.IsFinite(x[row]))
                return false;
        }

        return true;
    }
}
