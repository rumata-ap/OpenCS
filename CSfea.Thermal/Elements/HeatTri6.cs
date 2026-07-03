namespace CSfea.Thermal.Elements;

/// <summary>
/// Квадратичный треугольный элемент теплопроводности (6 узлов, 1 DOF/узел).
/// Узлы: 0–2 — вершины CCW, 3 — середина 0–1, 4 — 1–2, 5 — 2–0.
/// Интегрирование: 3-точечная квадратура на эталонном треугольнике.
/// </summary>
public static class HeatTri6
{
    public const int NodesPerElement = 6;

    /// <summary>Площадь по трём вершинам (первые 6 координат).</summary>
    public static double AreaFromCorners(ReadOnlySpan<double> coords)
        => HeatTri3.Area(coords[0], coords[1], coords[2], coords[3], coords[4], coords[5]);

    /// <summary>Локальная матрица проводимости K<sub>e</sub> (6×6).</summary>
    public static double[,] ElementK(double lambda, ReadOnlySpan<double> coords)
        => IntegrateStiffness(lambda, coords);

    /// <summary>Локальная согласованная матрица ёмкости M<sub>e</sub> (6×6).</summary>
    public static double[,] ElementM(double rhocp, ReadOnlySpan<double> coords)
        => IntegrateMass(rhocp, coords);

    /// <summary>
    /// Значения 6 функций формы в точке с барицентрическими координатами вершин (L1, L2, L3).
    /// </summary>
    public static void ShapeFunctions(double l1, double l2, Span<double> n)
    {
        if (n.Length < NodesPerElement)
            throw new ArgumentException("Буфер функций формы должен содержать 6 элементов.", nameof(n));
        double l3 = 1.0 - l1 - l2;
        n[0] = l1 * (2.0 * l1 - 1.0);
        n[1] = l2 * (2.0 * l2 - 1.0);
        n[2] = l3 * (2.0 * l3 - 1.0);
        n[3] = 4.0 * l1 * l2;
        n[4] = 4.0 * l2 * l3;
        n[5] = 4.0 * l3 * l1;
    }

    static double[,] IntegrateStiffness(double lambda, ReadOnlySpan<double> coords)
    {
        ValidateCoords(coords);
        var ke = new double[NodesPerElement, NodesPerElement];
        Span<double> dNdL1 = stackalloc double[NodesPerElement];
        Span<double> dNdL2 = stackalloc double[NodesPerElement];
        Span<double> dNdx = stackalloc double[NodesPerElement];
        Span<double> dNdy = stackalloc double[NodesPerElement];

        foreach (var (l1, l2, w) in QuadraturePoints)
        {
            ShapeFunctionDerivatives(l1, l2, dNdL1, dNdL2);
            double detJ = JacobianSubParametric(coords, dNdL1, dNdL2, dNdx, dNdy);
            if (Math.Abs(detJ) <= 1e-18)
                throw new ArgumentException("Вырожденный якобиан в точке квадратуры.", nameof(coords));

            double factor = lambda * w * Math.Abs(detJ);
            for (int i = 0; i < NodesPerElement; i++)
            {
                for (int j = 0; j < NodesPerElement; j++)
                    ke[i, j] += factor * (dNdx[i] * dNdx[j] + dNdy[i] * dNdy[j]);
            }
        }

        return ke;
    }

    static double[,] IntegrateMass(double rhocp, ReadOnlySpan<double> coords)
    {
        ValidateCoords(coords);
        var me = new double[NodesPerElement, NodesPerElement];
        Span<double> n = stackalloc double[NodesPerElement];
        Span<double> dNdL1 = stackalloc double[NodesPerElement];
        Span<double> dNdL2 = stackalloc double[NodesPerElement];
        Span<double> dNdx = stackalloc double[NodesPerElement];
        Span<double> dNdy = stackalloc double[NodesPerElement];

        foreach (var (l1, l2, w) in MassQuadraturePoints)
        {
            ShapeFunctions(l1, l2, n);
            ShapeFunctionDerivatives(l1, l2, dNdL1, dNdL2);
            double detJ = JacobianSubParametric(coords, dNdL1, dNdL2, dNdx, dNdy);
            if (Math.Abs(detJ) <= 1e-18)
                throw new ArgumentException("Вырожденный якобиан в точке квадратуры.", nameof(coords));

            double factor = rhocp * w * Math.Abs(detJ);
            for (int i = 0; i < NodesPerElement; i++)
            {
                for (int j = 0; j < NodesPerElement; j++)
                    me[i, j] += factor * n[i] * n[j];
            }
        }

        return me;
    }

