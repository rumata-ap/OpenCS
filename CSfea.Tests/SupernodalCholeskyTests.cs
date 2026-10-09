using CSfea.Sparse;

namespace CSfea.Tests;

/// <summary>Суперузловой Холецкий: символический анализ (структура, слияние, списки обновлений).</summary>
[HarnessChecks]
public class SupernodalCholeskyTests
{
    [Fact]
    public static void RunAll()
    {
        TestHarness.Section("Supernodal: символика");
        Analysis_Fundamental_MatchesColumnCounts();
        Analysis_Relaxed_FewerSupernodesSameNnz();
        Analysis_UpdateListsCoverDescendantRows();
        Analysis_OneSidedPattern_UsesUpperTriangle();

        TestHarness.Section("Supernodal: численная факторизация");
        Kernels_GemmAndPanel_MatchNaive();
        Factorize_MatchesUpLooking(BuildShellLike(12, 9, 6), "оболочка 12×9", SupernodeRelaxation.None);
        Factorize_MatchesUpLooking(BuildShellLike(30, 25, 6, seed: 5), "оболочка 30×25", SupernodeRelaxation.Default);
        Factorize_MatchesUpLooking(BuildShellLike(40, 40, 3, seed: 9), "оболочка 40×40, 3 DOF", SupernodeRelaxation.Default);
        Factorize_MatchesUpLooking(DropSomeLower(BuildShellLike(14, 10, 6, seed: 2), every: 3), "односторонний портрет",
            SupernodeRelaxation.Default);
        Factorize_Refactorize_And_NotSpd();
        Factorize_ThreadCountDoesNotChangeResult();
    }

    static void Factorize_ThreadCountDoesNotChangeResult()
    {
        // Крупная сетка, чтобы сработало и деление плотных произведений внутри суперузлов у корня.
        var a = BuildShellLike(60, 50, 6, seed: 13);
        var b = Enumerable.Range(0, a.Cols).Select(i => Math.Cos(0.01 * i)).ToArray();
        double[]? x1 = null;
        bool same = true;
        foreach (int threads in new[] { 1, 2, 4, 8 })
        {
            var sn = new SupernodalCholeskySolver { MaxDegreeOfParallelism = threads };
            sn.AnalyzePattern(a);
            sn.Factorize(a);
            var x = sn.Solve(b);
            if (x1 == null) x1 = x;
            else same &= x.AsSpan().SequenceEqual(x1);
        }
        var old = new SparseCholeskySolver();
        old.AnalyzePattern(a);
        old.Factorize(a);
        var xo = old.Solve(b);
        double d = 0, nx = 0;
        for (int i = 0; i < xo.Length; i++) { d = Math.Max(d, Math.Abs(x1![i] - xo[i])); nx = Math.Max(nx, Math.Abs(xo[i])); }
        TestHarness.Check("Потоки 1/2/4/8: решение побитно одно", same);
        TestHarness.Check("Потоки: совпадает с up-looking", d <= 1e-11 * nx, $"n = {a.Cols}, max|Δx|/max|x| = {d / nx:E2}");
    }

    static void Kernels_GemmAndPanel_MatchNaive()
    {
        var rnd = new Random(11);
        double worst = 0;
        foreach (var (p, q, kk, lower) in new[] { (37, 13, 70, false), (64, 64, 300, true), (21, 21, 5, true), (9, 3, 600, false) })
        {
            int lda = p + 3, ldc = p + 1;
            var a = Enumerable.Range(0, lda * kk + 7).Select(_ => rnd.NextDouble() - 0.5).ToArray();
            var c = Enumerable.Range(0, ldc * q + 5).Select(_ => rnd.NextDouble()).ToArray();
            var cRef = (double[])c.Clone();
            // B — первые q строк A со сдвигом 2 (как строки потомка в обновлении).
            DenseKernels.GemmNtSub(p, q, kk, a, 2, lda, a, 4, lda, c, 3, ldc, lower);
            for (int t = 0; t < q; t++)
                for (int i = lower ? t : 0; i < p; i++)
                {
                    double sum = 0;
                    for (int l = 0; l < kk; l++) sum += a[2 + l * lda + i] * a[4 + l * lda + t];
                    worst = Math.Max(worst, Math.Abs(c[3 + t * ldc + i] - (cRef[3 + t * ldc + i] - sum)));
                }
        }
        TestHarness.Check("GemmNtSub = наивное произведение (края, lowerOnly)", worst < 1e-12, $"max|Δ| = {worst:E2}");

        // Панель 150×100: L·Lᵀ = A на первых 100 строках, L21·L11ᵀ = A21 ниже.
        int m = 150, k = 100;
        var g = new double[m, k];
        for (int i = 0; i < m; i++) for (int j = 0; j < k; j++) g[i, j] = rnd.NextDouble() - 0.5;
        var x = new double[m * k];
        for (int j = 0; j < k; j++)
            for (int i = j; i < m; i++)
            {
                double sum = i == j ? k : 0;
                for (int l = 0; l < k; l++) sum += g[i, l] * g[j, l];
                x[j * m + i] = sum;
            }
        var a0 = (double[])x.Clone();
        bool spd = DenseKernels.FactorPanel(x, 0, m, k, nb: 16) < 0;
        double err = 0;
        for (int j = 0; j < k; j++)
            for (int i = j; i < m; i++)
            {
                double sum = 0;
                for (int l = 0; l <= j; l++) sum += x[l * m + i] * x[l * m + j];
                err = Math.Max(err, Math.Abs(sum - a0[j * m + i]));
            }
        TestHarness.Check("FactorPanel: L·Lᵀ = A на трапеции", spd && err < 1e-10, $"max|Δ| = {err:E2}");
    }

