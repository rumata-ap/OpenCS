namespace CScore.Planar;

/// <summary>Полуплоскость <c>Nu·u + Nv·v ≥ C</c> в осях плоской области.</summary>
public readonly record struct PlanarHalfPlane(double Nu, double Nv, double C)
{
    /// <summary>Отстояние со знаком (в единицах нормали): больше нуля — внутри.</summary>
    public double Eval(PlanarPoint2D p) => Nu * p.U + Nv * p.V - C;
}

/// <summary>Многоугольник с отверстиями: внешний контур и отверстия, открытые (без повтора первой вершины).</summary>
public sealed record PlanarPolygonWithHoles(
    IReadOnlyList<PlanarPoint2D> Outer, IReadOnlyList<IReadOnlyList<PlanarPoint2D>> Holes)
{
    /// <summary>Площадь: внешний контур минус отверстия.</summary>
    public double Area => Math.Abs(PlanarPolygonOps.SignedArea(Outer)) - Holes.Sum(h => Math.Abs(PlanarPolygonOps.SignedArea(h)));
}

/// <summary>
/// Отсечение многоугольника с отверстиями полуплоскостью (обрезка стен полосой фрагмента этажа). Рёбра контуров
/// (внешний против часовой, отверстия по часовой — область всегда слева) обрезаются, части на прямой замыкаются
/// отрезками вдоль неё, из «супа» отрезков собираются петли: положительные — внешние контуры кусков, отрицательные —
/// отверстия. Отверстие, пересечённое прямой, становится вырезом контура; несвязный остаток даёт несколько кусков.
/// </summary>
public static class PlanarHalfPlaneClip
{
    /// <summary>
    /// Куски <paramref name="polygon"/> в полуплоскости <paramref name="keep"/>: внешний контур против часовой,
    /// отверстия по часовой; порядок — по (min V, min U). Куски площадью меньше <paramref name="minArea"/> отбрасываются.
    /// </summary>
    public static IReadOnlyList<PlanarPolygonWithHoles> Clip(
        PlanarPolygonWithHoles polygon, PlanarHalfPlane keep, double tol, double minArea = 1e-12)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        double norm = Math.Sqrt(keep.Nu * keep.Nu + keep.Nv * keep.Nv);
        if (norm < 1e-15) throw new ArgumentException("Нормаль полуплоскости нулевая.", nameof(keep));
        var h = new PlanarHalfPlane(keep.Nu / norm, keep.Nv / norm, keep.C / norm);
        // Направление прямой, при котором сохраняемая сторона слева.
        double tu = h.Nv, tv = -h.Nu;

        var contours = new List<IReadOnlyList<PlanarPoint2D>> { PlanarPolygonOps.Ccw(polygon.Outer) };
        foreach (var hole in polygon.Holes) contours.Add(PlanarPolygonOps.Ccw(hole).Reverse().ToList());

        var segments = new List<(PlanarPoint2D P, PlanarPoint2D Q)>();
        foreach (var c in contours)
            for (int i = 0; i < c.Count; i++)
            {
                var p = c[i];
                var q = c[(i + 1) % c.Count];
                int sp = Side(h.Eval(p), tol), sq = Side(h.Eval(q), tol);
                if (sp >= 0 && sq >= 0)
                {
                    // Ребро на прямой сохраняется, только если область по сохраняемую сторону от него.
                    if (sp == 0 && sq == 0 && (q.U - p.U) * tu + (q.V - p.V) * tv < 0) continue;
                    segments.Add((p, q));
                }
                else if (sp > 0 && sq < 0) segments.Add((p, Cross(p, q, h)));
                else if (sp < 0 && sq > 0) segments.Add((Cross(p, q, h), q));
            }

