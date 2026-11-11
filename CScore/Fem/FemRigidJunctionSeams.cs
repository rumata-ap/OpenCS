using CScore.Planar;

namespace CScore.Fem;

/// <summary>
/// Следы АЖТ у стыка с сохранённой (импортной) сеткой фрагмента (спека 2026-11-10-fragment-keep-parent-mesh): вершина
/// следа на кромке области, общей с сохранённой сеткой, стала бы новым узлом стыка, которого у сохранённой сетки нет, —
/// висячий узел, лестница перемешивает сохранённый КонЭ. Поэтому куски следа, подходящие к такой кромке ближе
/// <c>inset</c>, обрезаются параллельной ей прямой на расстоянии <c>inset</c> внутрь области: стык остаётся из узлов
/// сохранённой сетки, ведущий узел стержня на стыке связан с областью общим узлом.
/// </summary>
public static class FemRigidJunctionSeams
{
    /// <param name="keptPoints">Узлы сохранённой сетки (глобальные координаты).</param>
    /// <param name="inset">Отступ обрезки от кромки стыка, м.</param>
    /// <param name="tol">Допуск «узел на кромке» и «в плоскости области», м.</param>
    public static RigidJunctionTraceSet AwayFromKeptSeams(RigidJunctionTraceSet set, PlanarRegion region,
        IReadOnlyList<PlanarVector3> keptPoints, double inset, double tol = 1e-3)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(region);
        if (set.Traces.Count == 0 || keptPoints.Count == 0 || inset <= 0) return set;
        var seams = SeamEdges(region, keptPoints, tol);
        if (seams.Count == 0) return set;

        var traces = new List<RigidJunctionTrace>();
        var clipped = new List<string>();
        var dropped = new List<string>();
        double minArea = Math.Pow(0.01 * region.MeshMaxElementSizeM, 2);
        foreach (var trace in set.Traces)
        {
            bool changed = false;
            var pieces = new List<IReadOnlyList<PlanarPoint2D>>();
            foreach (var piece in trace.Pieces)
            {
                var p = piece;
                foreach (var (a, b) in seams)
                {
                    if (p.Count < 3 || !Near(p, a, b, inset)) continue;
                    var c = Centroid(p);
                    double side = Cross(a, b, c);
                    if (Math.Abs(side) < 1e-15) continue;
                    // Прямая a′b′ — кромка, сдвинутая на inset в сторону куска (внутрь области).
                    double ux = b.U - a.U, uv = b.V - a.V, len = Math.Sqrt(ux * ux + uv * uv);
                    double nx = -uv / len * Math.Sign(side), ny = ux / len * Math.Sign(side);
                    var a1 = new PlanarPoint2D(a.U + nx * inset, a.V + ny * inset);
                    var b1 = new PlanarPoint2D(b.U + nx * inset, b.V + ny * inset);
                    p = PlanarPolygonOps.ClipHalfPlane(p, a1, b1, keepLeft: side > 0);
                    changed = true;
                }
                if (p.Count >= 3 && Math.Abs(PlanarPolygonOps.SignedArea(p)) > minArea) pieces.Add(p);
            }
            if (!changed) { traces.Add(trace); continue; }
            string label = trace.MemberTags.Count > 0 ? trace.MemberTags[0] : trace.Id;
            if (pieces.Count == 0) { dropped.Add(label); continue; }
            clipped.Add(label);
            traces.Add(trace with { Pieces = pieces });
        }
        if (clipped.Count == 0 && dropped.Count == 0) return set;
        var diagnostics = new List<FemValidationDiagnostic>(set.Diagnostics);
        string d = inset.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture);
        if (clipped.Count > 0)
            diagnostics.Add(new("rigid_junction_trace_kept_seam",
                $"Следы АЖТ стержней {Short(clipped)} у стыка с сохранённой сеткой области «{region.Tag}» обрезаны в {d} м от стыка — " +
                "новых узлов на стыке нет.", false, [.. clipped]));
        if (dropped.Count > 0)
            diagnostics.Add(new("rigid_junction_trace_kept_seam_empty",
                $"Следы АЖТ стержней {Short(dropped)} целиком у стыка с сохранённой сеткой области «{region.Tag}» — АЖТ не создаётся, " +
                "связь общими узлами.", false, [.. dropped]));
        return new RigidJunctionTraceSet(traces, diagnostics);
    }

    /// <summary>Рёбра контуров области (в её осях), на которых лежат хотя бы два узла сохранённой сетки.</summary>
    static List<(PlanarPoint2D A, PlanarPoint2D B)> SeamEdges(PlanarRegion region, IReadOnlyList<PlanarVector3> keptPoints, double tol)
    {
        var frame = region.Frame;
        var local = new List<PlanarPoint2D>();
        foreach (var p in keptPoints)
        {
            var d = p - frame.Origin;
            if (Math.Abs(d.Dot(frame.LocalZ)) > tol) continue;
            local.Add(new PlanarPoint2D(d.Dot(frame.LocalX), d.Dot(frame.LocalY)));
        }
        var result = new List<(PlanarPoint2D, PlanarPoint2D)>();
        if (local.Count < 2) return result;
        foreach (var contour in region.Contours)
        {
            var pts = Open(contour);
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                int on = 0;
                foreach (var q in local)
                    if (DistanceToSegment(q, a, b) <= tol && ++on >= 2) break;
                if (on >= 2) result.Add((a, b));
            }
        }
        return result;
    }

    static List<PlanarPoint2D> Open(Contour c)
    {
        var pts = Enumerable.Range(0, c.X.Count).Select(i => new PlanarPoint2D(c.X[i], c.Y[i])).ToList();
        if (pts.Count > 1 && Math.Abs(pts[0].U - pts[^1].U) < 1e-12 && Math.Abs(pts[0].V - pts[^1].V) < 1e-12) pts.RemoveAt(pts.Count - 1);
        return pts;
    }

    /// <summary>Кусок подходит к отрезку ближе <paramref name="inset"/>: вершина или ребро куска.</summary>
    static bool Near(IReadOnlyList<PlanarPoint2D> p, PlanarPoint2D a, PlanarPoint2D b, double inset)
    {
        for (int i = 0; i < p.Count; i++)
            if (DistanceToSegment(p[i], a, b) < inset) return true;
        return false;
    }

    static double DistanceToSegment(PlanarPoint2D q, PlanarPoint2D a, PlanarPoint2D b)
    {
        double ux = b.U - a.U, uv = b.V - a.V, l2 = ux * ux + uv * uv;
        double t = l2 < 1e-24 ? 0 : Math.Clamp(((q.U - a.U) * ux + (q.V - a.V) * uv) / l2, 0, 1);
        double dx = q.U - (a.U + t * ux), dy = q.V - (a.V + t * uv);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    static double Cross(PlanarPoint2D a, PlanarPoint2D b, PlanarPoint2D q) => (b.U - a.U) * (q.V - a.V) - (b.V - a.V) * (q.U - a.U);

    static PlanarPoint2D Centroid(IReadOnlyList<PlanarPoint2D> p) => new(p.Average(x => x.U), p.Average(x => x.V));

    static string Short(List<string> tags)
    {
        var list = tags.Distinct(StringComparer.Ordinal).ToList();
        string head = string.Join(", ", list.Take(10).Select(t => $"«{t}»"));
        return list.Count > 10 ? $"{head} и ещё {list.Count - 10}" : head;
    }
}
