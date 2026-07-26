using CSTriangulation;

namespace CScore.Planar;

public static class PlanarRegionTopologyValidator
{
    public const double MinSignedArea = 1e-9;

    public static (double[] X, double[] Y) ToOpenLoop(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        int n = x.Count;
        if (n >= 2)
        {
            double dx = x[n - 1] - x[0], dy = y[n - 1] - y[0];
            if (Math.Sqrt(dx * dx + dy * dy) < Contour.CloseTolerance)
                n--;
        }
        return (x.Take(n).ToArray(), y.Take(n).ToArray());
    }

    public static double SignedArea(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        var (ox, oy) = ToOpenLoop(x, y);
        var poly = new double[ox.Length][];
        for (int i = 0; i < ox.Length; i++) poly[i] = [ox[i], oy[i]];
        return GeometryUtils.SignedArea(poly);
    }

    public static bool HasSelfIntersection(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        var (ox, oy) = ToOpenLoop(x, y);
        int n = ox.Length;
        for (int i = 0; i < n; i++)
        {
            int i2 = (i + 1) % n;
            for (int k = i + 1; k < n; k++)
            {
                int k2 = (k + 1) % n;
                if (k == i || k2 == i || k == i2) continue; // соседние рёбра делят вершину — не пересечение
                if (GeometryUtils.SegmentsIntersect(ox[i], oy[i], ox[i2], oy[i2], ox[k], oy[k], ox[k2], oy[k2]))
                    return true;
            }
        }
        return false;
    }

    public static (double[] X, double[] Y) NormalizeWinding(IReadOnlyList<double> x, IReadOnlyList<double> y, bool ccw)
    {
        var (ox, oy) = ToOpenLoop(x, y);
        bool isCcw = SignedArea(ox, oy) > 0;
        if (isCcw == ccw) return (ox, oy);
        return (ox.Reverse().ToArray(), oy.Reverse().ToArray());
    }

    public static void ValidateLoop(IReadOnlyList<double> x, IReadOnlyList<double> y, string loopName)
    {
        var (ox, oy) = ToOpenLoop(x, y);
        if (ox.Length < 3)
            throw new InvalidOperationException($"{loopName}: контур должен содержать не менее 3 вершин.");
        if (Math.Abs(SignedArea(ox, oy)) < MinSignedArea)
            throw new InvalidOperationException($"{loopName}: контур имеет нулевую или вырожденную площадь.");
        if (HasSelfIntersection(ox, oy))
            throw new InvalidOperationException($"{loopName}: контур самопересекается.");
    }
}
