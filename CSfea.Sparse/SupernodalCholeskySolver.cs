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
    private const long BigSupernode = 1L << 18;   // m·k, с которого обновления суперузла делятся по полосам столбцов
    private const int ColumnChunk = 96;            // ширина полосы столбцов крупного суперузла

    [ThreadStatic] private static double[]? _columnBuf;   // буфер обновления полосы (на поток)

    private SupernodalAnalysis? _an;
    private double[] _lx = [];
    private bool _factorized;

    /// <summary>Упорядочивание для <see cref="AnalyzePattern"/>.</summary>
    public CholeskyOrdering Ordering { get; init; } = CholeskyOrdering.Amd;

    /// <summary>Ослабленное слияние суперузлов (по умолчанию — как в CHOLMOD).</summary>
    public SupernodeRelaxation Relaxation { get; init; } = SupernodeRelaxation.Default;

    /// <summary>
    /// Число потоков факторизации (по умолчанию — число логических процессоров; 1 — последовательно). Потоки берут
    /// суперузлы, у которых готовы все дети; у крупных суперузлов (у корня) обновления делятся по полосам столбцов,
    /// а плотные произведения и решение панели — по блокам строк.
    /// Порядок суммирования каждого элемента L фиксирован — результат не зависит от числа потоков.
    /// </summary>
    public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;

    /// <summary>Последняя факторизация прошла как SPD (положительные ведущие элементы).</summary>
    public bool LastFactorizationSpd => FirstNonPositivePivot < 0;

    /// <summary>
    /// Неизвестная (в нумерации исходной матрицы) с первым по порядку исключения неположительным ведущим элементом
    /// последней факторизации; −1 — матрица положительно определена. Указывает на вырожденность (механизм) или
    /// потерю устойчивости в окрестности этой неизвестной.
    /// </summary>
    public int FirstNonPositivePivot { get; private set; } = -1;

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
        int threads = Math.Max(1, Math.Min(MaxDegreeOfParallelism, Environment.ProcessorCount));
        _factorized = false;
        int bad = threads == 1 || an.SupernodeCount < 2
            ? FactorizeSequential(a.Values)
            : FactorizeParallel(a.Values, threads);
        FirstNonPositivePivot = bad == int.MaxValue ? -1 : an.Perm[bad];
        _factorized = true;
    }

    // Возвращают наименьший переставленный столбец с неположительным ведущим элементом или int.MaxValue.
    private int FactorizeSequential(double[] values)
    {
        var an = _an!;
        var (map, buf) = Workspace();
        int bad = int.MaxValue;
        for (int s = 0; s < an.SupernodeCount; s++)
            bad = Math.Min(bad, FactorSupernode(s, values, map, buf, null));
        return bad;
    }

    // Планировщик по дереву суперузлов: готов — у кого факторизованы все дети; стек готовых (LIFO) держит работу
    // потока ближе к только что посчитанным блокам. Потоки — выделенные (не пул), пул занят делением крупных блоков.
    private int FactorizeParallel(double[] values, int threads)
    {
        var an = _an!;
        int ns = an.SupernodeCount;
        var pending = new int[ns];
        for (int s = 0; s < ns; s++)
            if (an.SuperParent[s] != -1) pending[an.SuperParent[s]]++;
        var ready = new Stack<int>();
        for (int s = ns - 1; s >= 0; s--)
            if (pending[s] == 0) ready.Push(s);
        var gate = new object();
        using var available = new SemaphoreSlim(ready.Count);
        int done = 0, bad = int.MaxValue;
        Exception? error = null;
        var po = new ParallelOptions { MaxDegreeOfParallelism = threads };

        void Worker()
        {
            var (map, buf) = Workspace();
            while (true)
            {
                available.Wait();
                int s;
                lock (gate) s = ready.Pop();
                if (s < 0) return;
                try
                {
                    int col = FactorSupernode(s, values, map, buf, po);
                    if (col != int.MaxValue)
                        lock (gate) bad = Math.Min(bad, col);
                }
                catch (Exception ex)
                {
                    lock (gate) error ??= ex;
                }
                lock (gate)
                {
                    int parent = an.SuperParent[s];
                    int released = 0;
                    if (parent != -1 && --pending[parent] == 0)
                    {
                        ready.Push(parent);
                        released++;
                    }
                    // Конец (или ошибка — родитель всё равно ждёт детей, тогда считаем остальные впустую, но конечно).
                    if (++done == ns)
                    {
                        for (int t = 0; t < threads; t++) ready.Push(-1);
                        released += threads;
                    }
                    if (released > 0) available.Release(released);
                }
            }
        }

        var workers = new Thread[threads];
        for (int t = 0; t < threads; t++)
        {
            workers[t] = new Thread(Worker) { IsBackground = true, Name = $"CSfea Cholesky {t}" };
            workers[t].Start();
        }
        foreach (var w in workers) w.Join();
        if (error != null)
            throw new InvalidOperationException("Ошибка суперузловой факторизации: " + error.Message, error);
        return bad;
    }

    // Рабочие массивы потока: карта строк суперузла и буфер обновления.
    private (int[] Map, double[] Buf) Workspace()
    {
        var an = _an!;
        return (new int[an.N], new double[(long)an.MaxSupernodeRows * Math.Min(UpdateChunk, Math.Max(an.MaxSupernodeCols, 1))]);
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
    // Возвращает переставленный столбец первого неположительного ведущего элемента суперузла или int.MaxValue.
    private int FactorSupernode(int s, double[] aValues, int[] map, double[] buf, ParallelOptions? po)
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
        if ((long)m * k >= BigSupernode)
        {
            // Крупный суперузел: полосы его столбцов независимы — каждая проходит все обновления только для своих
            // столбцов. Разбиение фиксировано (не зависит от числа потоков), порядок потомков в элементе — тот же.
            int chunks = (k + ColumnChunk - 1) / ColumnChunk;
            if (po != null && po.MaxDegreeOfParallelism > 1)
                Parallel.For(0, chunks, po, ch => UpdateColumns(s, f + ch * ColumnChunk, Math.Min(l, f + (ch + 1) * ColumnChunk), map));
            else
                for (int ch = 0; ch < chunks; ch++)
                    UpdateColumns(s, f + ch * ColumnChunk, Math.Min(l, f + (ch + 1) * ColumnChunk), map);
        }
        else
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
                    DenseKernels.GemmNtSub(p, q, dk, lx, doff + t0, dm, lx, doff + t0, dm, buf, 0, p, lowerOnly: true, po);
                    Scatter(lx, off, m, f, rows, drp + t0, map, buf, p, 0, q);
                }
            }

        int local = DenseKernels.FactorPanel(lx, off, m, k, po: po);
        return local < 0 ? int.MaxValue : f + local;
    }

    // Обновления от всех потомков суперузла s, только для его глобальных столбцов [c0, c1).
    private void UpdateColumns(int s, int c0, int c1, int[] map)
    {
        var an = _an!;
        var lx = _lx;
        int[] rows = an.Rows;
        int f = an.SuperStart[s];
        int m = an.RowPtr[s + 1] - an.RowPtr[s];
        long off = an.LxPtr[s];
        var buf = _columnBuf;
        if (buf == null || buf.Length < an.MaxSupernodeRows * ColumnChunk)
            _columnBuf = buf = new double[an.MaxSupernodeRows * ColumnChunk];
        for (int u = an.UpdPtr[s]; u < an.UpdPtr[s + 1]; u++)
        {
            int d = an.UpdSuper[u];
            int drp = an.RowPtr[d], dm = an.RowPtr[d + 1] - drp;
            int b = an.UpdRowBegin[u], e = an.UpdRowEnd[u];
            // Строки D отсортированы: отрезок [tb, te) — те, что попадают в столбцы [c0, c1).
            if (rows[drp + e - 1] < c0 || rows[drp + b] >= c1) continue;
            int tb = LowerBound(rows, drp + b, drp + e, c0) - drp;
            int te = LowerBound(rows, drp + tb, drp + e, c1) - drp;
            if (tb == te) continue;
            int p = dm - tb, q = te - tb;
            int dk = an.SuperStart[d + 1] - an.SuperStart[d];
            long doff = an.LxPtr[d];
            Array.Clear(buf, 0, p * q);
            DenseKernels.GemmNtSub(p, q, dk, lx, doff + tb, dm, lx, doff + tb, dm, buf, 0, p, lowerOnly: true);
            Scatter(lx, off, m, f, rows, drp + tb, map, buf, p, 0, q);
        }
    }

    private static int LowerBound(int[] a, int lo, int hi, int value)
    {
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (a[mid] < value) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    /// <summary>
    /// Разброс столбцов [ta, tb) буфера обновления p×q в блок суперузла (начало off, m строк, первый столбец f):
    /// столбец t буфера — глобальный столбец rows[rowStart + t], строка i (i ≥ t) — глобальная строка rows[rowStart + i].
    /// </summary>
    private static void Scatter(double[] lx, long off, int m, int f, int[] rows, int rowStart, int[] map,
                                double[] buf, int p, int ta, int tb)
    {
        for (int t = ta; t < tb; t++)
        {
            long col = off + (long)(rows[rowStart + t] - f) * m;
            int bc = t * p;
            for (int i = t; i < p; i++)
                lx[col + map[rows[rowStart + i]]] += buf[bc + i];
        }
    }
}

