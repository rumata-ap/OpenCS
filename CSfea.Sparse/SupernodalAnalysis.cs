namespace CSfea.Sparse;

/// <summary>
/// Ослабленное слияние суперузлов (relaxed amalgamation, правила CHOLMOD): соседние по дереву суперузлы объединяются,
/// если добавленных явных нулей немного. Крупнее блоки — эффективнее плотные ядра, ценой лишней памяти.
/// Слияние разрешено, если в новом суперузле не больше <see cref="Nrelax0"/> столбцов; либо не больше
/// <see cref="Nrelax1"/> и доля нулей меньше <see cref="Zrelax0"/>; либо не больше <see cref="Nrelax2"/> и доля меньше
/// <see cref="Zrelax1"/>; либо доля меньше <see cref="Zrelax2"/>.
/// </summary>
public sealed record SupernodeRelaxation(int Nrelax0, int Nrelax1, int Nrelax2, double Zrelax0, double Zrelax1, double Zrelax2)
{
    /// <summary>Значения по умолчанию CHOLMOD.</summary>
    public static SupernodeRelaxation Default { get; } = new(4, 16, 48, 0.8, 0.1, 0.05);

    /// <summary>Без слияния — только фундаментальные суперузлы (структура L без лишних нулей).</summary>
    public static SupernodeRelaxation None { get; } = new(0, 0, 0, 0, 0, 0);

    internal bool Accept(int newCols, double zeroFraction) =>
        newCols <= Nrelax0
        || (newCols <= Nrelax1 && zeroFraction < Zrelax0)
        || (newCols <= Nrelax2 && zeroFraction < Zrelax1)
        || zeroFraction < Zrelax2;
}

/// <summary>
/// Символический анализ суперузлового Холецкого A = L·Lᵀ: упорядочивание (AMD/RCM) с обратным обходом дерева
/// исключений, фундаментальные суперузлы с ослабленным слиянием, структура строк каждого суперузла и списки
/// обновлений «потомок → предок» для левосторонней (left-looking) численной факторизации.
/// Суперузел S — столбцы [<see cref="SuperStart"/>[S], SuperStart[S+1]); его строки — <see cref="Rows"/>
/// [<see cref="RowPtr"/>[S], RowPtr[S+1]), первыми идут сами столбцы; значения L хранятся плотным блоком
/// m×k по столбцам начиная с <see cref="LxPtr"/>[S] (над диагональю блока — нули).
/// </summary>
public sealed class SupernodalAnalysis
{
    /// <summary>Порядок системы.</summary>
    public int N { get; private init; }

    /// <summary>Перестановка: Perm[new] = old.</summary>
    public int[] Perm { get; private init; } = [];

    /// <summary>Обратная перестановка: IPerm[old] = new.</summary>
    public int[] IPerm { get; private init; } = [];

    /// <summary>Дерево исключений переставленной матрицы (в обратном обходе: parent[j] &gt; j).</summary>
    public int[] Parent { get; private init; } = [];

    /// <summary>Число ненулевых в столбцах L с диагональю.</summary>
    public int[] ColCount { get; private init; } = [];

    /// <summary>Число суперузлов.</summary>
    public int SupernodeCount { get; private init; }

    /// <summary>Число фундаментальных суперузлов (до слияния).</summary>
    public int FundamentalSupernodeCount { get; private init; }

    /// <summary>Первый столбец суперузла, длина SupernodeCount + 1.</summary>
    public int[] SuperStart { get; private init; } = [];

    /// <summary>Родитель суперузла в дереве суперузлов (−1 у корня).</summary>
    public int[] SuperParent { get; private init; } = [];

    /// <summary>Суперузел, которому принадлежит столбец.</summary>
    public int[] ColToSuper { get; private init; } = [];

    /// <summary>Начала структур строк суперузлов в <see cref="Rows"/>, длина SupernodeCount + 1.</summary>
    public int[] RowPtr { get; private init; } = [];

    /// <summary>Строки суперузлов (переставленные номера): сначала столбцы суперузла, затем по возрастанию.</summary>
    public int[] Rows { get; private init; } = [];

    /// <summary>Начала плотных блоков суперузлов в массиве значений L, длина SupernodeCount + 1.</summary>
    public long[] LxPtr { get; private init; } = [];

