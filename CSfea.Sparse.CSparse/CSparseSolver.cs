using CSparse;
using CSparse.Double;
using CSparse.Double.Factorization;
using CSfea.Sparse;

namespace CSfea.Sparse.CSparseBackend;

/// <summary>
/// Реализация <see cref="ISparseSolver"/> на CSparse.NET (SparseLU с
/// AMD-переупорядочиванием). Используется как эталон для кросс-валидации
/// собственного <see cref="SparseLuSolver"/> (см. конспект).
/// </summary>
public sealed class CSparseSolver : ISparseSolver
{
    private SparseLU? _lu;
    private int _n;

    /// <summary>Порог частичного выбора ведущего элемента.</summary>
    public double Tolerance { get; init; } = 1.0;

    /// <summary>Стратегия переупорядочивания столбцов.</summary>
    public ColumnOrdering Ordering { get; init; } = ColumnOrdering.MinimumDegreeAtPlusA;

    public void Factorize(CscMatrix a)
    {
        _n = a.Cols;
        var m = new SparseMatrix(a.Rows, a.Cols, a.Nnz);
        Array.Copy(a.ColPtr, m.ColumnPointers, a.Cols + 1);
        Array.Copy(a.RowIdx, m.RowIndices, a.Nnz);
        Array.Copy(a.Values, m.Values, a.Nnz);
        _lu = SparseLU.Create(m, Ordering, Tolerance);
    }

    public double[] Solve(double[] b)
    {
        if (_lu == null)
            throw new InvalidOperationException("Сначала вызовите Factorize.");
        var x = new double[_n];
        _lu.Solve(b, x);
        return x;
    }

    public void Dispose() { }

    /// <summary>Однократная факторизация и решение.</summary>
    public static double[] SolveOnce(CscMatrix a, double[] b)
    {
        using var s = new CSparseSolver();
        s.Factorize(a);
        return s.Solve(b);
    }
}
