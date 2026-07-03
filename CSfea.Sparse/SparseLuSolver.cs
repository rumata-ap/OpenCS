namespace CSfea.Sparse;

/// <summary>
/// Прямой разреженный решатель A·x = b на основе LU-разложения по алгоритму
/// Gilbert–Peierls (left-looking) с частичным выбором ведущего элемента.
/// Без переупорядочивания столбцов (q = тождество) — для структурированных
/// ленточных FEM-матриц заполнение остаётся умеренным. Аналог
/// <c>scipy.sparse.linalg.spsolve</c> на собственном слое (см. конспект).
/// </summary>
public sealed class SparseLuSolver : ISparseSolver
{
    private int _n;
    private int[] _lp = Array.Empty<int>();
    private int[] _li = Array.Empty<int>();
    private double[] _lx = Array.Empty<double>();
    private int[] _up = Array.Empty<int>();
    private int[] _ui = Array.Empty<int>();
    private double[] _ux = Array.Empty<double>();
    private int[] _pinv = Array.Empty<int>();
    private bool _factorized;

    /// <summary>Порог частичного выбора (1.0 = чистый partial pivoting).</summary>
    public double Tolerance { get; init; } = 1.0;

    /// <summary>Удобный однократный вызов: факторизация + решение.</summary>
    public static double[] SolveOnce(CscMatrix a, double[] b)
    {
        var s = new SparseLuSolver();
        s.Factorize(a);
        return s.Solve(b);
    }

    public void Factorize(CscMatrix a)
    {
        if (a.Rows != a.Cols)
            throw new ArgumentException("LU определён только для квадратной матрицы.");
        int n = a.Cols;
        _n = n;
        int[] ap = a.ColPtr;
        int[] ai = a.RowIdx;
        double[] ax = a.Values;

        int cap = Math.Max(a.Nnz, n) + n;
        var li = new int[cap];
        var lx = new double[cap];
        var ui = new int[cap];
        var ux = new double[cap];
        var lp = new int[n + 1];
        var up = new int[n + 1];
        var pinv = new int[n];
        for (int i = 0; i < n; i++) pinv[i] = -1;

        var x = new double[n];
        var xi = new int[n];
        var pstack = new int[n];
        var marked = new bool[n];

        int lnz = 0, unz = 0;
        for (int k = 0; k < n; k++)
        {
            lp[k] = lnz;
            up[k] = unz;
            if (lnz + n > li.Length) { Grow(ref li, ref lx, 2 * li.Length + n); }
            if (unz + n > ui.Length) { Grow(ref ui, ref ux, 2 * ui.Length + n); }

            int col = k;
            int top = Spsolve(lp, li, lx, pinv, marked, ap, ai, ax, col, xi, pstack, x, n);

            // Поиск ведущего элемента среди непивотальных строк; пивотальные → в U.
            int ipiv = -1;
            double amax = -1.0;
            for (int p = top; p < n; p++)
            {
                int i = xi[p];
                if (pinv[i] < 0)
                {
                    double t = Math.Abs(x[i]);
                    if (t > amax) { amax = t; ipiv = i; }
                }
                else
                {
                    ui[unz] = pinv[i];
                    ux[unz++] = x[i];
                }
            }
            if (ipiv == -1 || amax <= 0.0)
                throw new InvalidOperationException("Разреженная матрица вырождена (LU).");
            if (pinv[col] < 0 && Math.Abs(x[col]) >= amax * Tolerance)
                ipiv = col;

            double pivot = x[ipiv];
            ui[unz] = k;
            ux[unz++] = pivot;          // диагональ U(k,k) — последняя в столбце
            pinv[ipiv] = k;
            li[lnz] = ipiv;
            lx[lnz++] = 1.0;            // диагональ L(k,k) = 1 — первая в столбце
            for (int p = top; p < n; p++)
            {
                int i = xi[p];
                if (pinv[i] < 0)
                {
                    li[lnz] = i;
                    lx[lnz++] = x[i] / pivot;
                }
                x[i] = 0.0;
            }
        }
        lp[n] = lnz;
        up[n] = unz;
        // Перенумерация строк L в перестановочное пространство.
        for (int p = 0; p < lnz; p++) li[p] = pinv[li[p]];

        _lp = lp; _li = li; _lx = lx;
        _up = up; _ui = ui; _ux = ux;
        _pinv = pinv;
        _factorized = true;
    }

