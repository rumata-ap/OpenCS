namespace CScore.PlateStrip;

/// <summary>Общие операции над мембранно-изгибной матрицей плитного отклика.</summary>
internal static class PlateSectionResponseMath
{
    public static double[,] BuildH(double[,] a, double[,] b, double[,] d)
    {
        ValidateBlock(a, 3, 3, nameof(a));
        ValidateBlock(b, 3, 3, nameof(b));
        ValidateBlock(d, 3, 3, nameof(d));

        var h = new double[6, 6];
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
        {
            h[i, j] = a[i, j];
            h[i, j + 3] = b[i, j];
            h[i + 3, j] = b[j, i];
            h[i + 3, j + 3] = d[i, j];
        }
        return h;
    }

    public static PlateResultants ToResultants(double[] values)
    {
        if (values.Length != 6) throw new ArgumentException("Ожидалось шесть плитных усилий.", nameof(values));
        return new PlateResultants(values[0], values[1], values[2], values[3], values[4], values[5]);
    }

    public static double[] MatVec(double[,] matrix, double[] vector)
    {
        if (matrix.GetLength(1) != vector.Length)
            throw new ArgumentException("Размеры матрицы и вектора не согласованы.");
        var result = new double[matrix.GetLength(0)];
        for (int i = 0; i < matrix.GetLength(0); i++)
        for (int j = 0; j < matrix.GetLength(1); j++)
            result[i] += matrix[i, j] * vector[j];
        return result;
    }

    public static double[,] Clone(double[,] source)
    {
        var result = new double[source.GetLength(0), source.GetLength(1)];
        Array.Copy(source, result, source.Length);
        return result;
    }

    public static void ValidateBlock(double[,] matrix, int rows, int columns, string name)
    {
        if (matrix.GetLength(0) != rows || matrix.GetLength(1) != columns)
            throw new ArgumentException($"Матрица {name} должна иметь размер {rows}x{columns}.", name);
        for (int i = 0; i < rows; i++)
        for (int j = 0; j < columns; j++)
            if (!double.IsFinite(matrix[i, j]))
                throw new ArgumentException($"Матрица {name} содержит нечисловой элемент.", name);
    }

    public static PlateShellTangentResult BuildTangent(double[,] a, double[,] b, double[,] d,
                                                        double[,] ass, PlateResultants resultants)
    {
        ValidateBlock(ass, 2, 2, nameof(ass));
        return new PlateShellTangentResult
        {
            Nx = resultants.Nx,
            Ny = resultants.Ny,
            Nxy = resultants.Nxy,
            Mx = resultants.Mx,
            My = resultants.My,
            Mxy = resultants.Mxy,
            A = Clone(a),
            B = Clone(b),
            D = Clone(d),
            As = Clone(ass)
        };
    }

    /// <summary>Сравнивает две касательные с относительно-абсолютным допуском по всем четырём
    /// блокам A/B/D/As. Общая точка правды для PlateSectionTangentSnapshot и
    /// ShellMeshPatchPreflight (обе проверяют линейность материала пробами касательной).</summary>
    public static bool CloseTangent(PlateShellTangentResult a, PlateShellTangentResult b,
                                    double relativeTolerance, double absoluteTolerance)
    {
        return CloseBlock(a.A, b.A, relativeTolerance, absoluteTolerance) &&
               CloseBlock(a.B, b.B, relativeTolerance, absoluteTolerance) &&
               CloseBlock(a.D, b.D, relativeTolerance, absoluteTolerance) &&
               CloseBlock(a.As, b.As, relativeTolerance, absoluteTolerance);
    }

    public static bool CloseBlock(double[,] a, double[,] b, double relativeTolerance, double absoluteTolerance)
    {
        for (int i = 0; i < a.GetLength(0); i++)
        for (int j = 0; j < a.GetLength(1); j++)
        {
            double scale = Math.Max(Math.Abs(a[i, j]), Math.Abs(b[i, j]));
            if (Math.Abs(a[i, j] - b[i, j]) > absoluteTolerance + relativeTolerance * scale)
                return false;
        }
        return true;
    }
}