    static void Factorize_MatchesUpLooking(CscMatrix a, string name, SupernodeRelaxation relax)
    {
        int n = a.Cols;
        var b = Enumerable.Range(0, n).Select(i => Math.Sin(0.37 * i) + 0.5).ToArray();
        var old = new SparseCholeskySolver();
        old.AnalyzePattern(a);
        old.Factorize(a);
        var xOld = old.Solve(b);
        var sn = new SupernodalCholeskySolver { Relaxation = relax };
        sn.AnalyzePattern(a);
        sn.Factorize(a);
        var x = sn.Solve(b);
        double diff = 0, norm = 0;
        for (int i = 0; i < n; i++)
        {
            diff = Math.Max(diff, Math.Abs(x[i] - xOld[i]));
            norm = Math.Max(norm, Math.Abs(xOld[i]));
        }
        TestHarness.Check($"{name}: совпадает с up-looking", sn.LastFactorizationSpd && diff <= 1e-11 * norm,
            $"n = {n}, суперузлов {sn.Analysis!.SupernodeCount}, max|Δx|/max|x| = {diff / norm:E2}");
    }

    static void Factorize_Refactorize_And_NotSpd()
    {
        var a1 = BuildShellLike(10, 10, 6, seed: 4);
        var sn = new SupernodalCholeskySolver();
        sn.AnalyzePattern(a1);
        sn.Factorize(a1);
        // Тот же паттерн, другие значения: диагональ ×3.
        var v2 = (double[])a1.Values.Clone();
        for (int j = 0; j < a1.Cols; j++)
            for (int p = a1.ColPtr[j]; p < a1.ColPtr[j + 1]; p++)
                if (a1.RowIdx[p] == j) v2[p] *= 3;
        var a2 = new CscMatrix(a1.Rows, a1.Cols, a1.ColPtr, a1.RowIdx, v2);
        sn.Factorize(a2);
        var b = Enumerable.Range(0, a1.Cols).Select(i => 1.0 + i % 7).ToArray();
        var x = sn.Solve(b);
        var r = a2.Multiply(x);
        double res = 0;
        for (int i = 0; i < r.Length; i++) res = Math.Max(res, Math.Abs(r[i] - b[i]));
        TestHarness.Check("Повторная факторизация по тому же анализу", sn.LastFactorizationSpd && res < 1e-10,
            $"max|A·x − b| = {res:E2}");

        // Знаконеопределённая: диагональ со знаком минус → флаг не-SPD.
        var v3 = (double[])a1.Values.Clone();
        for (int j = 0; j < a1.Cols; j++)
            for (int p = a1.ColPtr[j]; p < a1.ColPtr[j + 1]; p++)
                if (a1.RowIdx[p] == j && j % 50 == 17) v3[p] = -v3[p];
        sn.Factorize(new CscMatrix(a1.Rows, a1.Cols, a1.ColPtr, a1.RowIdx, v3));
        TestHarness.Check("Не-SPD: флаг снят", !sn.LastFactorizationSpd);

        // Одна отрицательная диагональ без связей (развязанная неизвестная) — именно она и указывается.
        var v4 = (double[])a1.Values.Clone();
        int target = 123;
        for (int j = 0; j < a1.Cols; j++)
            for (int p = a1.ColPtr[j]; p < a1.ColPtr[j + 1]; p++)
                if (a1.RowIdx[p] == target || j == target) v4[p] = a1.RowIdx[p] == j ? -1.0 : 0.0;
        sn.Factorize(new CscMatrix(a1.Rows, a1.Cols, a1.ColPtr, a1.RowIdx, v4));
        TestHarness.Check("Не-SPD: указана неизвестная", sn.FirstNonPositivePivot == target,
            $"FirstNonPositivePivot = {sn.FirstNonPositivePivot}");
    }

