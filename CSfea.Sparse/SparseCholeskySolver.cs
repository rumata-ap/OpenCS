namespace CSfea.Sparse;

/// <summary>
/// Разрежённый Холецкий A = L·Lᵀ для симметричных положительно определённых матриц.
/// Up-looking факторизация (по строкам) с деревом исключений и RCM-переупорядочиванием.
/// Символический анализ выполняется один раз (<see cref="AnalyzePattern"/>),
/// численная факторизация — многократно (<see cref="Factorize"/>) по постоянному паттерну.
/// </summary>
public sealed class SparseCholeskySolver
{
    private int _n;
    private int[] _perm = [];   // perm[new] = old
    private int[] _iperm = [];  // iperm[old] = new
    private int[] _pColPtr = []; // CSC переставленной A
    private int[] _pRowIdx = [];
    private int[] _valMap = [];  // _valMap[k] = индекс в a.Values для k-го элемента переставленной A
    private int[] _parent = [];
    private int[] _Lp = [];
    private int[] _Li = [];
    private double[] _Lx = [];
    private bool _analyzed;
    private bool _factorized;

    /// <summary>Последняя факторизация прошла как SPD (положительные пивоты).</summary>
    public bool LastFactorizationSpd { get; private set; }

    /// <summary>Символический анализ: RCM + структура L. Выполнить один раз для постоянного паттерна.</summary>
    public void AnalyzePattern(CscMatrix patternA)
    {
        if (patternA.Rows != patternA.Cols)
            throw new ArgumentException("Холецкий определён только для квадратной матрицы.");
        int n = patternA.Cols;
        _n = n;

        _perm = ReverseCuthillMcKee.ComputeOrdering(n, patternA.ColPtr, patternA.RowIdx);
        _iperm = new int[n];
        for (int i = 0; i < n; i++) _iperm[_perm[i]] = i;

        BuildPermutedPattern(patternA);
        _parent = EliminationTree(n, _pColPtr, _pRowIdx);
        SymbolicFactor(n, _pColPtr, _pRowIdx, _parent);
        _analyzed = true;
        _factorized = false;
    }

    /// <summary>Численная факторизация по постоянной структуре. a имеет тот же паттерн, что в AnalyzePattern.</summary>
    public void Factorize(CscMatrix a)
    {
        if (!_analyzed)
            throw new InvalidOperationException("Сначала вызовите AnalyzePattern.");
        int n = _n;

        // Значения переставленной A по карте _valMap.
        var pVal = new double[_pColPtr[n]];
        for (int k = 0; k < pVal.Length; k++) pVal[k] = a.Values[_valMap[k]];

        Array.Clear(_Lx, 0, _Lx.Length);
        var x = new double[n];
        var s = new int[n];
        var st = new int[n];
        var marked = new int[n];
        for (int i = 0; i < n; i++) marked[i] = -1;
        var c = new int[n];
        for (int i = 0; i < n; i++) c[i] = _Lp[i];

        LastFactorizationSpd = true;
        for (int k = 0; k < n; k++)
        {
            int top = Ereach(k, _pColPtr, _pRowIdx, _parent, s, st, marked);

            // x = переставленная A(:,k), часть i<=k
            for (int p = _pColPtr[k]; p < _pColPtr[k + 1]; p++)
            {
                int i = _pRowIdx[p];
                if (i <= k) x[i] = pVal[p];
            }
            double d = x[k];
            x[k] = 0.0;

            for (; top < n; top++)
            {
                int i = s[top];
                double lii = _Lx[_Lp[i]];
                double lki = x[i] / lii;
                x[i] = 0.0;
                for (int p = _Lp[i] + 1; p < c[i]; p++)
                    x[_Li[p]] -= _Lx[p] * lki;
                d -= lki * lki;
                int pos = c[i]++;
                _Li[pos] = k;
                _Lx[pos] = lki;
            }

            if (d <= 0.0)
            {
                LastFactorizationSpd = false;
                d = Math.Abs(d) < 1e-300 ? 1e-300 : Math.Abs(d); // продолжаем, флаг выставлен
            }
            int pk = c[k]++;
            _Li[pk] = k;
            _Lx[pk] = Math.Sqrt(d);
        }

        _factorized = true;
    }