        // Концы цепочек на прямой: начало (+1) и конец (−1). Вдоль прямой они чередуются «конец, начало»;
        // замыкающий отрезок идёт от конца к следующему началу.
        var balance = new Dictionary<PlanarPoint2D, int>();
        foreach (var (p, q) in segments)
        {
            balance[p] = balance.GetValueOrDefault(p) + 1;
            balance[q] = balance.GetValueOrDefault(q) - 1;
        }
        var ends = balance.Where(kv => kv.Value != 0).OrderBy(kv => kv.Key.U * tu + kv.Key.V * tv).ToList();
        if (ends.Count % 2 != 0) throw new InvalidOperationException("Контур не замыкается при отсечении полуплоскостью.");
        for (int i = 0; i < ends.Count; i += 2)
        {
            if (ends[i].Value >= 0 || ends[i + 1].Value <= 0)
                throw new InvalidOperationException("Контур не замыкается при отсечении полуплоскостью.");
            segments.Add((ends[i].Key, ends[i + 1].Key));
        }

        var loops = AssembleLoops(segments);
        var outers = loops.Where(l => PlanarPolygonOps.SignedArea(l) > minArea).ToList();
        var holes = loops.Where(l => PlanarPolygonOps.SignedArea(l) < -minArea).ToList();
        var holesOf = outers.ToDictionary(o => o, _ => new List<IReadOnlyList<PlanarPoint2D>>());
        foreach (var hole in holes)
        {
            var owner = outers
                .Where(o => hole.Any(v => PlanarPolygonOps.Contains(o, v, -tol)) || hole.All(v => PlanarPolygonOps.Contains(o, v, tol)))
                .OrderBy(o => PlanarPolygonOps.SignedArea(o))
                .FirstOrDefault();
            owner ??= outers.FirstOrDefault();
            if (owner is not null) holesOf[owner].Add(hole);
        }
        return outers
            .Select(o => new PlanarPolygonWithHoles(o, holesOf[o]))
            .OrderBy(p => p.Outer.Min(q => q.V)).ThenBy(p => p.Outer.Min(q => q.U))
            .ToList();
    }

    static int Side(double f, double tol) => f > tol ? 1 : f < -tol ? -1 : 0;

    static PlanarPoint2D Cross(PlanarPoint2D p, PlanarPoint2D q, PlanarHalfPlane h)
    {
        double fp = h.Eval(p), fq = h.Eval(q), t = fp / (fp - fq);
        return new(p.U + (q.U - p.U) * t, p.V + (q.V - p.V) * t);
    }

    /// <summary>Петли из направленных отрезков; в узле с несколькими выходами — самый левый поворот.</summary>
    static List<List<PlanarPoint2D>> AssembleLoops(List<(PlanarPoint2D P, PlanarPoint2D Q)> segments)
    {
        var outgoing = new Dictionary<PlanarPoint2D, List<int>>();
        for (int i = 0; i < segments.Count; i++)
        {
            if (!outgoing.TryGetValue(segments[i].P, out var list)) outgoing[segments[i].P] = list = [];
            list.Add(i);
        }
        var used = new bool[segments.Count];
        var loops = new List<List<PlanarPoint2D>>();
        for (int start = 0; start < segments.Count; start++)
        {
            if (used[start]) continue;
            var loop = new List<PlanarPoint2D>();
            int current = start;
            for (int guard = 0; guard <= segments.Count; guard++)
            {
                used[current] = true;
                var (p, q) = segments[current];
                loop.Add(p);
                if (q.Equals(segments[start].P)) break;
                if (!outgoing.TryGetValue(q, out var candidates)) break;
                int next = -1;
                double best = double.NegativeInfinity;
                double du = q.U - p.U, dv = q.V - p.V;
                foreach (int k in candidates)
                {
                    if (used[k]) continue;
                    double eu = segments[k].Q.U - q.U, ev = segments[k].Q.V - q.V;
                    double angle = Math.Atan2(du * ev - dv * eu, du * eu + dv * ev);
                    if (angle > best) { best = angle; next = k; }
                }
                if (next < 0) break;
                current = next;
            }
            if (loop.Count >= 3) loops.Add(loop);
        }
        return loops;
    }
}
