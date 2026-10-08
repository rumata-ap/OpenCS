using global::CSparse;
using global::CSparse.Double;

namespace CSfea.Sparse;

/// <summary>
/// Упорядочивание приближённой минимальной степени (AMD) по портрету A + Aᵀ для разрежённого Холецкого.
/// Пока — обёртка над CSparse.NET (Davis, AMD); своя реализация — позже. Возвращает перестановку
/// perm[newIndex] = oldIndex. На пространственных схемах заполнение L в десятки раз меньше, чем у RCM
/// (музей, 645 тыс. DOF: nnz(L) 1,0·10⁸ против 2,8·10⁹).
/// </summary>
public static class ApproximateMinimumDegree
{
    /// <summary>Вычислить AMD-перестановку по CSC-портрету квадратной матрицы (значения не нужны).</summary>
    public static int[] ComputeOrdering(int n, int[] colPtr, int[] rowIdx)
    {
        if (n == 0) return [];
        int nnz = colPtr[n];
        var m = new SparseMatrix(n, n, nnz);
        Array.Copy(colPtr, m.ColumnPointers, n + 1);
        Array.Copy(rowIdx, m.RowIndices, nnz);
        Array.Fill(m.Values, 1.0);
        // CSparse возвращает n + 1 элементов (последний — служебный).
        var p = global::CSparse.Ordering.AMD.Generate(m, ColumnOrdering.MinimumDegreeAtPlusA);
        return p.AsSpan(0, n).ToArray();
    }
}
