using CScore.Planar;

namespace CScore.Submodel;

public enum ShellContactKind { None, Vertex, Edge, Face, Degenerate, Warped }

/// <summary>Классификация контакта точки с треугольным или четырёхузловым shell-элементом.</summary>
public static class ShellContactGeometry
{
    public static ShellContactKind Classify(IReadOnlyList<PlanarVector3> points, PlanarVector3 probe,
        ResolvedTolerances tolerances)
    {
        if (points.Count is not (3 or 4) || !probe.IsFinite || points.Any(point => !point.IsFinite) || IsDegenerate(points, tolerances))
            return ShellContactKind.Degenerate;
        foreach (var point in points)
            if ((point - probe).Length <= tolerances.NodeCoincidenceM) return ShellContactKind.Vertex;
        for (var i = 0; i < points.Count; i++)
            if (ChainEnvironmentScan.DistancePointToSegment(probe, points[i], points[(i + 1) % points.Count]) <= tolerances.NodeCoincidenceM)
                return ShellContactKind.Edge;
        if (!IsNear(points, probe, tolerances)) return ShellContactKind.None;
        if (points.Count == 4 && !IsPlanar(points, tolerances)) return ShellContactKind.Warped;
        return IsInsideFace(points, probe, tolerances) ? ShellContactKind.Face : ShellContactKind.None;
    }

    static bool IsDegenerate(IReadOnlyList<PlanarVector3> points, ResolvedTolerances tolerances)
    {
        for (var i = 0; i < points.Count; i++)
        for (var j = i + 1; j < points.Count; j++)
            if ((points[j] - points[i]).Length <= tolerances.NodeCoincidenceM) return true;
        return (points[1] - points[0]).Cross(points[2] - points[0]).Length <= tolerances.NodeCoincidenceM * tolerances.NodeCoincidenceM;
    }
    static bool IsNear(IReadOnlyList<PlanarVector3> p, PlanarVector3 q, ResolvedTolerances t)
    {
        var s = t.LineDistanceM;
        return q.X >= p.Min(x => x.X)-s && q.X <= p.Max(x => x.X)+s && q.Y >= p.Min(x => x.Y)-s && q.Y <= p.Max(x => x.Y)+s && q.Z >= p.Min(x => x.Z)-s && q.Z <= p.Max(x => x.Z)+s;
    }
    static bool IsPlanar(IReadOnlyList<PlanarVector3> q, ResolvedTolerances t)
    {
        var normal = (q[1] - q[0]).Cross(q[2] - q[0]);
        return normal.Length > 0.0 && Math.Abs((q[3] - q[0]).Dot(normal.Normalize())) <= t.LineDistanceM;
    }
    static bool IsInsideFace(IReadOnlyList<PlanarVector3> p, PlanarVector3 q, ResolvedTolerances t)
    {
        if (p.Count == 3) return IsInsideTriangle(p[0], p[1], p[2], q, t);
        return (p[2]-p[0]).Length <= (p[3]-p[1]).Length
            ? IsInsideTriangle(p[0],p[1],p[2],q,t) || IsInsideTriangle(p[0],p[2],p[3],q,t)
            : IsInsideTriangle(p[0],p[1],p[3],q,t) || IsInsideTriangle(p[1],p[2],p[3],q,t);
    }
    static bool IsInsideTriangle(PlanarVector3 a, PlanarVector3 b, PlanarVector3 c, PlanarVector3 q, ResolvedTolerances t)
    {
        var normal = (b-a).Cross(c-a); var area = normal.Length;
        if (area <= 0.0) return false;
        var unit = normal * (1.0/area);
        if (Math.Abs((q-a).Dot(unit)) > t.LineDistanceM) return false;
        var u = (b-q).Cross(c-q).Dot(unit)/area;
        var v = (c-q).Cross(a-q).Dot(unit)/area;
        return u >= -1e-9 && v >= -1e-9 && 1-u-v >= -1e-9;
    }
}