    /// <summary>Решить A·x = b после Factorize.</summary>
    public double[] Solve(double[] b)
    {
        if (!_factorized)
            throw new InvalidOperationException("Сначала вызовите Factorize.");
        int n = _n;
        if (b.Length != n) throw new ArgumentException("Несовместимая длина правой части.");

        var y = new double[n];
        for (int i = 0; i < n; i++) y[i] = b[_perm[i]];

        // L·z = y (нижний треугольник, диагональ первой в столбце)
        for (int j = 0; j < n; j++)
        {
            y[j] /= _Lx[_Lp[j]];
            double yj = y[j];
            for (int p = _Lp[j] + 1; p < _Lp[j + 1]; p++)
                y[_Li[p]] -= _Lx[p] * yj;
        }
        // Lᵀ·w = z
        for (int j = n - 1; j >= 0; j--)
        {
            double yj = y[j];
            for (int p = _Lp[j] + 1; p < _Lp[j + 1]; p++)
                yj -= _Lx[p] * y[_Li[p]];
            y[j] = yj / _Lx[_Lp[j]];
        }

        var x = new double[n];
        for (int i = 0; i < n; i++) x[_perm[i]] = y[i];
        return x;
    }

    // ---- внутреннее ----

    private void BuildPermutedPattern(CscMatrix a)
    {
        int n = _n;
        // Триплеты переставленной A: (i, j) = (iperm[r], iperm[col]); храним исходный индекс p.
        var rows = new List<int>[n];
        var srcs = new List<int>[n];
        for (int j = 0; j < n; j++) { rows[j] = new List<int>(); srcs[j] = new List<int>(); }
        for (int col = 0; col < n; col++)
        {
            for (int p = a.ColPtr[col]; p < a.ColPtr[col + 1]; p++)
            {
                int r = a.RowIdx[p];
                int ni = _iperm[r];
                int nj = _iperm[col];
                rows[nj].Add(ni);
                srcs[nj].Add(p);
            }
        }

        var colPtr = new int[n + 1];
        var allRows = new List<int>();
        var allSrc = new List<int>();
        for (int j = 0; j < n; j++)
        {
            // сортировка по строке
            var idx = Enumerable.Range(0, rows[j].Count).OrderBy(t => rows[j][t]).ToArray();
            foreach (int t in idx)
            {
                allRows.Add(rows[j][t]);
                allSrc.Add(srcs[j][t]);
            }
            colPtr[j + 1] = allRows.Count;
        }
        _pColPtr = colPtr;
        _pRowIdx = allRows.ToArray();
        _valMap = allSrc.ToArray();
    }

    private static int[] EliminationTree(int n, int[] ap, int[] ai)
    {
        var parent = new int[n];
        var ancestor = new int[n];
        for (int k = 0; k < n; k++)
        {
            parent[k] = -1;
            ancestor[k] = -1;
            for (int p = ap[k]; p < ap[k + 1]; p++)
            {
                int i = ai[p];
                while (i != -1 && i < k)
                {
                    int inext = ancestor[i];
                    ancestor[i] = k;
                    if (inext == -1) parent[i] = k;
                    i = inext;
                }
            }
        }
        return parent;
    }

    private void SymbolicFactor(int n, int[] ap, int[] ai, int[] parent)
    {
        // Размеры столбцов L = 1 (диагональ) + число внедиагональных.
        var s = new int[n];
        var st = new int[n];
        var marked = new int[n];
        for (int i = 0; i < n; i++) marked[i] = -1;

        var perCol = new List<int>[n];
        for (int i = 0; i < n; i++) perCol[i] = new List<int>();

        for (int k = 0; k < n; k++)
        {
            int top = Ereach(k, ap, ai, parent, s, st, marked);
            for (int t = top; t < n; t++)
            {
                int i = s[t];
                perCol[i].Add(k); // внедиагональ L(k,i) в столбце i
            }
        }

        var lp = new int[n + 1];
        for (int i = 0; i < n; i++)
            lp[i + 1] = lp[i] + 1 + perCol[i].Count;
        _Lp = lp;
        _Li = new int[lp[n]];
        _Lx = new double[lp[n]];
        // Li заполнит численная фаза; здесь только размеры.
    }

    // Reach по дереву исключений: s[top..n-1] — паттерн строки k (топологически).
    private static int Ereach(int k, int[] ap, int[] ai, int[] parent,
                              int[] s, int[] st, int[] marked)
    {
        int n = marked.Length;
        int top = n;
        marked[k] = k;
        for (int p = ap[k]; p < ap[k + 1]; p++)
        {
            int i = ai[p];
            if (i > k) continue;
            int len = 0;
            while (marked[i] != k)
            {
                st[len++] = i;
                marked[i] = k;
                i = parent[i];
                if (i == -1) break;
            }
            while (len > 0) s[--top] = st[--len];
        }
        return top;
    }
}
