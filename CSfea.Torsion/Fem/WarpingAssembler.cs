namespace CSfea.Torsion;

/// <summary>
/// Элементные интегралы для центра кручения через МКЭ (депланация ω, функции сдвига ψ/φ по
/// Тимошенко) — дословный порт формул <c>_assemble_torsion</c>/<c>_assemble_shear_load</c> из
/// sectionproperties (fea.py). Работает и с T3, и с T6 (используется одна и та же матрица
/// жёсткости K, что и для функции Прандтля — интегралы Лапласа не зависят от начала координат).
/// Все интегралы берутся в системе координат, центрированной на центроиде сечения (xc, yc).
/// </summary>
internal static class WarpingAssembler
{
    /// <summary>4-точечная квадратура на треугольнике, точная для полиномов степени ≤3.
    /// Веса даны в соглашении «сумма = площадь эталонного треугольника = 0.5» (как в PrandtlTri6).</summary>
    internal static readonly (double L1, double L2, double W)[] Quad4 =
    [
        (1.0 / 3.0, 1.0 / 3.0, -9.0 / 32.0),
        (0.6, 0.2, 25.0 / 96.0),
        (0.2, 0.6, 25.0 / 96.0),
        (0.2, 0.2, 25.0 / 96.0),
    ];

    internal readonly struct ElementResult
    {
        public required double[] FWarp { get; init; }
        public required double[] FPsi { get; init; }
        public required double[] FPhi { get; init; }
        public required double[] MX { get; init; }
        public required double[] MY { get; init; }
        public required double ScXint { get; init; }
        public required double ScYint { get; init; }
    }

    /// <summary>
    /// Считает элементные векторы FWarp/FPsi/FPhi (нагрузки задач депланации/сдвига),
    /// моментные векторы MX/MY (∫x·Nᵢ dA, ∫y·Nᵢ dA — для I_xω/I_yω после решения ω)
    /// и скалярные геометрические интегралы ScXint/ScYint (формула Трефтца не участвует).
    /// </summary>
    internal static ElementResult Integrate(ReadOnlySpan<double> coords, int nodesPerElement,
        double xc, double yc, double ixx, double iyy, double ixy, double nu)
    {
        var fWarp = new double[nodesPerElement];
        var fPsi = new double[nodesPerElement];
        var fPhi = new double[nodesPerElement];
        var mX = new double[nodesPerElement];
        var mY = new double[nodesPerElement];
        double scXint = 0.0, scYint = 0.0;

        Span<double> n = stackalloc double[6];
        Span<double> dNdx = stackalloc double[6];
        Span<double> dNdy = stackalloc double[6];
        Span<double> dNdL1 = stackalloc double[6];
        Span<double> dNdL2 = stackalloc double[6];

        foreach (var (l1, l2, w) in Quad4)
        {
            double l3 = 1.0 - l1 - l2;
            double detJ;
            if (nodesPerElement == 6)
            {
                PrandtlTri6.ShapeFunctions(l1, l2, n);
                PrandtlTri6.ShapeFunctionDerivatives(l1, l2, dNdL1, dNdL2);
                detJ = PrandtlTri6.Jacobian(coords, dNdL1, dNdL2, dNdx, dNdy);
            }
            else
            {
                n[0] = l1; n[1] = l2; n[2] = l3;
                double det = PrandtlTri3.Det(coords);
                double[] b = { coords[3] - coords[5], coords[5] - coords[1], coords[1] - coords[3] };
                double[] cc = { coords[4] - coords[2], coords[0] - coords[4], coords[2] - coords[0] };
                for (int i = 0; i < 3; i++) { dNdx[i] = b[i] / det; dNdy[i] = cc[i] / det; }
                detJ = det;
            }

            double weight = w * Math.Abs(detJ);

            // Физическая точка Гаусса — через 3 вершины (геометрия субпараметрическая).
            double x = l1 * coords[0] + l2 * coords[2] + l3 * coords[4];
            double y = l1 * coords[1] + l2 * coords[3] + l3 * coords[5];
            double xr = x - xc, yr = y - yc;

            var (d1, d2, h1, h2) = ShearParams(xr, yr, ixx, iyy, ixy);

            for (int i = 0; i < nodesPerElement; i++)
            {
                fWarp[i] += weight * (dNdx[i] * yr - dNdy[i] * xr);
                fPsi[i] += weight * (nu / 2.0 * (dNdx[i] * d1 + dNdy[i] * d2)
                                      + 2.0 * (1.0 + nu) * n[i] * (ixx * xr - ixy * yr));
                fPhi[i] += weight * (nu / 2.0 * (dNdx[i] * h1 + dNdy[i] * h2)
                                      + 2.0 * (1.0 + nu) * n[i] * (iyy * yr - ixy * xr));
                mX[i] += weight * n[i] * xr;
                mY[i] += weight * n[i] * yr;
            }
            scXint += weight * (iyy * xr + ixy * yr) * (xr * xr + yr * yr);
            scYint += weight * (ixx * yr + ixy * xr) * (xr * xr + yr * yr);
        }

        return new ElementResult
        {
            FWarp = fWarp, FPsi = fPsi, FPhi = fPhi, MX = mX, MY = mY,
            ScXint = scXint, ScYint = scYint
        };
    }

    /// <summary>Параметры сдвига Пилки (r=xr²−yr², q=2·xr·yr; d1,d2 — для ψ, h1,h2 — для φ)
    /// в точке (xr,yr), заданной относительно центроида сечения.</summary>
    internal static (double d1, double d2, double h1, double h2) ShearParams(
        double xr, double yr, double ixx, double iyy, double ixy)
    {
        double r = xr * xr - yr * yr, q = 2.0 * xr * yr;
        double d1 = ixx * r - ixy * q, d2 = ixy * r + ixx * q;
        double h1 = -ixy * r + iyy * q, h2 = -iyy * r - ixy * q;
        return (d1, d2, h1, h2);
    }
}