    /// <summary>
    /// Списки обновлений: для суперузла S элементы [UpdPtr[S], UpdPtr[S+1]) — потомки D (по возрастанию), чьи строки
    /// [UpdRowBegin, UpdRowEnd) (локальные позиции в структуре D) попадают в столбцы S.
    /// </summary>
    public int[] UpdPtr { get; private init; } = [];

    /// <summary>Потомок D для элемента списка обновлений.</summary>
    public int[] UpdSuper { get; private init; } = [];

    /// <summary>Первая локальная строка D, попадающая в столбцы S.</summary>
    public int[] UpdRowBegin { get; private init; } = [];

    /// <summary>Конец (не включая) локальных строк D в столбцах S.</summary>
    public int[] UpdRowEnd { get; private init; } = [];

    /// <summary>
    /// Строго нижний треугольник переставленной A (CSC, строки по возрастанию), построенный транспонированием
    /// верхнего: как и up-looking <see cref="SparseCholeskySolver"/>, факторизация видит только верхний треугольник
    /// (портрет Kff бывает структурно несимметричен — у музея 5 тыс. односторонних элементов).
    /// <see cref="LowerValMap"/> — индекс значения в исходной a.Values.
    /// </summary>
    internal int[] LowerColPtr { get; private init; } = [];
    internal int[] LowerRowIdx { get; private init; } = [];
    internal int[] LowerValMap { get; private init; } = [];

    /// <summary>Индекс диагонального элемента столбца в исходной a.Values (−1, если его нет).</summary>
    internal int[] DiagValMap { get; private init; } = [];

    /// <summary>Число ненулевых L (с диагональю) без явных нулей слияния.</summary>
    public long NnzL { get; private init; }

    /// <summary>Объём плотных блоков L (Σ m·k): столько чисел реально хранится.</summary>
    public long StoredL => LxPtr.Length == 0 ? 0 : LxPtr[^1];

    /// <summary>Флопы факторизации по структуре L без нулей слияния: Σ (число ненулевых столбца)².</summary>
    public double Flops { get; private init; }

    /// <summary>Наибольшее число строк суперузла (размер буфера обновлений — его квадрат порядка).</summary>
    public int MaxSupernodeRows { get; private init; }

    /// <summary>Наибольшее число столбцов суперузла.</summary>
    public int MaxSupernodeCols { get; private init; }

