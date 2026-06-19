namespace CSfea.Sparse;

/// <summary>Приёмник вкладов разрежённой матрицы: накопление A[i,j] += value.</summary>
public interface IMatrixSink
{
    /// <summary>Добавить вклад в элемент (i, j).</summary>
    void Add(int i, int j, double value);
}
