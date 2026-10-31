using CScore.Fem.Loads;
using CScore.Planar;
using CScore.Submodel;

namespace CScore.Fem.Fragment.Masks;

/// <summary>КЭ пластины родителя в плоскости стены.</summary>
public sealed record FragmentPlaneElement(string Tag, FemElementGeometry Geometry)
{
    public PlanarVector3 Centroid { get; } = Geometry.Centroid;
}

/// <summary>Точка сэмпла кромки: <see cref="S"/> от начала отрезка, м; КЭ плоскости, содержащие точку, и функции формы.</summary>
public sealed record FragmentWallPoint(double S, PlanarVector3 X,
    IReadOnlyList<(FragmentPlaneElement E, double[] Shape)> Containing);

/// <summary>Геометрия одного отрезка кромки стены над сеткой родителя (общая для всех ключей).</summary>
public sealed class FragmentWallCutGeometry
{
    public required PlanarVector3 P { get; init; }
    public required PlanarVector3 Q { get; init; }
    public double Length => (Q - P).Length;
    /// <summary>Единичный P → Q.</summary>
    public required PlanarVector3 Axis { get; init; }
    public required PlanarVector3 WallNormal { get; init; }
    /// <summary>Нормаль сечения в плоскости стены — от фрагмента к отброшенной части.</summary>
    public required PlanarVector3 CutNormal { get; init; }
    public required IReadOnlyList<FragmentPlaneElement> Plane { get; init; }
    public required IReadOnlyList<FragmentWallPoint> Points { get; init; }
    /// <summary>Интервалы между соседними пересечениями с рёбрами и КЭ, содержащий середину (для прямой суммы).</summary>
    public required IReadOnlyList<(double S0, double S1, FragmentPlaneElement E)> Intervals { get; init; }
    internal Dictionary<string, List<FragmentPlaneElement>> ByNode { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Патч точки: КЭ, содержащие точку, и КЭ плоскости с общим с ними узлом.</summary>
    public IReadOnlyList<FragmentPlaneElement> Patch(FragmentWallPoint point)
    {
        var set = new Dictionary<string, FragmentPlaneElement>(StringComparer.Ordinal);
        foreach (var (e, _) in point.Containing)
        {
            set[e.Tag] = e;
            foreach (var tag in e.Geometry.NodeTags)
                if (ByNode.TryGetValue(tag, out var list))
                    foreach (var n in list) set[n.Tag] = n;
        }
        return set.Values.ToList();
    }
}

/// <summary>Кромка стены фрагмента (спека разд. 4.3): точки сэмплов и перемещения по функциям формы КЭ родителя.</summary>
public static class FragmentWallCutSampler
{
    const double ParallelTolerance = 1e-3;

