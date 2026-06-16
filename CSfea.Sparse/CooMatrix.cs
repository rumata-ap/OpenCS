namespace CSfea.Sparse;

/// <summary>
/// Разреженная матрица в координатном (триплетном) формате COO — стратегия
/// сборки FEM-матриц (см. конспект: COO-триплеты → CSR/CSC). Дубликаты
/// (i, j) суммируются при конвертации. Аналог <c>scipy.sparse.coo_matrix</c>.
/// </summary>
public sealed class CooMatrix
{
    private readonly List<int> _i;
    private readonly List<int> _j;
    private readonly List<double> _v;

    /// <summary>Число строк.</summary>
    public int Rows { get; }

    /// <summary>Число столбцов.</summary>
    public int Cols { get; }

    /// <summary>Текущее число накопленных триплетов (с дубликатами).</summary>
    public int Count => _v.Count;

    public CooMatrix(int rows, int cols, int capacity = 0)
    {
        Rows = rows;
        Cols = cols;
        _i = new List<int>(capacity);
        _j = new List<int>(capacity);
        _v = new List<double>(capacity);
    }

    /// <summary>Добавить вклад в элемент (i, j).</summary>
    public void Add(int i, int j, double value)
    {
        _i.Add(i);
        _j.Add(j);
        _v.Add(value);
    }

    /// <summary>
    /// Добавить плотный блок элемента <paramref name="ke"/> по глобальным DOF
    /// <paramref name="dofs"/> (паттерн <c>np.meshgrid(dofs, dofs)</c>).
    /// </summary>
    public void AddBlock(int[] dofs, double[,] ke)
    {
        int n = dofs.Length;
        for (int a = 0; a < n; a++)
        {
            int ia = dofs[a];
            for (int b = 0; b < n; b++)
            {
                double val = ke[a, b];
                if (val == 0.0) continue;
                _i.Add(ia);
                _j.Add(dofs[b]);
                _v.Add(val);
            }
        }
    }

    /// <summary>Конвертация в CSC с суммированием дубликатов.</summary>
    public CscMatrix ToCsc()
    {
        int n = Cols;
        int nnzRaw = _v.Count;
        var colCount = new int[n + 1];
        for (int t = 0; t < nnzRaw; t++)
            colCount[_j[t] + 1]++;
        for (int c = 0; c < n; c++)
            colCount[c + 1] += colCount[c];

        var rowIdxTmp = new int[nnzRaw];
        var valTmp = new double[nnzRaw];
        var next = (int[])colCount.Clone();
        for (int t = 0; t < nnzRaw; t++)
        {
            int col = _j[t];
            int dest = next[col]++;
            rowIdxTmp[dest] = _i[t];
            valTmp[dest] = _v[t];
        }

        // Сжатие дубликатов внутри каждого столбца.
        var colPtr = new int[n + 1];
        var rowIdx = new int[nnzRaw];
        var values = new double[nnzRaw];
        var mark = new int[Rows];
        for (int r = 0; r < Rows; r++) mark[r] = -1;
        int nz = 0;
        for (int c = 0; c < n; c++)
        {
            int colStart = nz;
            for (int p = colCount[c]; p < colCount[c + 1]; p++)
            {
                int r = rowIdxTmp[p];
                if (mark[r] >= colStart)
                {
                    values[mark[r]] += valTmp[p];
                }
                else
                {
                    mark[r] = nz;
                    rowIdx[nz] = r;
                    values[nz] = valTmp[p];
                    nz++;
                }
            }
            colPtr[c + 1] = nz;
        }

        if (nz != nnzRaw)
        {
            Array.Resize(ref rowIdx, nz);
            Array.Resize(ref values, nz);
        }
        return new CscMatrix(Rows, Cols, colPtr, rowIdx, values);
    }

    /// <summary>Конвертация в CSR с суммированием дубликатов.</summary>
    public CsrMatrix ToCsr() => ToCsc().ToCsr();

    /// <summary>Плотное представление (для тестов/малых матриц).</summary>
    public double[,] ToDense()
    {
        var d = new double[Rows, Cols];
        for (int t = 0; t < _v.Count; t++)
            d[_i[t], _j[t]] += _v[t];
        return d;
    }
}
