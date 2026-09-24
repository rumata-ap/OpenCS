using CScore.Planar;
using CSTriangulation;

namespace CScore.PlateStrip;

/// <summary>Геометрия следов опор полосы во внутренних координатах (U, V) региона плиты.
///
/// Все проверки работают на open-loop контуре (<see cref="PlanarRegionTopologyValidator.ToOpenLoop"/>):
/// контуры PlanarRegion хранятся с дублированной замыкающей вершиной.
///
/// <b>Принадлежность плите — с учётом границы.</b> Граница внешнего контура и граница отверстия
/// принадлежат плите: край проёма — такое же ребро плиты, и опора вдоль него допустима.
/// Исключается только строгая внутренность отверстия. Ray-casting
/// (<see cref="GeometryUtils.PointInPolygon(double, double, double[][])"/>) на границе
/// недетерминирован, поэтому граница проверяется расстоянием, а ray-casting — только для
/// строгой внутренности.
///
/// Класс публичный: Runner сверки (<c>OpenCS.OpenSees.CScore</c>) использует те же проверки.</summary>
public static class StripSupportGeometry
{
    /// <summary>Расстояние от точки до следа: до точки или до ломаной.</summary>
    public static double DistanceToFootprint(PlanarPoint2D p, PlanarConstraintGeometry footprint)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(footprint);
        var points = footprint.Points;
        if (points.Count == 0) return double.PositiveInfinity;
        if (points.Count == 1) return Distance(p, points[0]);

