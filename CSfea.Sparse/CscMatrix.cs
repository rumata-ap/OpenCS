namespace CSfea.Sparse;

/// <summary>
/// Разреженная матрица в формате CSC (compressed sparse column).
/// Основной формат для прямого решателя (<see cref="SparseLuSolver"/>) и
/// срезов по столбцам. Аналог <c>scipy.sparse.csc_matrix</c>.
/// </summary>
public sealed class CscMatrix
{
    /// <summary>Число строк.</summary>
    public int Rows { get; }

    /// <summary>Число столбцов.</summary>
    public int Cols { get; }

    /// <summary>Указатели начала столбцов, длина Cols+1.</summary>
    public int[] ColPtr { get; }

    /// <summary>Индексы строк ненулевых элементов, длина nnz.</summary>
    public int[] RowIdx { get; }

    /// <summary>Значения ненулевых элементов, длина nnz.</summary>
    public double[] Values { get; }

    /// <summary>Число ненулевых элементов.</summary>
    public int Nnz => Values.Length;

    public CscMatrix(int rows, int cols, int[] colPtr, int[] rowIdx, double[] values)
    {
        Rows = rows;
        Cols = cols;
        ColPtr = colPtr;
        RowIdx = rowIdx;
        Values = values;
    }

    /// <summary>Произведение матрица-вектор: y = A · x.</summary>
    public double[] Multiply(double[] x)
    {
        if (x.Length != Cols)
            throw new ArgumentException("Несовместимая длина вектора в SpMV (CSC).");
        var y = new double[Rows];
        for (int c = 0; c < Cols; c++)
        {
            double xc = x[c];
            if (xc == 0.0) continue;
            for (int p = ColPtr[c]; p < ColPtr[c + 1]; p++)
                y[RowIdx[p]] += Values[p] * xc;
        }
        return y;
    }

    /// <summary>Конвертация CSC → CSR.</summary>
    public CsrMatrix ToCsr()
    {
        int nnz = Nnz;
        var rowPtr = new int[Rows + 1];
        for (int p = 0; p < nnz; p++)
            rowPtr[RowIdx[p] + 1]++;
        for (int r = 0; r < Rows; r++)
            rowPtr[r + 1] += rowPtr[r];

        var colIdx = new int[nnz];
        var values = new double[nnz];
        var next = (int[])rowPtr.Clone();
        for (int c = 0; c < Cols; c++)
        {
            for (int p = ColPtr[c]; p < ColPtr[c + 1]; p++)
            {
                int r = RowIdx[p];
                int dest = next[r]++;
                colIdx[dest] = c;
                values[dest] = Values[p];
            }
        }
        return new CsrMatrix(Rows, Cols, rowPtr, colIdx, values);
    }

    /// <summary>Плотное представление (для тестов/малых матриц).</summary>
    public double[,] ToDense()
    {
        var d = new double[Rows, Cols];
        for (int c = 0; c < Cols; c++)
            for (int p = ColPtr[c]; p < ColPtr[c + 1]; p++)
                d[RowIdx[p], c] = Values[p];
        return d;
    }
}