    /// <summary>
    /// Геометрия отрезка <paramref name="p"/>→<paramref name="q"/>: КЭ плоскости пластины (нормаль параллельна
    /// <paramref name="shellNormal"/>, отстояние ≤ <paramref name="tol"/>), точки на пересечениях с рёбрами КЭ, концы и
    /// середины. <paramref name="outward"/> — нормаль разреза от фрагмента к отброшенной части; нормаль сечения — её
    /// проекция на плоскость пластины (кромка стены на z, кромка плиты и вертикальная кромка стены на боковой грани).
    /// </summary>
    public static FragmentWallCutGeometry Prepare(PlanarVector3 p, PlanarVector3 q, PlanarVector3 shellNormal,
        PlanarVector3 outward, IEnumerable<(string Tag, FemElementGeometry G)> shells, double tol)
    {
        var nw = shellNormal.Normalize();
        var axis = (q - p).Normalize();
        double length = (q - p).Length;
        var inPlane = outward - nw * outward.Dot(nw);
        if (inPlane.Length < 1e-6)
            throw new ArgumentException("Нормаль разреза перпендикулярна плоскости пластины — кромки в ней нет.", nameof(outward));
        var cutNormal = inPlane.Normalize();
        var e2 = nw.Cross(axis).Normalize();

        var plane = new List<FragmentPlaneElement>();
        foreach (var (tag, g) in shells)
        {
            if (g == null || !g.IsShell) continue;
            var n = g.ShellFrame().Z;
            if (n.Cross(nw).Length > ParallelTolerance) continue;
            if (Math.Abs((g.Centroid - p).Dot(nw)) > tol) continue;
            plane.Add(new FragmentPlaneElement(tag, g));
        }
        var byNode = new Dictionary<string, List<FragmentPlaneElement>>(StringComparer.Ordinal);
        foreach (var e in plane)
            foreach (var t in e.Geometry.NodeTags)
            {
                if (!byNode.TryGetValue(t, out var list)) byNode[t] = list = [];
                list.Add(e);
            }

        // Пересечения отрезка (v = 0, u ∈ [0, L]) с рёбрами КЭ в осях (u — вдоль отрезка, v — по e2).
        var cuts = new List<double> { 0, length };
        foreach (var e in plane)
        {
            var c = e.Geometry.Contour;
            for (int k = 0; k < c.Count; k++)
            {
                var a = e.Geometry.Points[c[k]] - p;
                var b = e.Geometry.Points[c[(k + 1) % c.Count]] - p;
                double va = a.Dot(e2), vb = b.Dot(e2);
                if (va * vb > 0 || Math.Abs(va - vb) < 1e-15) continue;
                double u = a.Dot(axis) + (b.Dot(axis) - a.Dot(axis)) * (va / (va - vb));
                if (u > tol && u < length - tol) cuts.Add(u);
            }
        }
        cuts.Sort();
        var crossings = new List<double>();
        foreach (double u in cuts)
            if (crossings.Count == 0 || u - crossings[^1] > tol) crossings.Add(u);
        if (length - crossings[^1] <= tol) crossings[^1] = length; else crossings.Add(length);

        var ss = new List<double>();
        for (int i = 0; i < crossings.Count; i++)
        {
            ss.Add(crossings[i]);
            if (i + 1 < crossings.Count) ss.Add((crossings[i] + crossings[i + 1]) / 2);
        }
        var points = ss.Select(s => Point(p + axis * s, s, plane)).ToList();
        var intervals = new List<(double, double, FragmentPlaneElement)>();
        for (int i = 0; i + 1 < crossings.Count; i++)
        {
            var mid = points[2 * i + 1];
            if (mid.Containing.Count > 0) intervals.Add((crossings[i], crossings[i + 1], mid.Containing[0].E));
        }
        return new FragmentWallCutGeometry
        {
            P = p, Q = q, Axis = axis, WallNormal = nw, CutNormal = cutNormal, Plane = plane, Points = points,
            Intervals = intervals, ByNode = byNode,
        };
    }

    static FragmentWallPoint Point(PlanarVector3 x, double s, IReadOnlyList<FragmentPlaneElement> plane)
    {
        var containing = new List<(FragmentPlaneElement, double[])>();
        foreach (var e in plane)
        {
            if ((x - e.Centroid).Length > 2 * MaxRadius(e)) continue;
            if (e.Geometry.ShellShapeAt(x) is { } shape) containing.Add((e, shape));
        }
        return new FragmentWallPoint(s, x, containing);
    }

    static double MaxRadius(FragmentPlaneElement e) => e.Geometry.Points.Max(pt => (pt - e.Centroid).Length);

    /// <summary>
    /// Перемещения в точке — по функциям формы КЭ, содержащих точку, среднее по ним; null — точки нет ни в одном КЭ или
    /// у узла КЭ нет перемещения.
    /// </summary>
    public static Dof6? Displacement(FragmentWallPoint point, Func<string, Dof6?> nodeDisplacement)
    {
        if (point.Containing.Count == 0) return null;
        var sum = Dof6.Zero;
        foreach (var (e, shape) in point.Containing)
        {
            var tags = e.Geometry.NodeTags;
            for (int k = 0; k < tags.Count; k++)
            {
                if (nodeDisplacement(tags[k]) is not { } u) return null;
                sum += u.Scale(shape[k]);
            }
        }
        return sum.Scale(1.0 / point.Containing.Count);
    }
}
