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
