using CScore.Fem.Fields;
using CScore.Planar;

namespace CScore.Fem.Fragment;

/// <summary>
/// Сопоставитель «новый КЭ → КЭ родителя» по опорной сетке (спека разд. 5), поиск — k-d-деревом центров
/// (<see cref="KdTree3"/>). Пластина — КЭ той же плоскости (нормаль и отстояние в пределах допуска), содержащий точку;
/// нет — ближайший той же плоскости не дальше <c>maxDistance</c>. Стержень — КЭ на той же оси, содержащий точку.
/// </summary>
public sealed class ParentMeshMapper
{
    /// <summary>1 − |cos| угла между нормалями (осями), ниже которого они параллельны.</summary>
    const double ParallelTolerance = 1e-4;

    readonly Index _shells;
    readonly Index _bars;
    readonly double _tol;

    public ParentMeshMapper(IReadOnlyList<FragmentRefElement> elements, double toleranceM = 0.001)
    {
        ArgumentNullException.ThrowIfNull(elements);
        _tol = toleranceM;
        _shells = new Index(elements.Where(e => e.IsShell).ToList());
        _bars = new Index(elements.Where(e => e.IsBar).ToList());
    }

    /// <summary>КЭ-пластина родителя для точки <paramref name="point"/> плоскости с нормалью <paramref name="normal"/>.</summary>
    public FragmentRefElement? MapShell(PlanarVector3 point, PlanarVector3 normal, double maxDistance = 0)
    {
        FragmentRefElement? best = null;
        double bestDistance = double.PositiveInfinity;
        foreach (var r in _shells.Near(point, Math.Max(maxDistance, _tol)))
        {
            if (1 - Math.Abs(normal.Dot(r.Direction)) > ParallelTolerance) continue;
            if (Math.Abs((point - r.Centroid).Dot(r.Direction)) > _tol) continue;
            double d = DistanceInPlane(r, point);
            if (d <= _tol) return r;
            if (d <= maxDistance && d < bestDistance) (best, bestDistance) = (r, d);
        }
        return best;
    }

    /// <summary>КЭ-стержень родителя на оси <paramref name="axis"/>, содержащий точку.</summary>
    public FragmentRefElement? MapBar(PlanarVector3 point, PlanarVector3 axis)
    {
        foreach (var r in _bars.Near(point, _tol))
        {
            if (1 - Math.Abs(axis.Dot(r.Direction)) > ParallelTolerance) continue;
            var a = r.Points[0];
            double t = (point - a).Dot(r.Direction);
            if (t < -_tol || t > r.Geometry.Length + _tol) continue;
            if ((point - (a + r.Direction * t)).Length <= _tol) return r;
        }
        return null;
    }

    /// <summary>Расстояние от точки (в плоскости КЭ) до его контура; 0 — внутри.</summary>
    static double DistanceInPlane(FragmentRefElement r, PlanarVector3 point)
    {
        var g = r.Geometry;
        var (ex, ey, _) = g.ShellFrame();
        var o = g.Points[g.Contour[0]];
        PlanarPoint2D Local(PlanarVector3 p) => new((p - o).Dot(ex), (p - o).Dot(ey));
        var poly = g.Contour.Select(i => Local(g.Points[i])).ToList();
        var q = Local(point);
        if (PlanarPolygonOps.Contains(poly, q, 0)) return 0;
        double best = double.PositiveInfinity;
        for (int i = 0; i < poly.Count; i++) best = Math.Min(best, SegmentDistance(q, poly[i], poly[(i + 1) % poly.Count]));
        return best;
    }

    static double SegmentDistance(PlanarPoint2D q, PlanarPoint2D a, PlanarPoint2D b)
    {
        double du = b.U - a.U, dv = b.V - a.V, l2 = du * du + dv * dv;
        double t = l2 > 0 ? Math.Clamp(((q.U - a.U) * du + (q.V - a.V) * dv) / l2, 0, 1) : 0;
        double x = a.U + du * t - q.U, y = a.V + dv * t - q.V;
        return Math.Sqrt(x * x + y * y);
    }

    /// <summary>
    /// Кандидаты по центрам: k ближайших с ростом k, пока центр последнего не дальше <c>reach</c> + наибольший радиус
    /// КЭ — дальше КЭ, содержащих точку или ближе <c>reach</c>, нет.
    /// </summary>
    sealed class Index(List<FragmentRefElement> items)
    {
        readonly KdTree3 _tree = new(items.Select(e => new FieldVec3(e.Centroid.X, e.Centroid.Y, e.Centroid.Z)).ToList());
        readonly double _radius = items.Count == 0 ? 0 : items.Max(e => e.Radius);

        public IEnumerable<FragmentRefElement> Near(PlanarVector3 p, double reach)
        {
            var q = new FieldVec3(p.X, p.Y, p.Z);
            int seen = 0;
            for (int k = 8; seen < items.Count; k *= 4)
            {
                var near = _tree.KNearest(q, Math.Min(k, items.Count));
                for (int i = seen; i < near.Count; i++) yield return items[near[i].Index];
                seen = near.Count;
                if (near[^1].Distance > reach + _radius) yield break;
            }
        }
    }
}