    /// <summary>Анализ по портрету симметричной матрицы (полный портрет, значения не нужны).</summary>
    public static SupernodalAnalysis Analyze(CscMatrix a, CholeskyOrdering ordering = CholeskyOrdering.Amd,
                                             SupernodeRelaxation? relaxation = null)
    {
        if (a.Rows != a.Cols)
            throw new ArgumentException("Холецкий определён только для квадратной матрицы.");
        relaxation ??= SupernodeRelaxation.Default;
        int n = a.Cols;

        // 1. Упорядочивание, затем обратный обход его дерева исключений: потомки каждого узла идут подряд.
        int[] perm0 = ordering == CholeskyOrdering.Rcm
            ? ReverseCuthillMcKee.ComputeOrdering(n, a.ColPtr, a.RowIdx)
            : ApproximateMinimumDegree.ComputeOrdering(n, a.ColPtr, a.RowIdx);
        var iperm0 = Invert(perm0);
        CholeskyPattern.Permute(a, iperm0, out var cp0, out var ri0, out _);
        int[] post = CholeskyPattern.Postorder(CholeskyPattern.EliminationTree(n, cp0, ri0));
        cp0 = ri0 = null!;
        var perm = new int[n];
        for (int k = 0; k < n; k++) perm[k] = perm0[post[k]];
        var iperm = Invert(perm);

        CholeskyPattern.Permute(a, iperm, out var pcp, out var pri, out var valMap);
        int[] parent = CholeskyPattern.EliminationTree(n, pcp, pri);
        int[] cc = CholeskyPattern.ColumnCounts(n, pcp, pri, parent);
        BuildLower(n, pcp, pri, valMap, out var lcp, out var lri, out var lval, out var diagMap);
        pcp = pri = valMap = null!;
        long nnzL = 0;
        double flops = 0;
        for (int j = 0; j < n; j++)
        {
            nnzL += cc[j];
            flops += (double)cc[j] * cc[j];
        }

        // 2. Фундаментальные суперузлы: j−1 и j вместе, если j — единственный ребёнок-родитель и структура вложена.
        var childCount = new int[n];
        for (int j = 0; j < n; j++)
            if (parent[j] != -1) childCount[parent[j]]++;
        var fStart = new List<int>();
        for (int j = 0; j < n; j++)
            if (j == 0 || !(parent[j - 1] == j && childCount[j] == 1 && cc[j - 1] == cc[j] + 1))
                fStart.Add(j);
        int nf = fStart.Count;
        fStart.Add(n);
        var colToF = new int[n];
        for (int s = 0; s < nf; s++)
            for (int j = fStart[s]; j < fStart[s + 1]; j++) colToF[j] = s;
        var fParent = new int[nf];
        for (int s = 0; s < nf; s++)
        {
            int pj = parent[fStart[s + 1] - 1];
            fParent[s] = pj == -1 ? -1 : colToF[pj];
        }

        // 3. Ослабленное слияние (как в CHOLMOD): сверху вниз, s сливается с группой, начинающейся с s+1, если
        // s+1 — её родитель (непрерывный последний ребёнок). Параметры группы хранятся у её первого суперузла.
        var gk = new int[nf];      // столбцов
        var gm = new int[nf];      // строк
        var gz = new double[nf];   // явных нулей
        for (int s = 0; s < nf; s++)
        {
            gk[s] = fStart[s + 1] - fStart[s];
            gm[s] = cc[fStart[s]];
        }
        var merged = new bool[nf];
        for (int s = nf - 2; s >= 0; s--)
        {
            if (fParent[s] != s + 1) continue;
            int p = s + 1;
            int k = gk[s] + gk[p];
            int m = gk[s] + gm[p];
            double stored = Trapezoid(k, m);
            double z = gz[s] + gz[p] + stored - Trapezoid(gk[s], gm[s]) - Trapezoid(gk[p], gm[p]);
            if (!relaxation.Accept(k, z / stored)) continue;
            gk[s] = k;
            gm[s] = m;
            gz[s] = z;
            merged[p] = true;
        }

        var superStart = new List<int>();
        var superM = new List<int>();
        for (int s = 0; s < nf; s++)
        {
            if (merged[s]) continue;
            superStart.Add(fStart[s]);
            superM.Add(gm[s]);
        }
        int ns = superStart.Count;
        superStart.Add(n);
        var colToSuper = new int[n];
        int maxCols = 0, maxRows = 0;
        for (int s = 0; s < ns; s++)
        {
            for (int j = superStart[s]; j < superStart[s + 1]; j++) colToSuper[j] = s;
            maxCols = Math.Max(maxCols, superStart[s + 1] - superStart[s]);
            maxRows = Math.Max(maxRows, superM[s]);
        }
        var superParent = new int[ns];
        for (int s = 0; s < ns; s++)
        {
            int pj = parent[superStart[s + 1] - 1];
            superParent[s] = pj == -1 ? -1 : colToSuper[pj];
        }

        // 4. Структуры строк: столбцы суперузла ∪ строки A ниже суперузла ∪ строки детей ниже суперузла.
        var rowPtr = new int[ns + 1];
        var lxPtr = new long[ns + 1];
        for (int s = 0; s < ns; s++)
        {
            rowPtr[s + 1] = checked(rowPtr[s] + superM[s]);
            lxPtr[s + 1] = lxPtr[s] + (long)superM[s] * (superStart[s + 1] - superStart[s]);
        }
        var rows = new int[rowPtr[ns]];
        var childHead = new int[ns];
        var childNext = new int[ns];
        Array.Fill(childHead, -1);
        for (int s = ns - 1; s >= 0; s--)
        {
            int p = superParent[s];
            if (p == -1) continue;
            childNext[s] = childHead[p];
            childHead[p] = s;
        }
        var mark = new int[n];
        Array.Fill(mark, -1);
        for (int s = 0; s < ns; s++)
        {
            int f = superStart[s], l = superStart[s + 1];
            int q = rowPtr[s];
            int end = rowPtr[s + 1];
            for (int j = f; j < l; j++)
            {
                rows[q++] = j;
                mark[j] = s;
            }
            for (int j = f; j < l; j++)
                for (int p = lcp[j]; p < lcp[j + 1]; p++)
                {
                    int r = lri[p];
                    if (r < l || mark[r] == s) continue;
                    if (q >= end) throw StructureMismatch(s);
                    mark[r] = s;
                    rows[q++] = r;
                }
            for (int c = childHead[s]; c != -1; c = childNext[c])
            {
                int ck = superStart[c + 1] - superStart[c];
                for (int p = rowPtr[c] + ck; p < rowPtr[c + 1]; p++)
                {
                    int r = rows[p];
                    if (r < l || mark[r] == s) continue;
                    if (q >= end) throw StructureMismatch(s);
                    mark[r] = s;
                    rows[q++] = r;
                }
            }
            if (q != end) throw StructureMismatch(s);
            Array.Sort(rows, rowPtr[s] + (l - f), end - rowPtr[s] - (l - f));
        }

        // 5. Списки обновлений: строки потомка D ниже его столбцов, разбитые на отрезки по суперузлам-предкам.
        var updCount = new int[ns + 1];
        for (int d = 0; d < ns; d++)
            ForEachUpdate(d, (t, _, _) => updCount[t + 1]++);
        for (int s = 0; s < ns; s++) updCount[s + 1] += updCount[s];
        int nUpd = updCount[ns];
        var updSuper = new int[nUpd];
        var updBegin = new int[nUpd];
        var updEnd = new int[nUpd];
        var fill = (int[])updCount.Clone();
        for (int d = 0; d < ns; d++)
            ForEachUpdate(d, (t, b, e) =>
            {
                int pos = fill[t]++;
                updSuper[pos] = d;
                updBegin[pos] = b;
                updEnd[pos] = e;
            });

        return new SupernodalAnalysis
        {
            N = n,
            Perm = perm,
            IPerm = iperm,
            Parent = parent,
            ColCount = cc,
            SupernodeCount = ns,
            FundamentalSupernodeCount = nf,
            SuperStart = superStart.ToArray(),
            SuperParent = superParent,
            ColToSuper = colToSuper,
            RowPtr = rowPtr,
            Rows = rows,
            LxPtr = lxPtr,
            UpdPtr = updCount,
            UpdSuper = updSuper,
            UpdRowBegin = updBegin,
            UpdRowEnd = updEnd,
            LowerColPtr = lcp,
            LowerRowIdx = lri,
            LowerValMap = lval,
            DiagValMap = diagMap,
            NnzL = nnzL,
            Flops = flops,
            MaxSupernodeRows = maxRows,
            MaxSupernodeCols = maxCols,
        };

        void ForEachUpdate(int d, Action<int, int, int> visit)
        {
            int k = superStart[d + 1] - superStart[d];
            int m = rowPtr[d + 1] - rowPtr[d];
            int b = k;
            while (b < m)
            {
                int t = colToSuper[rows[rowPtr[d] + b]];
                int lt = superStart[t + 1];
                int e = b + 1;
                while (e < m && rows[rowPtr[d] + e] < lt) e++;
                visit(t, b, e);
                b = e;
            }
        }
    }

