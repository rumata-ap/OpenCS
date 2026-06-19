using CSfea.Sparse;

namespace CSfea.Tests;

/// <summary>Тесты разреженного Холецкого: сверка с прямым LU и плотной системой.</summary>
public static class SparseCholeskyTests
{
    public static void RunAll()
    {
        TestHarness.Section("SparseCholesky");
        Cholesky_MatchesLu_OnSpdFemLikeMatrix();
        Cholesky_RefactorizeReusesPattern();
    }

    // SPD-матрица: 2D-лапласиан на сетке gx×gy + диагональный сдвиг.
    static CscMatrix BuildSpd(int gx, int gy, double shift)
    {
        int n = gx * gy;
        int Id(int i, int j) => j * gx + i;
        var coo = new CooMatrix(n, n, n * 5);
        for (int j = 0; j < gy; j++)
            for (int i = 0; i < gx; i++)
            {
                int k = Id(i, j);
                double diagLocal = 0.0;
                void Nb(int ii, int jj)
                {
                    if (ii < 0 || jj < 0 || ii >= gx || jj >= gy) return;
                    int m = Id(ii, jj);
                    coo.Add(k, m, -1.0);
                    diagLocal += 1.0;
                }
                Nb(i - 1, j); Nb(i + 1, j); Nb(i, j - 1); Nb(i, j + 1);
                coo.Add(k, k, shift + diagLocal);
            }
        return coo.ToCsc();
    }

    static void Cholesky_MatchesLu_OnSpdFemLikeMatrix()
    {
        var a = BuildSpd(5, 5, 0.5);
        int n = a.Cols;
        var b = new double[n];
        for (int i = 0; i < n; i++) b[i] = 1.0 + 0.1 * i;

        double[] xLu = SparseLuSolver.SolveOnce(a, b);

        var chol = new SparseCholeskySolver();
        chol.AnalyzePattern(a);
        chol.Factorize(a);
        double[] xCh = chol.Solve(b);

        double maxDiff = 0.0;
        for (int i = 0; i < n; i++) maxDiff = Math.Max(maxDiff, Math.Abs(xLu[i] - xCh[i]));
        TestHarness.Check("Cholesky_MatchesLu", maxDiff < 1e-9 && chol.LastFactorizationSpd,
            $"maxDiff={maxDiff:E3}");
    }

    static void Cholesky_RefactorizeReusesPattern()
    {
        // Тот же паттерн, другие значения -> повторная Factorize без AnalyzePattern.
        var a1 = BuildSpd(4, 4, 0.5);
        var a2 = BuildSpd(4, 4, 2.0); // та же структура, другой сдвиг
        int n = a1.Cols;
        var b = new double[n];
        for (int i = 0; i < n; i++) b[i] = 1.0;

        var chol = new SparseCholeskySolver();
        chol.AnalyzePattern(a1);
        chol.Factorize(a2);
        double[] x = chol.Solve(b);
        double[] xRef = SparseLuSolver.SolveOnce(a2, b);

        double maxDiff = 0.0;
        for (int i = 0; i < n; i++) maxDiff = Math.Max(maxDiff, Math.Abs(x[i] - xRef[i]));
        TestHarness.Check("Cholesky_RefactorizeReusesPattern", maxDiff < 1e-9, $"maxDiff={maxDiff:E3}");
    }
}
