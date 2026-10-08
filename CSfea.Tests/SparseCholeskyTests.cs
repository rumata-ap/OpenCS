using CSfea.Sparse;

namespace CSfea.Tests;

/// <summary>Тесты разреженного Холецкого: сверка с прямым LU и плотной системой.</summary>
[HarnessChecks]
public class SparseCholeskyTests
{
    [Fact]
    public static void RunAll()
    {
        TestHarness.Section("SparseCholesky");
        Cholesky_MatchesLu_OnSpdFemLikeMatrix();
        Cholesky_RefactorizeReusesPattern();
        Cholesky_LargeGrid_NoException();
        Cholesky_AmdMatchesRcm_LessFill();
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

    // Проверка на большой матрице (~4900 узлов), воспроизводящей краш на крупной сетке.
    static void Cholesky_LargeGrid_NoException()
    {
        var a = BuildSpd(70, 70, 0.5); // n=4900, ~4 ненулевых на строку
        int n = a.Cols;
        var b = new double[n];
        for (int i = 0; i < n; i++) b[i] = 1.0;

        var chol = new SparseCholeskySolver();
        chol.AnalyzePattern(a);
        chol.Factorize(a);
        double[] x = chol.Solve(b);
        double[] xRef = SparseLuSolver.SolveOnce(a, b);

        double maxDiff = 0.0;
        for (int i = 0; i < n; i++) maxDiff = Math.Max(maxDiff, Math.Abs(x[i] - xRef[i]));
        TestHarness.Check("Cholesky_LargeGrid: SPD", chol.LastFactorizationSpd, "");
        TestHarness.Check("Cholesky_LargeGrid: matches LU", maxDiff < 1e-6, $"maxDiff={maxDiff:E3}");
    }

    // AMD (по умолчанию) и RCM дают одно решение; на квадратной сетке заполнение L у AMD меньше, чем у ленты RCM.
    static void Cholesky_AmdMatchesRcm_LessFill()
    {
        var a = BuildSpd(60, 60, 0.5);
        int n = a.Cols;
        var b = new double[n];
        for (int i = 0; i < n; i++) b[i] = Math.Sin(0.01 * i) + 1.0;

        var amd = new SparseCholeskySolver();
        amd.AnalyzePattern(a);
        amd.Factorize(a);
        var rcm = new SparseCholeskySolver { Ordering = CholeskyOrdering.Rcm };
        rcm.AnalyzePattern(a);
        rcm.Factorize(a);
        double[] xa = amd.Solve(b), xr = rcm.Solve(b);

        double maxDiff = 0.0;
        for (int i = 0; i < n; i++) maxDiff = Math.Max(maxDiff, Math.Abs(xa[i] - xr[i]));
        TestHarness.Check("Cholesky_Amd: SPD и совпадает с RCM", amd.LastFactorizationSpd && maxDiff < 1e-9,
            $"maxDiff={maxDiff:E3}");
        TestHarness.Check("Cholesky_Amd: заполнение меньше RCM", amd.NnzL < rcm.NnzL,
            $"nnz(L): AMD {amd.NnzL}, RCM {rcm.NnzL}");
        var perm = ApproximateMinimumDegree.ComputeOrdering(n, a.ColPtr, a.RowIdx);
        TestHarness.Check("Amd_IsValidPermutation", perm.Length == n && perm.OrderBy(i => i).SequenceEqual(Enumerable.Range(0, n)));
    }
}
