using CScore;

namespace OpenCS.Utilites;

/// <summary>Ребро контура с единичной внутренней нормалью и отступом.</summary>
public readonly record struct OffsetEdge(
    double StartX, double StartY, double EndX, double EndY,
    double NormalX, double NormalY, double Offset);

/// <summary>
/// Строит оффсетную линию замкнутого контура пересечением соседних смещённых рёбер.
/// </summary>
public static class ContourOffset
{
    /// <summary>Возвращает рёбра контура с внутренними нормалями и общим отступом.</summary>
    public static IReadOnlyList<OffsetEdge> BuildEdges(Contour hull, double defaultOffset)
    {
        var (xs, ys) = Vertices(hull);
        int n = xs.Count;
        if (n < 3) return [];

        double sign = SignedArea(xs, ys) >= 0 ? 1.0 : -1.0;
        var edges = new List<OffsetEdge>(n);
        for (int i = 0; i < n; i++)
        {
            double x0 = xs[i], y0 = ys[i];
            double x1 = xs[(i + 1) % n], y1 = ys[(i + 1) % n];
            double dx = x1 - x0, dy = y1 - y0;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-12) continue;
            edges.Add(new OffsetEdge(
                x0, y0, x1, y1,
                -sign * dy / len, sign * dx / len,
                defaultOffset));
        }
        return edges;
    }

    /// <summary>Возвращает вершины оффсетной линии по набору рёбер.</summary>
    public static IReadOnlyList<(double X, double Y)> Offset(IReadOnlyList<OffsetEdge> edges)
    {
        int n = edges.Count;
        if (n < 3) return [];

        var points = new (double X, double Y)[n];
        for (int i = 0; i < n; i++)
        {
            var previous = edges[(i - 1 + n) % n];
            var current = edges[i];

            double q1x = previous.StartX + previous.Offset * previous.NormalX;
            double q1y = previous.StartY + previous.Offset * previous.NormalY;
            double d1x = previous.EndX - previous.StartX;
            double d1y = previous.EndY - previous.StartY;

            double q2x = current.StartX + current.Offset * current.NormalX;
            double q2y = current.StartY + current.Offset * current.NormalY;
            double d2x = current.EndX - current.StartX;
            double d2y = current.EndY - current.StartY;

            points[i] = IntersectLines(q1x, q1y, d1x, d1y, q2x, q2y, d2x, d2y);
        }
        return points;
    }

    /// <summary>Строит оффсет с проверкой вырождения и самопересечения.</summary>
    public static bool TryOffset(Contour hull, double offset,
                                 out IReadOnlyList<(double X, double Y)> points,
                                 out string? error)
    {
        points = [];
        if (!double.IsFinite(offset) || offset < 0.0)
        {
            error = "Отступ должен быть конечным и неотрицательным.";
            return false;
        }

        var (sourceX, sourceY) = Vertices(hull);
        if (sourceX.Count < 3)
        {
            error = "Контур должен содержать минимум три ребра.";
            return false;
        }

        // Ограничение по габариту отсекает заведомо вырожденный отступ
        // ещё до пересечения смещённых рёбер. Для невыпуклых контуров это
        // консервативная, но безопасная проверка.
        double minSpan = Math.Min(sourceX.Max() - sourceX.Min(), sourceY.Max() - sourceY.Min());
        if (offset > 0.0 && offset >= minSpan / 2.0)
        {
            error = "Отступ слишком велик: оффсетная линия вырождается.";
            return false;
        }

        var edges = BuildEdges(hull, offset);
        if (edges.Count < 3)
        {
            error = "Контур должен содержать минимум три ребра.";
            return false;
        }

        var result = Offset(edges);
        if (result.Count < 3)
        {
            error = "Оффсет вырожден.";
            return false;
        }

        var (hx, hy) = Vertices(hull);
        double sourceArea = SignedArea(hx, hy);
        double offsetArea = SignedArea(result.Select(p => p.X).ToList(), result.Select(p => p.Y).ToList());
        if (Math.Abs(offsetArea) < 1e-9 || Math.Sign(offsetArea) != Math.Sign(sourceArea))
        {
            error = "Отступ слишком велик: оффсетная линия вырождается.";
            return false;
        }

        if (HasSelfIntersection(result))
        {
            error = "Отступ слишком велик: оффсетная линия самопересекается.";
            return false;
        }

        points = result;
        error = null;
        return true;
    }

    static (List<double> X, List<double> Y) Vertices(Contour hull)
    {
        var xs = hull.X.ToList();
        var ys = hull.Y.ToList();
        if (xs.Count >= 2 &&
            Math.Abs(xs[0] - xs[^1]) < Contour.CloseTolerance &&
            Math.Abs(ys[0] - ys[^1]) < Contour.CloseTolerance)
        {
            xs.RemoveAt(xs.Count - 1);
            ys.RemoveAt(ys.Count - 1);
        }
        return (xs, ys);
    }

    static double SignedArea(IList<double> xs, IList<double> ys)
    {
        double sum = 0.0;
        for (int i = 0; i < xs.Count; i++)
        {
            int j = (i + 1) % xs.Count;
            sum += xs[i] * ys[j] - xs[j] * ys[i];
        }
        return sum / 2.0;
    }

    static bool HasSelfIntersection(IReadOnlyList<(double X, double Y)> points)
    {
        int n = points.Count;
        for (int i = 0; i < n; i++)
            for (int j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1) continue;
                if (SegmentsIntersect(points[i], points[(i + 1) % n], points[j], points[(j + 1) % n]))
                    return true;
            }
        return false;
    }

    static bool SegmentsIntersect((double X, double Y) a, (double X, double Y) b,
                                  (double X, double Y) c, (double X, double Y) d)
    {
        double d1 = Cross(c, d, a), d2 = Cross(c, d, b);
        double d3 = Cross(a, b, c), d4 = Cross(a, b, d);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
               ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    static double Cross((double X, double Y) origin, (double X, double Y) a,
                        (double X, double Y) b) =>
        (a.X - origin.X) * (b.Y - origin.Y) - (a.Y - origin.Y) * (b.X - origin.X);

    static (double X, double Y) IntersectLines(
        double q1x, double q1y, double d1x, double d1y,
        double q2x, double q2y, double d2x, double d2y)
    {
        double cross = d1x * d2y - d1y * d2x;
        if (Math.Abs(cross) < 1e-12) return (q1x, q1y);
        double dx = q2x - q1x, dy = q2y - q1y;
        double t = (d2y * dx - d2x * dy) / cross;
        return (q1x + t * d1x, q1y + t * d1y);
    }
}
