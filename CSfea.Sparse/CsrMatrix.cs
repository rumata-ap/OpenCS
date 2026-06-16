namespace CSfea.Sparse;

/// <summary>
/// Разреженная матрица в формате CSR (compressed sparse row).
/// Удобна для итерационных решателей (SpMV построчно). Аналог
/// <c>scipy.sparse.csr_matrix</c>.
/// </summary>
public sealed class CsrMatrix
{
    /// <summary>Число строк.</summary>
    public int Rows { get; }

    /// <summary>Число столбцов.</summary>
    public int Cols { get; }

    /// <summary>Указатели начала строк, длина Rows+1.</summary>
    public int[] RowPtr { get; }

    /// <summary>Индексы столбцов ненулевых элементов, длина nnz.</summary>
    public int[] ColIdx { get; }

    /// <summary>Значения ненулевых элементов, длина nnz.</summary>
    public double[] Values { get; }

    /// <summary>Число ненулевых элементов.</summary>
    public int Nnz => Values.Length;

    public CsrMatrix(int rows, int cols, int[] rowPtr, int[] colIdx, double[] values)
    {
        Rows = rows;
        Cols = cols;
        RowPtr = rowPtr;
        ColIdx = colIdx;
        Values = values;
    }

    /// <summary>Произведение матрица-вектор: y = A · x.</summary>
    public double[] Multiply(double[] x)
    {
        if (x.Length != Cols)
            throw new ArgumentException("Несовместимая длина вектора в SpMV (CSR).");
        var y = new double[Rows];
        for (int r = 0; r < Rows; r++)
        {
            double s = 0.0;
            for (int p = RowPtr[r]; p < RowPtr[r + 1]; p++)
                s += Values[p] * x[ColIdx[p]];
            y[r] = s;
        }
        return y;
    }

    /// <summary>Диагональ матрицы (для Jacobi-предобуславливателя).</summary>
    public double[] Diagonal()
    {
        int n = Math.Min(Rows, Cols);
        var d = new double[n];
        for (int r = 0; r < n; r++)
            for (int p = RowPtr[r]; p < RowPtr[r + 1]; p++)
                if (ColIdx[p] == r) { d[r] = Values[p]; break; }
        return d;
    }
}