    // Строго нижний треугольник как транспонированный верхний: A(i, k), i < k → элемент (k, i) столбца i.
    private static void BuildLower(int n, int[] cp, int[] ri, int[] valMap,
                                   out int[] lcp, out int[] lri, out int[] lval, out int[] diagMap)
    {
        lcp = new int[n + 1];
        diagMap = new int[n];
        Array.Fill(diagMap, -1);
        for (int k = 0; k < n; k++)
            for (int p = cp[k]; p < cp[k + 1]; p++)
            {
                int i = ri[p];
                if (i < k) lcp[i + 1]++;
                else if (i == k) diagMap[k] = valMap[p];
            }
        for (int i = 0; i < n; i++) lcp[i + 1] += lcp[i];
        lri = new int[lcp[n]];
        lval = new int[lcp[n]];
        var next = (int[])lcp.Clone();
        // Обход столбцов k по возрастанию — строки k в каждом столбце i ложатся уже отсортированными.
        for (int k = 0; k < n; k++)
            for (int p = cp[k]; p < cp[k + 1]; p++)
            {
                int i = ri[p];
                if (i >= k) continue;
                int q = next[i]++;
                lri[q] = k;
                lval[q] = valMap[p];
            }
    }

    // Хранимые элементы нижней трапеции k столбцов × m строк.
    private static double Trapezoid(int k, int m) => (double)k * m - (double)k * (k - 1) / 2;

    private static int[] Invert(int[] perm)
    {
        var inv = new int[perm.Length];
        for (int i = 0; i < perm.Length; i++) inv[perm[i]] = i;
        return inv;
    }

    private static InvalidOperationException StructureMismatch(int s) =>
        new($"Суперузловой анализ: структура суперузла {s} не совпала с подсчитанной (внутренняя ошибка).");
}