        double best = double.PositiveInfinity;
        for (int i = 0; i < points.Count - 1; i++)
            best = Math.Min(best, DistanceToSegment(p, points[i], points[i + 1]));
        return best;
    }

    /// <summary>Точка принадлежит плите: на границе hull или строго внутри него, и не строго внутри
    /// отверстия (граница отверстия плите принадлежит).</summary>
    public static bool IsInsideRegion(PlanarPoint2D p, Contour hull, IEnumerable<Contour> holes, double tol)
    {
        ArgumentNullException.ThrowIfNull(hull);
        ArgumentNullException.ThrowIfNull(holes);
        var hullLoop = OpenLoop(hull);
        if (DistanceToLoop(p, hullLoop) <= tol) return true;
        if (!GeometryUtils.PointInPolygon(p.U, p.V, hullLoop)) return false;

        foreach (var hole in holes)
        {
            var holeLoop = OpenLoop(hole);
            if (DistanceToLoop(p, holeLoop) <= tol) continue;
            if (GeometryUtils.PointInPolygon(p.U, p.V, holeLoop)) return false;
        }
        return true;
    }

    /// <summary>Части отрезка AB, принадлежащие плите. Отрезок режется во всех точках пересечения
    /// с рёбрами hull и отверстий (включая касание вершин); подотрезок сохраняется, если он длиннее
    /// tol и его середина принадлежит плите. Отрезок на границе сохраняется целиком.</summary>
    public static IReadOnlyList<(PlanarPoint2D A, PlanarPoint2D B)> ClipSegmentToRegion(
        PlanarPoint2D a, PlanarPoint2D b, Contour hull, IEnumerable<Contour> holes, double tol)
    {
        ArgumentNullException.ThrowIfNull(hull);
        var holeList = (holes ?? throw new ArgumentNullException(nameof(holes))).ToList();
        double length = Distance(a, b);
        if (length <= tol) return [];

        var parameters = new List<double> { 0.0, 1.0 };
        foreach (var loop in new[] { OpenLoop(hull) }.Concat(holeList.Select(OpenLoop)))
            for (int i = 0; i < loop.Length; i++)
            {
                var c = loop[i];
                var d = loop[(i + 1) % loop.Length];
                AddIntersectionParameters(a, b, new(c[0], c[1]), new(d[0], d[1]), parameters);
            }

        parameters.Sort();
        var distinct = new List<double>();
        foreach (double t in parameters)
            if (distinct.Count == 0 || (t - distinct[^1]) * length > tol)
                distinct.Add(t);
        if (distinct[^1] < 1.0) distinct[^1] = 1.0;

        var pieces = new List<(PlanarPoint2D A, PlanarPoint2D B)>();
        for (int i = 0; i < distinct.Count - 1; i++)
        {
            var from = Lerp(a, b, distinct[i]);
            var to = Lerp(a, b, distinct[i + 1]);
            if (Distance(from, to) <= tol) continue;
            if (!IsInsideRegion(Lerp(from, to, 0.5), hull, holeList, tol)) continue;

            // Смежные принятые куски склеиваются: разрез в точке касания не должен дробить след.
            if (pieces.Count > 0 && Distance(pieces[^1].B, from) <= tol)
                pieces[^1] = (pieces[^1].A, to);
            else
                pieces.Add((from, to));
        }
        return pieces;
    }

    /// <summary>Все точки следа и середины его отрезков лежат на границе hull.</summary>
    public static bool IsOnHullBoundary(PlanarConstraintGeometry footprint, Contour hull, double tol)
    {
        ArgumentNullException.ThrowIfNull(footprint);
        ArgumentNullException.ThrowIfNull(hull);
        var loop = OpenLoop(hull);
        var points = footprint.Points;
        if (points.Count == 0) return false;
        for (int i = 0; i < points.Count; i++)
        {
            if (DistanceToLoop(points[i], loop) > tol) return false;
            if (i < points.Count - 1 && DistanceToLoop(Lerp(points[i], points[i + 1], 0.5), loop) > tol)
                return false;
        }
        return true;
    }

    internal static double[][] OpenLoop(Contour contour)
    {
        var (x, y) = PlanarRegionTopologyValidator.ToOpenLoop(contour.X, contour.Y);
        var loop = new double[x.Length][];
        for (int i = 0; i < x.Length; i++) loop[i] = [x[i], y[i]];
        return loop;
    }

    internal static double DistanceToLoop(PlanarPoint2D p, double[][] loop)
    {
        double best = double.PositiveInfinity;
        for (int i = 0; i < loop.Length; i++)
        {
            var c = loop[i];
            var d = loop[(i + 1) % loop.Length];
            best = Math.Min(best, DistanceToSegment(p, new(c[0], c[1]), new(d[0], d[1])));
        }
        return best;
    }

    /// <summary>Расстояние между точками.</summary>
    public static double Distance(PlanarPoint2D a, PlanarPoint2D b) =>
        Math.Sqrt((a.U - b.U) * (a.U - b.U) + (a.V - b.V) * (a.V - b.V));

    /// <summary>Расстояние от точки до отрезка AB.</summary>
    public static double DistanceToSegment(PlanarPoint2D p, PlanarPoint2D a, PlanarPoint2D b)
    {
        double du = b.U - a.U, dv = b.V - a.V;
        double lengthSquared = du * du + dv * dv;
        if (lengthSquared <= 0.0) return Distance(p, a);
        double t = Math.Clamp(((p.U - a.U) * du + (p.V - a.V) * dv) / lengthSquared, 0.0, 1.0);
        return Distance(p, new PlanarPoint2D(a.U + t * du, a.V + t * dv));
    }

    internal static PlanarPoint2D Lerp(PlanarPoint2D a, PlanarPoint2D b, double t) =>
        new(a.U + (b.U - a.U) * t, a.V + (b.V - a.V) * t);

    /// <summary>Параметры t ∈ [0, 1] на AB точек пересечения с отрезком CD; для коллинеарного
    /// перекрытия — проекции концов CD.</summary>
    static void AddIntersectionParameters(
        PlanarPoint2D a, PlanarPoint2D b, PlanarPoint2D c, PlanarPoint2D d, List<double> parameters)
    {
        double ru = b.U - a.U, rv = b.V - a.V;
        double su = d.U - c.U, sv = d.V - c.V;
        double denominator = ru * sv - rv * su;
        double qu = c.U - a.U, qv = c.V - a.V;
        double scale = Math.Max(1e-300, Math.Sqrt((ru * ru + rv * rv) * (su * su + sv * sv)));

        if (Math.Abs(denominator) / scale < 1e-12)
        {
            // Параллельны: пересечение возможно только при коллинеарности.
            double cross = qu * rv - qv * ru;
            if (Math.Abs(cross) / Math.Max(1e-300, Math.Sqrt(ru * ru + rv * rv)) > 1e-9) return;
            double rr = ru * ru + rv * rv;
            AddClamped(parameters, (qu * ru + qv * rv) / rr);
            AddClamped(parameters, ((d.U - a.U) * ru + (d.V - a.V) * rv) / rr);
            return;
        }

        double t = (qu * sv - qv * su) / denominator;
        double u = (qu * rv - qv * ru) / denominator;
        const double eps = 1e-12;
        if (u < -eps || u > 1.0 + eps) return;
        AddClamped(parameters, t);
    }

    static void AddClamped(List<double> parameters, double t)
    {
        if (t > 0.0 && t < 1.0) parameters.Add(t);
    }
}
