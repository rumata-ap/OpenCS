namespace CSfea.Torsion;

/// <summary>
/// Линейный треугольный элемент для функции Прандтля φ: ∇²φ = −2, λ=1.
/// coords = [x1,y1, x2,y2, x3,y3].
/// </summary>
public static class PrandtlTri3
{
    /// <summary>Удвоенная площадь (со знаком) треугольника.</summary>
    public static double Det(ReadOnlySpan<double> c)
        => (c[2] - c[0]) * (c[5] - c[1]) - (c[4] - c[0]) * (c[3] - c[1]);

    /// <summary>Площадь треугольника (&gt;0).</summary>
    public static double Area(ReadOnlySpan<double> c) => Math.Abs(Det(c)) * 0.5;

    /// <summary>
    /// Элементная матрица жёсткости K_ij = (1/(4A))·(b_i·b_j + c_i·c_j),
    /// где b=(y2-y3, y3-y1, y1-y2), c=(x3-x2, x1-x3, x2-x1).
    /// </summary>
    public static double[,] ElementK(ReadOnlySpan<double> c)
    {
        double det = Det(c);
        double area = Math.Abs(det) * 0.5;
        double factor = 1.0 / (4.0 * area);
        double[] b = { c[3] - c[5], c[5] - c[1], c[1] - c[3] };
        double[] cc = { c[4] - c[2], c[0] - c[4], c[2] - c[0] };
        var k = new double[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                k[i, j] = factor * (b[i] * b[j] + cc[i] * cc[j]);
        return k;
    }

    /// <summary>
    /// Вектор правой части F_i = +2·(A/3). Уравнение −∇²φ = 2 (переписанное из ∇²φ=−2)
    /// после слабой формы даёт K·φ = ∫2·N_i dA = 2·(A/3)·[1,1,1].
    /// </summary>
    public static double[] LoadVector(ReadOnlySpan<double> c)
    {
        double area = Area(c);
        double val = 2.0 * area / 3.0;
        return new[] { val, val, val };
    }

    /// <summary>Интеграл от функций формы ∫N_i dA = A/3 (для постпроцессора It = 2∫φ dA).</summary>
    public static double[] MassVector(ReadOnlySpan<double> c)
    {
        double area = Area(c);
        double val = area / 3.0;
        return new[] { val, val, val };
    }
}
