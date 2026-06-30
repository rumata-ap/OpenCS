namespace CSfea.Torsion;

/// <summary>Дискретизированная граница для МГЭ: точки контуров + циклическая топология.</summary>
public sealed class BoundaryDiscrete
{
    /// <summary>Координаты X всех точек (контур за контуром).</summary>
    public double[] X { get; init; } = [];
    /// <summary>Координаты Y.</summary>
    public double[] Y { get; init; } = [];
    /// <summary>«Следующая вершина» в рамках каждого контура (замыкание по контуру).</summary>
    public int[] J1 { get; init; } = [];
    /// <summary>Размер каждого замкнутого контура (внешний, затем отверстия).</summary>
    public int[] LoopSizes { get; init; } = [];
}

/// <summary>Нарезка контуров в граничные элементы для МГЭ.</summary>
public static class BoundaryDiscretizer
{
    /// <summary>
    /// Нарезает внешний контур и отверстия на отрезки длиной ≤ maxElementSize,
    /// сохраняя все исходные вершины. Возвращает точки + loop_sizes (размеры контуров).
    /// </summary>
    public static BoundaryDiscrete Discretize(TorsionBoundary boundary, double maxElementSize)
    {
        if (maxElementSize <= 0.0) throw new ArgumentOutOfRangeException(nameof(maxElementSize));

        var xs = new List<double>();
        var ys = new List<double>();
        var loopSizes = new List<int>();

        int start = 0;
        SubdivideLoop(boundary.OuterX, boundary.OuterY, maxElementSize, xs, ys);
        loopSizes.Add(xs.Count - start);
        start = xs.Count;

        if (boundary.Holes != null)
        {
            foreach (var h in boundary.Holes)
            {
                SubdivideLoop(h.X, h.Y, maxElementSize, xs, ys);
                loopSizes.Add(xs.Count - start);
                start = xs.Count;
            }
        }

        int n = xs.Count;
        var j1 = new int[n];
        int offset = 0;
        foreach (int size in loopSizes)
        {
            for (int k = 0; k < size; k++)
                j1[offset + k] = offset + (k + 1) % size;
            offset += size;
        }
        return new BoundaryDiscrete { X = xs.ToArray(), Y = ys.ToArray(), J1 = j1, LoopSizes = loopSizes.ToArray() };
    }

    /// <summary>Разбивает замкнутый контур на отрезки ≤ maxElementSize, добавляя точки в xs/ys.</summary>
    private static void SubdivideLoop(double[] x, double[] y, double maxElementSize,
        List<double> xs, List<double> ys)
    {
        int n = x.Length;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            double dx = x[j] - x[i], dy = y[j] - y[i];
            double len = Math.Sqrt(dx * dx + dy * dy);
            int m = Math.Max(1, (int)Math.Ceiling(len / maxElementSize));
            for (int k = 0; k < m; k++)
            {
                double t = (double)k / m;
                xs.Add(x[i] + t * dx);
                ys.Add(y[i] + t * dy);
            }
        }
    }
}