    /// <summary>
    /// Матрица «как у оболочки»: сетка gx×gy узлов по <paramref name="dof"/> степеней свободы, связаны соседи
    /// по Q4 (включая диагональных); значения симметричные псевдослучайные, диагональное преобладание → SPD.
    /// </summary>
    internal static CscMatrix BuildShellLike(int gx, int gy, int dof, int seed = 1)
    {
        int n = gx * gy * dof;
        var rnd = new Random(seed);
        var coo = new CooMatrix(n, n, n * 9 * dof);
        var diag = new double[n];
        for (int j = 0; j < gy; j++)
            for (int i = 0; i < gx; i++)
                for (int dj = 0; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        if (dj == 0 && di <= 0) continue; // каждая пара узлов один раз
                        int i2 = i + di, j2 = j + dj;
                        if (i2 < 0 || i2 >= gx || j2 >= gy) continue;
                        int a = (j * gx + i) * dof, b = (j2 * gx + i2) * dof;
                        for (int p = 0; p < dof; p++)
                            for (int q = 0; q < dof; q++)
                            {
                                double v = rnd.NextDouble() - 0.5;
                                coo.Add(a + p, b + q, v);
                                coo.Add(b + q, a + p, v);
                                diag[a + p] += Math.Abs(v);
                                diag[b + q] += Math.Abs(v);
                            }
                    }
        // Внутриузловой блок: плотный, симметричный.
        for (int node = 0; node < gx * gy; node++)
            for (int p = 0; p < dof; p++)
                for (int q = p + 1; q < dof; q++)
                {
                    double v = 0.3 * (rnd.NextDouble() - 0.5);
                    int a = node * dof + p, b = node * dof + q;
                    coo.Add(a, b, v);
                    coo.Add(b, a, v);
                    diag[a] += Math.Abs(v);
                    diag[b] += Math.Abs(v);
                }
        for (int k = 0; k < n; k++) coo.Add(k, k, diag[k] + 1.0 + rnd.NextDouble());
        return coo.ToCsc();
    }

    static void Analysis_Fundamental_MatchesColumnCounts()
    {
        var a = BuildShellLike(12, 9, 6);
        var an = SupernodalAnalysis.Analyze(a, CholeskyOrdering.Amd, SupernodeRelaxation.None);
        var old = new SparseCholeskySolver();
        old.AnalyzePattern(a);

        // Без слияния хранимые блоки (трапеции) — ровно nnz(L); nnz(L) совпадает со старым решателем той же AMD.
        long trapezoids = 0;
        bool colsOk = true, postOk = true;
        for (int s = 0; s < an.SupernodeCount; s++)
        {
            int f = an.SuperStart[s], l = an.SuperStart[s + 1], k = l - f;
            int m = an.RowPtr[s + 1] - an.RowPtr[s];
            trapezoids += (long)k * m - (long)k * (k - 1) / 2;
            for (int j = f; j < l; j++)
                if (an.ColCount[j] != m - (j - f)) colsOk = false;
        }
        for (int j = 0; j < an.N; j++)
            if (an.Parent[j] != -1 && an.Parent[j] <= j) postOk = false;
        TestHarness.Check("Fundamental: nnz(L) как у up-looking", an.NnzL == old.NnzL,
            $"суперузловой {an.NnzL}, up-looking {old.NnzL}");
        TestHarness.Check("Fundamental: трапеции = nnz(L), без явных нулей", trapezoids == an.NnzL,
            $"Σ трапеций {trapezoids}");
        TestHarness.Check("Fundamental: столбцы суперузла — вложенная структура", colsOk);
        TestHarness.Check("Обратный обход: parent[j] > j", postOk);
        TestHarness.Check("Fundamental: узлы 6 DOF собраны в суперузлы", an.SupernodeCount <= an.N / 6,
            $"суперузлов {an.SupernodeCount} на {an.N} столбцов");
    }

