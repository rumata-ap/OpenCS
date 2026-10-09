namespace CSfea.Sparse;

/// <summary>
/// Суперузловой разрежённый Холецкий A = L·Lᵀ (left-looking): каждый суперузел собирает обновления от потомков
/// плотным произведением, затем факторизуется как плотная панель (<see cref="DenseKernels"/>). Символика —
/// <see cref="SupernodalAnalysis"/> один раз на паттерн, численная факторизация — многократно. Видит только верхний
/// треугольник A, как <see cref="SparseCholeskySolver"/>; при неположительном ведущем элементе выставляет
/// <see cref="LastFactorizationSpd"/> = false и продолжает (вызывающий откатывается на LU).
/// </summary>
public sealed class SupernodalCholeskySolver
{
    private const int UpdateChunk = 256;   // столбцов цели за одно плотное произведение (размер буфера обновления)

    private SupernodalAnalysis? _an;
    private double[] _lx = [];
    private bool _factorized;

    /// <summary>Упорядочивание для <see cref="AnalyzePattern"/>.</summary>
    public CholeskyOrdering Ordering { get; init; } = CholeskyOrdering.Amd;

    /// <summary>Ослабленное слияние суперузлов (по умолчанию — как в CHOLMOD).</summary>
    public SupernodeRelaxation Relaxation { get; init; } = SupernodeRelaxation.Default;

    /// <summary>Последняя факторизация прошла как SPD (положительные ведущие элементы).</summary>
    public bool LastFactorizationSpd { get; private set; }

    /// <summary>Символический анализ (после <see cref="AnalyzePattern"/>).</summary>
    public SupernodalAnalysis? Analysis => _an;

    /// <summary>Число ненулевых L (с диагональю) без явных нулей слияния.</summary>
    public long NnzL => _an?.NnzL ?? 0;

    /// <summary>Символический анализ по портрету. Выполнить один раз для постоянного паттерна.</summary>
    public void AnalyzePattern(CscMatrix patternA)
    {
        _an = SupernodalAnalysis.Analyze(patternA, Ordering, Relaxation);
        if (_an.StoredL > Array.MaxLength)
            throw new InvalidOperationException(
                $"Множитель L не помещается в один массив: {_an.StoredL:E3} чисел ({_an.StoredL * 8 / 1073741824.0:0.0} ГБ).");
        _lx = new double[_an.StoredL];
        _factorized = false;
    }

    /// <summary>Численная факторизация. a — тот же паттерн (и порядок элементов), что в <see cref="AnalyzePattern"/>.</summary>
    public void Factorize(CscMatrix a)
    {
        var an = _an ?? throw new InvalidOperationException("Сначала вызовите AnalyzePattern.");
        if (a.Cols != an.N) throw new ArgumentException("Размер матрицы не совпадает с анализом.");
        var map = new int[an.N];
        var buf = new double[(long)an.MaxSupernodeRows * Math.Min(UpdateChunk, Math.Max(an.MaxSupernodeCols, 1))];
        bool spd = true;
        for (int s = 0; s < an.SupernodeCount; s++)
            spd &= FactorSupernode(s, a.Values, map, buf);
        LastFactorizationSpd = spd;
        _factorized = true;
    }

    /// <summary>Решить A·x = b после <see cref="Factorize"/>.</summary>
    public double[] Solve(double[] b)
    {
        var an = _an;
        if (an == null || !_factorized)
            throw new InvalidOperationException("Сначала вызовите Factorize.");
        int n = an.N;
        if (b.Length != n) throw new ArgumentException("Несовместимая длина правой части.");
        var y = new double[n];
        for (int i = 0; i < n; i++) y[i] = b[an.Perm[i]];

        var lx = _lx;
        int[] rows = an.Rows;
        // L·z = y
        for (int s = 0; s < an.SupernodeCount; s++)
        {
            int f = an.SuperStart[s], k = an.SuperStart[s + 1] - f;
            int rp = an.RowPtr[s], m = an.RowPtr[s + 1] - rp;
            long off = an.LxPtr[s];
            for (int c = 0; c < k; c++)
            {
                long col = off + (long)c * m;
                double yc = y[f + c] / lx[col + c];
                y[f + c] = yc;
                if (yc == 0.0) continue;
                for (int r = c + 1; r < m; r++) y[rows[rp + r]] -= lx[col + r] * yc;
            }
        }
        // Lᵀ·w = z
        for (int s = an.SupernodeCount - 1; s >= 0; s--)
        {
            int f = an.SuperStart[s], k = an.SuperStart[s + 1] - f;
            int rp = an.RowPtr[s], m = an.RowPtr[s + 1] - rp;
            long off = an.LxPtr[s];
            for (int c = k - 1; c >= 0; c--)
            {
                long col = off + (long)c * m;
                double yc = y[f + c];
                for (int r = c + 1; r < m; r++) yc -= lx[col + r] * y[rows[rp + r]];
                y[f + c] = yc / lx[col + c];
            }
        }
        var x = new double[n];
        for (int i = 0; i < n; i++) x[an.Perm[i]] = y[i];
        return x;
    }

    // Суперузел s: значения A, обновления от потомков, плотная факторизация панели. map и buf — рабочие (на поток).
    private bool FactorSupernode(int s, double[] aValues, int[] map, double[] buf)
    {
        var an = _an!;
        var lx = _lx;
        int[] rows = an.Rows;
        int f = an.SuperStart[s], l = an.SuperStart[s + 1], k = l - f;
        int rp = an.RowPtr[s], m = an.RowPtr[s + 1] - rp;
        long off = an.LxPtr[s];

        for (int r = 0; r < m; r++) map[rows[rp + r]] = r;
        Array.Clear(lx, (int)off, m * k);

        // Значения A: диагональ и строго нижний треугольник (транспонированный верхний).
        for (int j = f; j < l; j++)
        {
            long col = off + (long)(j - f) * m;
            int dp = an.DiagValMap[j];
            if (dp >= 0) lx[col + (j - f)] += aValues[dp];
            for (int p = an.LowerColPtr[j]; p < an.LowerColPtr[j + 1]; p++)
                lx[col + map[an.LowerRowIdx[p]]] += aValues[an.LowerValMap[p]];
        }

        // Обновления от потомков: C = −L_D(строки b.., :)·L_D(строки b..e, :)ᵀ, разброс по строкам и столбцам s.
        for (int u = an.UpdPtr[s]; u < an.UpdPtr[s + 1]; u++)
        {
            int d = an.UpdSuper[u];
            int drp = an.RowPtr[d], dm = an.RowPtr[d + 1] - drp;
            int dk = an.SuperStart[d + 1] - an.SuperStart[d];
            long doff = an.LxPtr[d];
            int b = an.UpdRowBegin[u], e = an.UpdRowEnd[u];
            for (int t0 = b; t0 < e; t0 += UpdateChunk)
            {
                int q = Math.Min(UpdateChunk, e - t0);
                int p = dm - t0;
                Array.Clear(buf, 0, p * q);
                DenseKernels.GemmNtSub(p, q, dk, lx, doff + t0, dm, lx, doff + t0, dm, buf, 0, p, lowerOnly: true);
                for (int t = 0; t < q; t++)
                {
                    long col = off + (long)(rows[drp + t0 + t] - f) * m;
                    int bc = t * p;
                    for (int i = t; i < p; i++)
                        lx[col + map[rows[drp + t0 + i]]] += buf[bc + i];
                }
            }
        }

        return DenseKernels.FactorPanel(lx, off, m, k);
    }
}
