namespace CSfea.Torsion;

/// <summary>
/// Квадратичный треугольный элемент (T6/LST) для функции Прандтля: ∇²φ = −2, λ=1.
/// Узлы: 0–2 — вершины CCW, 3 — середина 0–1, 4 — середина 1–2, 5 — середина 2–0.
/// coords = [x0,y0, x1,y1, x2,y2, x3,y3, x4,y4, x5,y5] (12 значений).
/// Геометрия субпараметрическая: якобиан строится только по 3 вершинам (прямолинейные стороны).
/// </summary>
public static class PrandtlTri6
{
    public const int NodesPerElement = 6;

    /// <summary>Барицентрические координаты (L1,L2) 6 локальных узлов, в порядке узлов элемента.</summary>
    public static readonly (double L1, double L2)[] NodeNaturalCoords =
    [
        (1.0, 0.0), (0.0, 1.0), (0.0, 0.0),
        (0.5, 0.5), (0.0, 0.5), (0.5, 0.0)
    ];

    /// <summary>Площадь по трём вершинам (первые 6 координат).</summary>
    public static double AreaFromCorners(ReadOnlySpan<double> coords)
        => PrandtlTri3.Area(coords[..6]);

    /// <summary>Значения 6 функций формы в точке с барицентрическими координатами (L1, L2).</summary>
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

    /// <summary>
    /// Локальная матрица жёсткости K (6×6): 3-точечная квадратура Гаусса в серединах рёбер
    /// эталонного треугольника — точна для подынтегрального выражения степени ≤2
    /// (произведение градиентов, линейных по L).
    /// </summary>
    public static double[,] ElementK(ReadOnlySpan<double> coords)
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
            double detJ = Jacobian(coords, dNdL1, dNdL2, dNdx, dNdy);
            if (Math.Abs(detJ) <= 1e-18)
                throw new ArgumentException("Вырожденный якобиан в точке квадратуры.", nameof(coords));

            double factor = w * Math.Abs(detJ);
            for (int i = 0; i < NodesPerElement; i++)
                for (int j = 0; j < NodesPerElement; j++)
                    ke[i, j] += factor * (dNdx[i] * dNdx[j] + dNdy[i] * dNdy[j]);
        }
        return ke;
    }

    /// <summary>
    /// F_i = 2·∫N_i dA (правая часть слабой формы −∇²φ=2). Аналитически: 0 для вершин
    /// (∫N_i dA=0 для квадратичных вершинных функций формы), 2A/3 для серединных узлов.
    /// </summary>
    public static double[] LoadVector(ReadOnlySpan<double> coords)
    {
        double mid = 2.0 * AreaFromCorners(coords) / 3.0;
        return new[] { 0.0, 0.0, 0.0, mid, mid, mid };
    }

    /// <summary>
    /// ∫N_i dA (для It = 2∫φ dA постпроцессора): 0 для вершин, A/3 для серединных узлов.
    /// Вывод: N_i,corner=L_i(2L_i−1) → ∫=2·(A/6)−A/3=0; N_i,mid=4L_iL_j → ∫=4·(A/12)=A/3,
    /// через ∫L_i^a L_j^b L_k^c dA = 2A·a!b!c!/(a+b+c+2)!.
    /// </summary>
    public static double[] MassVector(ReadOnlySpan<double> coords)
    {
        double mid = AreaFromCorners(coords) / 3.0;
        return new[] { 0.0, 0.0, 0.0, mid, mid, mid };
    }

    /// <summary>
    /// Градиент φ в узле <paramref name="nodeIndex"/> (0..5) — в отличие от T3, градиент внутри
    /// T6-элемента не константа, а линеен; вычисляется в барицентрических координатах именно
    /// этого узла (<see cref="NodeNaturalCoords"/>).
    /// </summary>
    public static (double dphidx, double dphidy) NodeGradient(
        int nodeIndex, ReadOnlySpan<double> coords, ReadOnlySpan<double> phi)
    {
        var (l1, l2) = NodeNaturalCoords[nodeIndex];
        Span<double> dNdL1 = stackalloc double[NodesPerElement];
        Span<double> dNdL2 = stackalloc double[NodesPerElement];
        Span<double> dNdx = stackalloc double[NodesPerElement];
        Span<double> dNdy = stackalloc double[NodesPerElement];
        ShapeFunctionDerivatives(l1, l2, dNdL1, dNdL2);
        Jacobian(coords, dNdL1, dNdL2, dNdx, dNdy);

        double dphidx = 0.0, dphidy = 0.0;
        for (int i = 0; i < NodesPerElement; i++)
        {
            dphidx += dNdx[i] * phi[i];
            dphidy += dNdy[i] * phi[i];
        }
        return (dphidx, dphidy);
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

    /// <summary>Субпараметрический якобиан: геометрия — линейный треугольник (3 вершины).</summary>
    static double Jacobian(
        ReadOnlySpan<double> coords, ReadOnlySpan<double> dNdL1, ReadOnlySpan<double> dNdL2,
        Span<double> dNdx, Span<double> dNdy)
    {
        double x0 = coords[0], y0 = coords[1];
        double x1 = coords[2], y1 = coords[3];
        double x2 = coords[4], y2 = coords[5];

        double dxD1 = x0 - x2, dyD1 = y0 - y2;
        double dxD2 = x1 - x2, dyD2 = y1 - y2;
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

    /// <summary>3-точечная квадратура на эталонном треугольнике (совпадает с точками узлов-середин).</summary>
    static readonly (double L1, double L2, double Weight)[] QuadraturePoints =
    [
        (0.5, 0.5, 1.0 / 6.0),
        (0.0, 0.5, 1.0 / 6.0),
        (0.5, 0.0, 1.0 / 6.0),
    ];
}