    static void ShapeFunctionDerivatives(double l1, double l2, Span<double> dNdL1, Span<double> dNdL2)
    {
        double l3 = 1.0 - l1 - l2;
        dNdL1[0] = 4.0 * l1 - 1.0;
        dNdL2[0] = 0.0;
        dNdL1[1] = 0.0;
        dNdL2[1] = 4.0 * l2 - 1.0;
        dNdL1[2] = -(4.0 * l3 - 1.0);
        dNdL2[2] = -(4.0 * l3 - 1.0);
        dNdL1[3] = 4.0 * l2;
        dNdL2[3] = 4.0 * l1;
        dNdL1[4] = -4.0 * l2;
        dNdL2[4] = 4.0 * l3 - 4.0 * l2;
        dNdL1[5] = 4.0 * l3 - 4.0 * l1;
        dNdL2[5] = -4.0 * l1;
    }

    /// <summary>
    /// Субпараметрический якобиан: геометрия — линейный треугольник (3 вершины),
    /// поле температуры — квадратичное. Для прямолинейных сторон сетки пожара — точно.
    /// </summary>
    static double JacobianSubParametric(
        ReadOnlySpan<double> coords,
        ReadOnlySpan<double> dNdL1,
        ReadOnlySpan<double> dNdL2,
        Span<double> dNdx,
        Span<double> dNdy)
    {
        double x0 = coords[0], y0 = coords[1];
        double x1 = coords[2], y1 = coords[3];
        double x2 = coords[4], y2 = coords[5];

        double dxD1 = x0 - x2;
        double dyD1 = y0 - y2;
        double dxD2 = x1 - x2;
        double dyD2 = y1 - y2;

        double det = dxD1 * dyD2 - dxD2 * dyD1;
        double invDet = 1.0 / det;
        for (int i = 0; i < NodesPerElement; i++)
        {
            dNdx[i] = invDet * (dyD2 * dNdL1[i] - dyD1 * dNdL2[i]);
            dNdy[i] = invDet * (-dxD2 * dNdL1[i] + dxD1 * dNdL2[i]);
        }

        return det;
    }

    static void ValidateCoords(ReadOnlySpan<double> coords)
    {
        if (coords.Length < 12)
            throw new ArgumentException("Ожидается 12 координат (6 узлов × 2).", nameof(coords));
        if (AreaFromCorners(coords) <= 1e-18)
            throw new ArgumentException("Вырожденный треугольный элемент (нулевая площадь).", nameof(coords));
    }

    /// <summary>3-точечная квадратура на эталонном треугольнике (точность для K — кубические полиномы).</summary>
    static readonly (double L1, double L2, double Weight)[] QuadraturePoints =
    [
        (0.5, 0.5, 1.0 / 6.0),
        (0.0, 0.5, 1.0 / 6.0),
        (0.5, 0.0, 1.0 / 6.0),
    ];

    /// <summary>6-точечная квадратура степени 4 на эталонном треугольнике (точно для матрицы массы T6).</summary>
    static readonly (double L1, double L2, double Weight)[] MassQuadraturePoints =
    [
        (0.108103018168070, 0.445948490915965, 0.223381589678011 / 2.0),
        (0.445948490915965, 0.108103018168070, 0.223381589678011 / 2.0),
        (0.445948490915965, 0.445948490915965, 0.223381589678011 / 2.0),
        (0.816847572980459, 0.091576213509771, 0.109951743655322 / 2.0),
        (0.091576213509771, 0.816847572980459, 0.109951743655322 / 2.0),
        (0.091576213509771, 0.091576213509771, 0.109951743655322 / 2.0),
    ];
}
