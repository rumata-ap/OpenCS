namespace CScore.Planar;

/// <summary>
/// Операции с простыми многоугольниками в осях плоской области (открытый контур, без повтора первой вершины):
/// площадь, выпуклая оболочка, принадлежность точки, отсечение полуплоскостью и выпуклым окном, разность
/// «выпуклый − многоугольник» на выпуклые куски. Нужны следам АЖТ (<c>FemRigidJunctions</c>); Clipper в решении нет.
/// </summary>
public static class PlanarPolygonOps
{
    /// <summary>Вершина ближе этого к прямой отсечения считается лежащей на ней.</summary>
    const double LineTolerance = 1e-12;

    /// <summary>Ориентированная площадь (Гаусс): больше нуля — обход против часовой стрелки.</summary>
    public static double SignedArea(IReadOnlyList<PlanarPoint2D> p)
    {
        double s = 0;
        for (int i = 0; i < p.Count; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Count];
            s += a.U * b.V - b.U * a.V;
        }
        return s / 2;
    }

    /// <summary>Тот же контур с обходом против часовой стрелки.</summary>
    public static IReadOnlyList<PlanarPoint2D> Ccw(IReadOnlyList<PlanarPoint2D> p) =>
        SignedArea(p) < 0 ? p.Reverse().ToArray() : p;

    /// <summary>Выпуклая оболочка (монотонная цепь Эндрю) против часовой стрелки, без коллинеарных вершин.</summary>
    public static IReadOnlyList<PlanarPoint2D> ConvexHull(IEnumerable<PlanarPoint2D> pts, double tol = 1e-9)
    {
        var sorted = pts.OrderBy(q => q.U).ThenBy(q => q.V).ToList();
        var unique = new List<PlanarPoint2D>(sorted.Count);
        foreach (var q in sorted)
            if (unique.Count == 0 || Math.Abs(unique[^1].U - q.U) > tol || Math.Abs(unique[^1].V - q.V) > tol)
                unique.Add(q);
        if (unique.Count < 3) return unique;

        var hull = new List<PlanarPoint2D>(2 * unique.Count);
        foreach (var q in unique) Push(hull, q, 0, tol);
        int lowerCount = hull.Count;
        for (int i = unique.Count - 2; i >= 0; i--) Push(hull, unique[i], lowerCount - 1, tol);
        hull.RemoveAt(hull.Count - 1);
        return hull;

        static void Push(List<PlanarPoint2D> hull, PlanarPoint2D q, int floor, double tol)
        {
            // Правый поворот или коллинеарность (с допуском по площади) — вершина выбрасывается.
            while (hull.Count >= floor + 2 && Cross(hull[^2], hull[^1], q) <= tol * tol)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(q);
        }
    }

    /// <summary>Многоугольник выпуклый (допускаются коллинеарные вершины).</summary>
    public static bool IsConvex(IReadOnlyList<PlanarPoint2D> p, double tol = 1e-12)
    {
        if (p.Count < 3) return false;
        double sign = Math.Sign(SignedArea(p));
        for (int i = 0; i < p.Count; i++)
            if (sign * Cross(p[i], p[(i + 1) % p.Count], p[(i + 2) % p.Count]) < -tol)
                return false;
        return true;
    }

    /// <summary>Точка внутри или на границе (ближе <paramref name="tol"/> к ребру); отрицательный допуск — только строго внутри.</summary>
    public static bool Contains(IReadOnlyList<PlanarPoint2D> p, PlanarPoint2D q, double tol)
    {
        for (int i = 0; i < p.Count; i++)
        {
            double d = DistanceToSegment(q, p[i], p[(i + 1) % p.Count]);
            if (d <= tol) return true;
            if (tol < 0 && d <= -tol) return false;
        }
        bool inside = false;
        for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
        {
            if ((p[i].V > q.V) == (p[j].V > q.V)) continue;
            if (q.U < (p[j].U - p[i].U) * (q.V - p[i].V) / (p[j].V - p[i].V) + p[i].U) inside = !inside;
        }
        return inside;
    }

    /// <summary>
    /// Часть многоугольника слева (<paramref name="keepLeft"/>) или справа от направленной прямой a→b.
    /// Вершины на прямой (в пределах допуска) попадают в обе стороны — общее ребро не порождает щелей.
    /// </summary>
    public static IReadOnlyList<PlanarPoint2D> ClipHalfPlane(
        IReadOnlyList<PlanarPoint2D> p, PlanarPoint2D a, PlanarPoint2D b, bool keepLeft)
    {
        double len = Math.Sqrt((b.U - a.U) * (b.U - a.U) + (b.V - a.V) * (b.V - a.V));
        if (len < 1e-15 || p.Count == 0) return p;
        double Side(PlanarPoint2D q)
        {
            double s = Cross(a, b, q) / len;
            if (Math.Abs(s) <= LineTolerance) return 0;
            return keepLeft ? s : -s;
        }

        var result = new List<PlanarPoint2D>(p.Count + 2);
        for (int i = 0; i < p.Count; i++)
        {
            var cur = p[i];
            var next = p[(i + 1) % p.Count];
            double sc = Side(cur), sn = Side(next);
            if (sc >= 0) result.Add(cur);
            if ((sc > 0 && sn < 0) || (sc < 0 && sn > 0))
            {
                double t = sc / (sc - sn);
                result.Add(new PlanarPoint2D(cur.U + (next.U - cur.U) * t, cur.V + (next.V - cur.V) * t));
            }
        }
        return Dedup(result);
    }

    /// <summary>
    /// Сазерленд–Ходжмен: часть <paramref name="subject"/> внутри выпуклого окна. Если пересечение несвязно,
    /// куски соединены вырожденными рёбрами вдоль границы окна — площадь при этом верна.
    /// </summary>
    public static IReadOnlyList<PlanarPoint2D> ClipByConvex(
        IReadOnlyList<PlanarPoint2D> subject, IReadOnlyList<PlanarPoint2D> convexWindow)
    {
        var window = Ccw(convexWindow);
        IReadOnlyList<PlanarPoint2D> result = subject;
        for (int i = 0; i < window.Count && result.Count > 0; i++)
            result = ClipHalfPlane(result, window[i], window[(i + 1) % window.Count], keepLeft: true);
        return result.Count >= 3 ? result : [];
    }

    /// <summary>Разбиение простого многоугольника на треугольники (отсечение ушей), все против часовой стрелки.</summary>
    public static IReadOnlyList<IReadOnlyList<PlanarPoint2D>> Triangulate(IReadOnlyList<PlanarPoint2D> simple)
    {
        var poly = Ccw(simple).ToList();
        var triangles = new List<IReadOnlyList<PlanarPoint2D>>();
        int guard = poly.Count * poly.Count;
        while (poly.Count > 3 && guard-- > 0)
        {
            bool clipped = false;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[(i + poly.Count - 1) % poly.Count];
                var b = poly[i];
                var c = poly[(i + 1) % poly.Count];
                double turn = Cross(a, b, c);
                if (turn <= LineTolerance)
                {
                    // Коллинеарная вершина ушей не даёт — выбрасывается, площадь не меняется.
                    if (Math.Abs(turn) <= LineTolerance) { poly.RemoveAt(i); clipped = true; break; }
                    continue;
                }
                PlanarPoint2D[] tri = [a, b, c];
                bool blocked = poly.Any(q => !ReferenceEquals(q, a) && !ReferenceEquals(q, b) && !ReferenceEquals(q, c) &&
                                             !Same(q, a) && !Same(q, b) && !Same(q, c) && Contains(tri, q, 0));
                if (blocked) continue;
                triangles.Add(tri);
                poly.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped) break;
        }
        if (poly.Count == 3 && SignedArea(poly) > LineTolerance) triangles.Add(poly.ToArray());
        return triangles;
    }

    /// <summary>
    /// Выпуклые куски <paramref name="convex"/> \ <paramref name="hole"/>. Невыпуклое отверстие режется на
    /// треугольники; каждый вычитаемый выпуклый многоугольник отнимается отсечением по его рёбрам: часть снаружи
    /// ребра — в результат, внутренняя — дальше, остаток (пересечение) выбрасывается. Куски площадью меньше
    /// <paramref name="minArea"/> отбрасываются.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<PlanarPoint2D>> SubtractFromConvex(
        IReadOnlyList<PlanarPoint2D> convex, IReadOnlyList<PlanarPoint2D> hole, double minArea)
    {
        var cutters = IsConvex(hole) ? [Ccw(hole)] : Triangulate(hole);
        List<IReadOnlyList<PlanarPoint2D>> pieces = [Ccw(convex)];
        foreach (var cutter in cutters)
        {
            var next = new List<IReadOnlyList<PlanarPoint2D>>();
            foreach (var piece in pieces)
            {
                if (!BoxesOverlap(piece, cutter)) { next.Add(piece); continue; }
                var rest = piece;
                for (int i = 0; i < cutter.Count && rest.Count >= 3; i++)
                {
                    var a = cutter[i];
                    var b = cutter[(i + 1) % cutter.Count];
                    var outside = ClipHalfPlane(rest, a, b, keepLeft: false);
                    if (outside.Count >= 3 && Math.Abs(SignedArea(outside)) >= minArea) next.Add(outside);
                    rest = ClipHalfPlane(rest, a, b, keepLeft: true);
                }
            }
            pieces = next;
        }
        return pieces;
    }

    static double Cross(PlanarPoint2D a, PlanarPoint2D b, PlanarPoint2D c) =>
        (b.U - a.U) * (c.V - a.V) - (b.V - a.V) * (c.U - a.U);

    static bool Same(PlanarPoint2D a, PlanarPoint2D b) =>
        Math.Abs(a.U - b.U) <= LineTolerance && Math.Abs(a.V - b.V) <= LineTolerance;

    static List<PlanarPoint2D> Dedup(List<PlanarPoint2D> p)
    {
        var result = new List<PlanarPoint2D>(p.Count);
        foreach (var q in p)
            if (result.Count == 0 || !Same(result[^1], q)) result.Add(q);
        if (result.Count > 1 && Same(result[0], result[^1])) result.RemoveAt(result.Count - 1);
        return result.Count >= 3 ? result : [];
    }

    static bool BoxesOverlap(IReadOnlyList<PlanarPoint2D> a, IReadOnlyList<PlanarPoint2D> b) =>
        a.Min(q => q.U) < b.Max(q => q.U) && b.Min(q => q.U) < a.Max(q => q.U) &&
        a.Min(q => q.V) < b.Max(q => q.V) && b.Min(q => q.V) < a.Max(q => q.V);

    static double DistanceToSegment(PlanarPoint2D q, PlanarPoint2D a, PlanarPoint2D b)
    {
        double dx = b.U - a.U, dy = b.V - a.V;
        double l2 = dx * dx + dy * dy;
        double t = l2 <= 1e-24 ? 0 : Math.Clamp(((q.U - a.U) * dx + (q.V - a.V) * dy) / l2, 0, 1);
        double x = a.U + t * dx - q.U, y = a.V + t * dy - q.V;
        return Math.Sqrt(x * x + y * y);
    }
}
