namespace CSfea.Thermal.Elements;

/// <summary>
/// Линейный треугольный CST-элемент теплопроводности (3 узла, 1 DOF на узел).
/// Формулы совпадают с <c>GreenSectionPy/solvers/fire_thermal/_fire_thermal_solver_impl.py</c>.
/// </summary>
public static class HeatTri3
{
    /// <summary>Площадь треугольника по координатам трёх узлов.</summary>
    public static double Area(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        double det = (x2 - x1) * (y3 - y1) - (x3 - x1) * (y2 - y1);
        return 0.5 * Math.Abs(det);
    }

    /// <summary>
    /// Локальная матрица проводимости K<sub>e</sub> (3×3): K<sub>ij</sub> = (λ / (4A)) · (b<sub>i</sub>b<sub>j</sub> + c<sub>i</sub>c<sub>j</sub>).
    /// </summary>
    /// <param name="lambda">Теплопроводность λ, Вт/(м·°C).</param>
    /// <param name="coords">Координаты узлов: [x1, y1, x2, y2, x3, y3].</param>
    public static double[,] ElementK(double lambda, ReadOnlySpan<double> coords)
    {
        var geom = ComputeGeometry(coords);
        double factor = lambda / (4.0 * geom.Area);
        var k = new double[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                k[i, j] = factor * (geom.B(i) * geom.B(j) + geom.C(i) * geom.C(j));
        return k;
    }

    /// <summary>
    /// Локальная матрица ёмкости M<sub>e</sub> (3×3): M<sub>ii</sub> = ρc·A/6, M<sub>ij</sub> = ρc·A/12 (i≠j).
    /// </summary>
    /// <param name="rhocp">Объёмная теплоёмкость ρc, Дж/(м³·°C).</param>
    /// <param name="coords">Координаты узлов: [x1, y1, x2, y2, x3, y3].</param>
    public static double[,] ElementM(double rhocp, ReadOnlySpan<double> coords)
    {
        var geom = ComputeGeometry(coords);
        double factor = rhocp * geom.Area / 12.0;
        var m = new double[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                m[i, j] = factor * (i == j ? 2.0 : 1.0);
        return m;
    }

    private readonly struct ElementGeometry(double area, double b0, double b1, double b2, double c0, double c1, double c2)
    {
        public double Area { get; } = area;
        private readonly double _b0 = b0, _b1 = b1, _b2 = b2;
        private readonly double _c0 = c0, _c1 = c1, _c2 = c2;

        public double B(int i) => i switch { 0 => _b0, 1 => _b1, 2 => _b2, _ => throw new IndexOutOfRangeException() };
        public double C(int i) => i switch { 0 => _c0, 1 => _c1, 2 => _c2, _ => throw new IndexOutOfRangeException() };
    }

    private static ElementGeometry ComputeGeometry(ReadOnlySpan<double> coords)
    {
        if (coords.Length < 6)
            throw new ArgumentException("Ожидается 6 координат: [x1,y1, x2,y2, x3,y3].", nameof(coords));

        double x1 = coords[0], y1 = coords[1];
        double x2 = coords[2], y2 = coords[3];
        double x3 = coords[4], y3 = coords[5];

        double area = Area(x1, y1, x2, y2, x3, y3);
        if (area <= 1e-18)
            throw new ArgumentException("Вырожденный треугольный элемент (нулевая площадь).", nameof(coords));

        return new ElementGeometry(
            area,
            y2 - y3, y3 - y1, y1 - y2,
            x3 - x2, x1 - x3, x2 - x1);
    }
}
