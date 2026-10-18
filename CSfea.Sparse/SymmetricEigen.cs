namespace CSfea.Sparse;

/// <summary>
/// Собственные значения и векторы небольшой плотной симметричной матрицы — циклический метод Якоби. Для проекций
/// Рэлея–Ритца (порядок — десятки), не для больших матриц.
/// </summary>
public static class SymmetricEigen
{
    /// <summary>
    /// Разложение A = V·diag(values)·Vᵀ; столбцы V ортонормированы. Значения не упорядочены. Используется только
    /// нижний треугольник с диагональю симметризованной копии (A не меняется).
    /// </summary>
    public static (double[] Values, double[,] Vectors) Jacobi(double[,] a, int maxSweeps = 100)
    {
        int n = a.GetLength(0);
        if (a.GetLength(1) != n) throw new ArgumentException("Матрица должна быть квадратной.");
        var m = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                m[i, j] = 0.5 * (a[i, j] + a[j, i]);
        var v = new double[n, n];
        for (int i = 0; i < n; i++) v[i, i] = 1.0;

        for (int sweep = 0; sweep < maxSweeps; sweep++)
        {
            double off = 0.0, diag = 0.0;
            for (int i = 0; i < n; i++)
            {
                diag += m[i, i] * m[i, i];
                for (int j = i + 1; j < n; j++) off += m[i, j] * m[i, j];
            }
            if (off <= 1e-30 * Math.Max(diag, 1e-300)) break;

            for (int p = 0; p < n - 1; p++)
                for (int q = p + 1; q < n; q++)
                {
                    double apq = m[p, q];
                    if (apq == 0.0) continue;
                    double theta = (m[q, q] - m[p, p]) / (2.0 * apq);
                    double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
                    if (theta == 0.0) t = 1.0;
                    double c = 1.0 / Math.Sqrt(t * t + 1.0), s = t * c;
                    for (int k = 0; k < n; k++)
                    {
                        double mkp = m[k, p], mkq = m[k, q];
                        m[k, p] = c * mkp - s * mkq;
                        m[k, q] = s * mkp + c * mkq;
                    }
                    for (int k = 0; k < n; k++)
                    {
                        double mpk = m[p, k], mqk = m[q, k];
                        m[p, k] = c * mpk - s * mqk;
                        m[q, k] = s * mpk + c * mqk;
                    }
                    for (int k = 0; k < n; k++)
                    {
                        double vkp = v[k, p], vkq = v[k, q];
                        v[k, p] = c * vkp - s * vkq;
                        v[k, q] = s * vkp + c * vkq;
                    }
                }
        }
        var values = new double[n];
        for (int i = 0; i < n; i++) values[i] = m[i, i];
        return (values, v);
    }
}
