using CScore;

namespace CSfea.Torsion;

/// <summary>Адаптеры геометрии CScore → TorsionBoundary.</summary>
public static class MaterialAreaExtensions
{
    /// <summary>
    /// Строит TorsionBoundary из MaterialArea. Внешний контур приводится к CCW,
    /// отверстия — к CW. Замыкающая (дублирующая) точка удаляется.
    /// </summary>
    public static TorsionBoundary FromMaterialArea(this MaterialArea area)
    {
        var hull = area.Hull ?? throw new InvalidOperationException("У MaterialArea отсутствует внешний контур (Hull).");

        double[] outerX = ToArrayNoClosure(hull.X, hull.Y, out double[] outerY0);
        double[] outerY = outerY0;
        EnsureOrientation(outerX, outerY, ccw: true);

        List<(double[] X, double[] Y)>? holes = null;
        if (area.Holes.Count > 0)
        {
            holes = new List<(double[] X, double[] Y)>(area.Holes.Count);
            foreach (var h in area.Holes)
            {
                double[] hx = ToArrayNoClosure(h.X, h.Y, out double[] hy0);
                double[] hy = hy0;
                EnsureOrientation(hx, hy, ccw: false);
                holes.Add((hx, hy));
            }
        }
        return new TorsionBoundary(outerX, outerY, holes);
    }

    /// <summary>Копирует IList в массив без замыкающей точки (если она дублирует первую по обеим осям).</summary>
    private static double[] ToArrayNoClosure(IList<double> srcX, IList<double> srcY, out double[] dstY)
    {
        int n = srcX.Count;
        if (n >= 2 && Math.Abs(srcX[0] - srcX[n - 1]) < 1e-12
                   && Math.Abs(srcY[0] - srcY[n - 1]) < 1e-12)
            n -= 1;
        var x = new double[n];
        var y = new double[n];
        for (int i = 0; i < n; i++) { x[i] = srcX[i]; y[i] = srcY[i]; }
        dstY = y;
        return x;
    }

    /// <summary>Знаковая площадь многоугольника (формула шнурка); &gt;0 для CCW.</summary>
    private static double SignedArea(double[] x, double[] y)
    {
        double s = 0.0;
        int n = x.Length;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            s += x[i] * y[j] - x[j] * y[i];
        }
        return 0.5 * s;
    }

    /// <summary>Разворачивает массив, если ориентация не соответствует требуемой.</summary>
    private static void EnsureOrientation(double[] x, double[] y, bool ccw)
    {
        double a = SignedArea(x, y);
        bool isCcw = a > 0.0;
        if (isCcw != ccw)
        {
            Array.Reverse(x);
            Array.Reverse(y);
        }
    }
}
