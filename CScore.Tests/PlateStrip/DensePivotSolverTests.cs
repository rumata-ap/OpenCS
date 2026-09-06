using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class DensePivotSolverTests
{
    [Fact]
    public void Solve_FiveByFive_ReturnsKnownSolution()
    {
        var a = new double[,]
        {
            { 4, 1, 0, 2, 0 },
            { 1, 5, 1, 0, 1 },
            { 0, 1, 6, 1, 0 },
            { 2, 0, 1, 7, 2 },
            { 0, 1, 0, 2, 8 }
        };
        var expected = new[] { 1.0, -2.0, 0.5, 3.0, -1.5 };
        var b = Multiply(a, expected);

        Assert.True(DensePivotSolver.Solve(a, b, out double[] x));
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], x[i], 9);
    }

    [Fact]
    public void Solve_SymmetricIndefiniteKktShape_ReturnsSolution()
    {
        // Форма KKT: [[H, Gᵀ], [G, 0]] — симметрична, но знаконеопределённа (Холецкий неприменим).
        var a = new double[,]
        {
            { 2, 0, 1, 1 },
            { 0, 2, 1, -1 },
            { 1, 1, 0, 0 },
            { 1, -1, 0, 0 }
        };
        var expected = new[] { 0.5, -0.25, 1.5, 2.0 };
        var b = Multiply(a, expected);

        Assert.True(DensePivotSolver.Solve(a, b, out double[] x));
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], x[i], 9);
    }

    [Fact]
    public void Solve_RequiresRowSwap_ReturnsSolution()
    {
        // Нулевой ведущий элемент на первом шаге — без перестановки строк решения нет.
        var a = new double[,] { { 0, 2 }, { 3, 1 } };
        var b = new double[] { 4, 5 };

        Assert.True(DensePivotSolver.Solve(a, b, out double[] x));
        Assert.Equal(1.0, x[0], 9);
        Assert.Equal(2.0, x[1], 9);
    }

    [Fact]
    public void Solve_SingularMatrix_ReturnsFalse()
    {
        var a = new double[,] { { 1, 2 }, { 2, 4 } };
        var b = new double[] { 1, 2 };

        Assert.False(DensePivotSolver.Solve(a, b, out _));
    }

    [Fact]
    public void Solve_NearlySingularMatrix_ReturnsFalseInsteadOfHugeCoefficients()
    {
        var a = new double[,] { { 1.0, 1.0 }, { 1.0, 1.0 + 1e-15 } };
        var b = new double[] { 1.0, 1.0 };

        Assert.False(DensePivotSolver.Solve(a, b, out _));
    }

    [Fact]
    public void Solve_ZeroMatrix_ReturnsFalse()
    {
        Assert.False(DensePivotSolver.Solve(new double[2, 2], new double[] { 1, 1 }, out _));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Solve_NonFiniteInput_ReturnsFalse(double bad)
    {
        var a = new double[,] { { bad, 1 }, { 1, 2 } };
        Assert.False(DensePivotSolver.Solve(a, new double[] { 1, 2 }, out _));

        var good = new double[,] { { 3.0, 1 }, { 1, 2 } };
        Assert.False(DensePivotSolver.Solve(good, new[] { bad, 2.0 }, out _));
    }

    [Fact]
    public void Solve_DoesNotMutateInputs()
    {
        var a = new double[,] { { 0, 2 }, { 3, 1 } };
        var b = new double[] { 4, 5 };
        var aCopy = (double[,])a.Clone();
        var bCopy = (double[])b.Clone();

        Assert.True(DensePivotSolver.Solve(a, b, out _));

        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(bCopy[i], b[i]);
            for (int j = 0; j < 2; j++)
                Assert.Equal(aCopy[i, j], a[i, j]);
        }
    }

    [Fact]
    public void Solve_MismatchedDimensions_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            DensePivotSolver.Solve(new double[2, 3], new double[2], out _));
        Assert.Throws<ArgumentException>(() =>
            DensePivotSolver.Solve(new double[2, 2], new double[3], out _));
    }

    static double[] Multiply(double[,] a, double[] x)
    {
        int n = x.Length;
        var result = new double[n];
        for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
            result[i] += a[i, j] * x[j];
        return result;
    }
}