    static void Analysis_Relaxed_FewerSupernodesSameNnz()
    {
        var a = BuildShellLike(20, 20, 6);
        var fund = SupernodalAnalysis.Analyze(a, CholeskyOrdering.Amd, SupernodeRelaxation.None);
        var relax = SupernodalAnalysis.Analyze(a);
        TestHarness.Check("Relaxed: суперузлов меньше", relax.SupernodeCount < fund.SupernodeCount,
            $"{relax.SupernodeCount} против {fund.SupernodeCount} (фундаментальных {relax.FundamentalSupernodeCount})");
        TestHarness.Check("Relaxed: nnz(L) тот же, хранение не меньше", relax.NnzL == fund.NnzL && relax.StoredL >= relax.NnzL,
            $"nnz(L) {relax.NnzL}, хранится {relax.StoredL}");

        // Строки суперузла отсортированы, первыми — его столбцы; дети вложены в строки родителя.
        bool sorted = true, nested = true;
        for (int s = 0; s < relax.SupernodeCount; s++)
        {
            int f = relax.SuperStart[s], k = relax.SuperStart[s + 1] - f;
            for (int p = relax.RowPtr[s]; p < relax.RowPtr[s + 1]; p++)
            {
                int local = p - relax.RowPtr[s];
                if (local < k && relax.Rows[p] != f + local) sorted = false;
                if (p > relax.RowPtr[s] && relax.Rows[p] <= relax.Rows[p - 1]) sorted = false;
            }
            int par = relax.SuperParent[s];
            if (par == -1) continue;
            var parentRows = new HashSet<int>(relax.Rows[relax.RowPtr[par]..relax.RowPtr[par + 1]]);
            for (int p = relax.RowPtr[s] + k; p < relax.RowPtr[s + 1]; p++)
                if (!parentRows.Contains(relax.Rows[p])) nested = false;
        }
        TestHarness.Check("Relaxed: строки отсортированы, столбцы первыми", sorted);
        TestHarness.Check("Relaxed: строки ребёнка ⊂ строк родителя", nested);
    }

    /// <summary>Без части элементов нижнего треугольника портрет несимметричен (как Kff музея).</summary>
    internal static CscMatrix DropSomeLower(CscMatrix a, int every)
    {
        var coo = new CooMatrix(a.Rows, a.Cols, a.Nnz);
        int count = 0;
        for (int j = 0; j < a.Cols; j++)
            for (int p = a.ColPtr[j]; p < a.ColPtr[j + 1]; p++)
            {
                if (a.RowIdx[p] > j && count++ % every == 0) continue;
                coo.Add(a.RowIdx[p], j, a.Values[p]);
            }
        return coo.ToCsc();
    }

    static void Analysis_OneSidedPattern_UsesUpperTriangle()
    {
        var a = DropSomeLower(BuildShellLike(10, 8, 6, seed: 3), every: 5);
        var an = SupernodalAnalysis.Analyze(a, CholeskyOrdering.Amd, SupernodeRelaxation.None);
        var old = new SparseCholeskySolver();
        old.AnalyzePattern(a);
        TestHarness.Check("Односторонний портрет: nnz(L) как у up-looking", an.NnzL == old.NnzL,
            $"суперузловой {an.NnzL}, up-looking {old.NnzL}");
    }

    static void Analysis_UpdateListsCoverDescendantRows()
    {
        var a = BuildShellLike(15, 11, 6, seed: 7);
        var an = SupernodalAnalysis.Analyze(a);
        // Каждая строка суперузла ниже его столбцов попадает ровно в один отрезок обновления предка.
        long rowsBelow = 0, covered = 0;
        bool ancestorsOnly = true, inRange = true;
        for (int s = 0; s < an.SupernodeCount; s++)
            rowsBelow += an.RowPtr[s + 1] - an.RowPtr[s] - (an.SuperStart[s + 1] - an.SuperStart[s]);
        for (int t = 0; t < an.SupernodeCount; t++)
            for (int u = an.UpdPtr[t]; u < an.UpdPtr[t + 1]; u++)
            {
                int d = an.UpdSuper[u];
                covered += an.UpdRowEnd[u] - an.UpdRowBegin[u];
                int x = d;
                while (x != -1 && x != t) x = an.SuperParent[x];
                if (x != t || d == t) ancestorsOnly = false;
                for (int p = an.UpdRowBegin[u]; p < an.UpdRowEnd[u]; p++)
                {
                    int r = an.Rows[an.RowPtr[d] + p];
                    if (r < an.SuperStart[t] || r >= an.SuperStart[t + 1]) inRange = false;
                }
            }
        TestHarness.Check("Обновления: покрыты все строки ниже суперузлов", covered == rowsBelow,
            $"{covered} из {rowsBelow}");
        TestHarness.Check("Обновления: только от потомков", ancestorsOnly);
        TestHarness.Check("Обновления: строки отрезка — столбцы цели", inRange);
    }
}