    public double[] Solve(double[] b)
    {
        if (!_factorized)
            throw new InvalidOperationException("Сначала вызовите Factorize.");
        if (b.Length != _n)
            throw new ArgumentException("Несовместимая длина правой части.");
        int n = _n;
        var x = new double[n];
        // x = P·b  (x[pinv[k]] = b[k])
        for (int k = 0; k < n; k++) x[_pinv[k]] = b[k];
        // L·y = x  (L — единичная нижняя, диагональ первой в столбце)
        for (int j = 0; j < n; j++)
        {
            double xj = x[j];
            for (int p = _lp[j] + 1; p < _lp[j + 1]; p++)
                x[_li[p]] -= _lx[p] * xj;
        }
        // U·z = y  (U — верхняя, диагональ последней в столбце)
        for (int j = n - 1; j >= 0; j--)
        {
            double xj = x[j] / _ux[_up[j + 1] - 1];
            x[j] = xj;
            for (int p = _up[j]; p < _up[j + 1] - 1; p++)
                x[_ui[p]] -= _ux[p] * xj;
        }
        return x;
    }

    public void Dispose() { }

    // ---- внутренние процедуры Gilbert–Peierls (cs_dfs / cs_reach / cs_spsolve) ----

    private static int Spsolve(int[] lp, int[] li, double[] lx, int[] pinv, bool[] marked,
                               int[] ap, int[] ai, double[] ax, int col,
                               int[] xi, int[] pstack, double[] x, int n)
    {
        int top = Reach(lp, li, pinv, marked, ap, ai, col, xi, pstack, n);
        for (int p = top; p < n; p++) x[xi[p]] = 0.0;
        for (int p = ap[col]; p < ap[col + 1]; p++) x[ai[p]] = ax[p];
        for (int px = top; px < n; px++)
        {
            int j = xi[px];
            int jcol = pinv[j];
            if (jcol < 0) continue;
            double xj = x[j];               // L(j,j) = 1 (первая запись столбца)
            for (int p = lp[jcol] + 1; p < lp[jcol + 1]; p++)
                x[li[p]] -= lx[p] * xj;
        }
        return top;
    }

    private static int Reach(int[] lp, int[] li, int[] pinv, bool[] marked,
                             int[] ap, int[] ai, int col, int[] xi, int[] pstack, int n)
    {
        int top = n;
        for (int p = ap[col]; p < ap[col + 1]; p++)
        {
            int i = ai[p];
            if (!marked[i])
                top = Dfs(i, lp, li, pinv, marked, top, xi, pstack);
        }
        for (int p = top; p < n; p++) marked[xi[p]] = false;
        return top;
    }

    private static int Dfs(int j0, int[] lp, int[] li, int[] pinv, bool[] marked,
                           int top, int[] xi, int[] pstack)
    {
        int head = 0;
        xi[0] = j0;
        while (head >= 0)
        {
            int j = xi[head];
            int jnew = pinv[j];
            if (!marked[j])
            {
                marked[j] = true;
                pstack[head] = jnew < 0 ? 0 : lp[jnew];
            }
            bool done = true;
            int p2 = jnew < 0 ? 0 : lp[jnew + 1];
            for (int p = pstack[head]; p < p2; p++)
            {
                int i = li[p];
                if (marked[i]) continue;
                pstack[head] = p;
                xi[++head] = i;
                done = false;
                break;
            }
            if (done)
            {
                head--;
                xi[--top] = j;
            }
        }
        return top;
    }

    private static void Grow(ref int[] idx, ref double[] val, int newCap)
    {
        Array.Resize(ref idx, newCap);
        Array.Resize(ref val, newCap);
    }
}
