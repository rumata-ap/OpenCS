namespace CSfea.Sparse;

/// <summary>
/// Метод сопряжённых градиентов с диагональным (Jacobi) предобуславливателем
/// для симметричных положительно определённых систем. Аналог
/// <c>scipy.sparse.linalg.cg</c>.
/// </summary>
public static class ConjugateGradient
{
    /// <summary>Результат итерационного решения.</summary>
    public readonly record struct Result(double[] X, int Iterations, double Residual, bool Converged);

    /// <summary>Решает A·x = b методом PCG (Jacobi).</summary>
    public static Result Solve(CscMatrix a, double[] b, double rtol = 1e-10,
                               int maxIter = -1, double[]? x0 = null)
    {
        int n = b.Length;
        if (a.Rows != n || a.Cols != n)
            throw new ArgumentException("Несовместимые размерности в CG.");
        if (maxIter < 0) maxIter = 10 * n + 50;

        var diag = ExtractDiagonal(a, n);
        var minv = new double[n];
        for (int i = 0; i < n; i++)
            minv[i] = diag[i] != 0.0 ? 1.0 / diag[i] : 1.0;

        var x = x0 != null ? (double[])x0.Clone() : new double[n];
        var r = Dense.SubV(b, a.Multiply(x));
        var z = new double[n];
        for (int i = 0; i < n; i++) z[i] = minv[i] * r[i];
        var p = (double[])z.Clone();

        double bnorm = Dense.Norm(b);
        if (bnorm == 0.0) bnorm = 1.0;
        double rzOld = Dense.Dot(r, z);

        double resid = Dense.Norm(r) / bnorm;
        if (resid <= rtol)
            return new Result(x, 0, resid, true);

        int it = 0;
        for (; it < maxIter; it++)
        {
            var ap = a.Multiply(p);
            double pap = Dense.Dot(p, ap);
            if (pap == 0.0) break;
            double alpha = rzOld / pap;
            for (int i = 0; i < n; i++)
            {
                x[i] += alpha * p[i];
                r[i] -= alpha * ap[i];
            }
            resid = Dense.Norm(r) / bnorm;
            if (resid <= rtol)
                return new Result(x, it + 1, resid, true);

            for (int i = 0; i < n; i++) z[i] = minv[i] * r[i];
            double rzNew = Dense.Dot(r, z);
            double beta = rzNew / rzOld;
            for (int i = 0; i < n; i++) p[i] = z[i] + beta * p[i];
            rzOld = rzNew;
        }
        return new Result(x, it, resid, false);
    }

    private static double[] ExtractDiagonal(CscMatrix a, int n)
    {
        var d = new double[n];
        for (int c = 0; c < n; c++)
            for (int pp = a.ColPtr[c]; pp < a.ColPtr[c + 1]; pp++)
                if (a.RowIdx[pp] == c) { d[c] = a.Values[pp]; break; }
        return d;
    }
}
