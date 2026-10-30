using CScore.Planar;

namespace CScore.Fem.Fragment;

public static partial class StoreyFragmentBuilder
{
    /// <summary>Своя кромка плиты, ставшая разрезом по грани призмы, — роль при наличии разметки у исходной плиты.</summary>
    public const string LateralCutProvenance = "fragment:lateral_cut";

    /// <summary>Плита целиком внутри призмы (или призмы нет): все вершины внешнего контура — по внутреннюю сторону граней.</summary>
    static bool InsidePrism(Ctx c, PlanarRegion region) =>
        Points(region.RequireHull()).Select(p => Global(region.Frame, p)).All(p => c.Lateral.All(face => face.Eval(p) <= c.Tol));

    /// <summary>
    /// Плита, пересекающая грани призмы (спека разд. 2.3, правило (в)): куски клипом следами граней («тег · k» при
    /// нескольких), зоны армирования тем же клипом, ограничения сетки и разметка границ — по правилу (в), боковые разрезы
    /// кромок. Плита вне призмы не входит.
    /// </summary>
    static void ClipSlab(Ctx c, SourceMember s, double z)
    {
        var source = s.Region!;
        var f = source.Frame;
        string tag = s.Member.ElemTag;
        double minArea = c.Tol * c.Tol;
        var pieces = new List<Piece> { new(new PlanarPolygonWithHoles(Points(source.RequireHull()), source.Holes.Select(Points).ToList()), [], 0) };
        if (!ClipLateral(c, f, tag, ref pieces, minArea)) return;
        var cuts = pieces[0].Cuts;
        var lost = new List<string>();
        var constraints = DistributeConstraints(c, source, pieces, cuts, lost);
        var names = PartNames(tag, pieces.Count, twoLevels: false);
        var regions = new List<PlanarRegion>();
        for (int i = 0; i < pieces.Count; i++)
        {
            var region = NewRegion(source, names[i], pieces[i].Polygon.Outer, pieces[i].Polygon.Holes);
            foreach (var zone in source.RebarZones) region.RebarZones.AddRange(ClipZone(zone, cuts, c.Tol, minArea));
            region.ConstraintObjects.AddRange(constraints[i]);
            regions.Add(region);
        }
        DistributeBoundarySegments(c, source, regions, cuts, lost);
        for (int i = 0; i < pieces.Count; i++)
        {
            regions[i].RecalcFingerprint();
            c.Add(s.Member, CopyPart(s.Member, names[i], []), regions[i]);
            c.Slabs.Add(new(names[i], regions[i], z));
            AddLateralEdgeCuts(c, source, names[i], pieces[i].Polygon);
        }
        if (lost.Count > 0)
            c.Warn("fragment_slab_constraints_lost",
                $"Плита {tag}: ограничения сетки и размеченные участки границы, пересекающие грань фрагмента, не переносятся: " +
                $"{Head(lost.Distinct().ToList())}.", tag);
    }

    static bool Inside(IReadOnlyList<PlanarHalfPlane> cuts, PlanarPoint2D p, double tol) => cuts.All(h => Norm(h).Eval(p) >= -tol);

    static PlanarHalfPlane Norm(PlanarHalfPlane h)
    {
        double n = Math.Sqrt(h.Nu * h.Nu + h.Nv * h.Nv);
        return new(h.Nu / n, h.Nv / n, h.C / n);
    }

    /// <summary>Индекс куска, содержащего точку (с допуском); −1 — ни одного.</summary>
    static int PieceOf(List<Piece> pieces, PlanarPoint2D p, double tol)
    {
        for (int i = 0; i < pieces.Count; i++)
        {
            var poly = pieces[i].Polygon;
            if (PlanarPolygonOps.Contains(poly.Outer, p, tol) && !poly.Holes.Any(h => PlanarPolygonOps.Contains(h, p, -tol))) return i;
        }
        return -1;
    }

    /// <summary>
    /// Правило (в) для ограничений сетки: точки внутри — в свой кусок, снаружи — молча; линии — обрезаются, части — в
    /// куски по середине; области целиком внутри — в свой кусок, пересекающие грань — в <paramref name="lost"/>.
    /// </summary>
    static List<PlanarConstraintObject>[] DistributeConstraints(
        Ctx c, PlanarRegion source, List<Piece> pieces, IReadOnlyList<PlanarHalfPlane> cuts, List<string> lost)
    {
        var result = pieces.Select(_ => new List<PlanarConstraintObject>()).ToArray();
        void Put(PlanarPoint2D at, PlanarConstraintObject o)
        {
            int k = PieceOf(pieces, at, c.Tol);
            if (k >= 0) result[k].Add(o);
        }
        foreach (var o in source.ConstraintObjects)
        {
            var pts = o.Geometry.Points;
            if (pts.Count == 0) continue;
            switch (o.Geometry.Kind)
            {
                case PlanarConstraintGeometryKind.Point:
                    if (Inside(cuts, pts[0], c.Tol)) Put(pts[0], o);
                    break;
                case PlanarConstraintGeometryKind.Curve:
                {
                    if (pts.All(p => Inside(cuts, p, c.Tol)))
                    {
                        Put(Middle(pts), o);
                        break;
                    }
                    var parts = ClipPolyline(pts, cuts).Where(p => Length(p) > c.Tol).ToList();
                    for (int k = 0; k < parts.Count; k++)
                        Put(Middle(parts[k]), WithGeometry(o, parts.Count == 1 ? o.Id : $"{o.Id} · {k + 1}",
                            new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve, parts[k])));
                    break;
                }
                default:
                {
                    if (pts.All(p => Inside(cuts, p, c.Tol)))
                    {
                        Put(pts[0], o);
                        break;
                    }
                    var area = new List<PlanarPolygonWithHoles> { new(pts, []) };
                    foreach (var h in cuts) area = area.SelectMany(a => PlanarHalfPlaneClip.Clip(a, h, c.Tol, c.Tol * c.Tol)).ToList();
                    if (area.Count > 0) lost.Add(Label(o));
                    break;
                }
            }
        }
        return result;
    }

    static string Label(PlanarConstraintObject o) => string.IsNullOrWhiteSpace(o.Tag) ? o.Id : o.Tag;

    static PlanarPoint2D Middle(IReadOnlyList<PlanarPoint2D> pts)
    {
        int k = (pts.Count - 1) / 2;
        var (p, q) = pts.Count == 1 ? (pts[0], pts[0]) : (pts[k], pts[k + 1]);
        return new((p.U + q.U) / 2, (p.V + q.V) / 2);
    }

    static double Length(IReadOnlyList<PlanarPoint2D> pts) =>
        Enumerable.Range(0, pts.Count - 1).Sum(i => Math.Sqrt(Math.Pow(pts[i + 1].U - pts[i].U, 2) + Math.Pow(pts[i + 1].V - pts[i].V, 2)));

    /// <summary>Ломаная, обрезанная пересечением полуплоскостей: непрерывные части внутри.</summary>
    static List<List<PlanarPoint2D>> ClipPolyline(IReadOnlyList<PlanarPoint2D> pts, IReadOnlyList<PlanarHalfPlane> cuts)
    {
        var result = new List<List<PlanarPoint2D>>();
        List<PlanarPoint2D>? current = null;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            PlanarPoint2D p = pts[i], q = pts[i + 1];
            double t0 = 0, t1 = 1;
            foreach (var h in cuts)
            {
                double ep = h.Eval(p), eq = h.Eval(q);
                if (ep < 0 && eq < 0) { t1 = -1; break; }
                if (ep < 0) t0 = Math.Max(t0, ep / (ep - eq));
                else if (eq < 0) t1 = Math.Min(t1, ep / (ep - eq));
            }
            if (t1 - t0 <= 1e-12)
            {
                current = null;
                continue;
            }
            PlanarPoint2D At(double t) => new(p.U + (q.U - p.U) * t, p.V + (q.V - p.V) * t);
            if (current is null || t0 > 1e-12)
            {
                current = [At(t0)];
                result.Add(current);
            }
            current.Add(At(t1));
            if (t1 < 1 - 1e-12) current = null;
        }
        return result;
    }

    /// <summary>Копия ограничения с новыми Id и геометрией (остальное — как у исходного).</summary>
    static PlanarConstraintObject WithGeometry(PlanarConstraintObject o, string id, PlanarConstraintGeometry geometry) => new()
    {
        Id = id,
        Tag = o.Tag,
        Geometry = geometry,
        StructuralFacet = o.StructuralFacet,
        MeshFacet = o.MeshFacet,
        MasterReference = o.MasterReference,
        DofMask = o.DofMask,
        StructuralFrame = o.StructuralFrame,
        ToleranceM = o.ToleranceM,
        Provenance = o.Provenance,
        IsDerived = o.IsDerived,
        KeepVertices = o.KeepVertices,
        SourceReferences = [.. o.SourceReferences],
        StructuralRelations = [.. o.StructuralRelations],
    };

    /// <summary>
    /// Правило (в) для разметки границ: ребро целиком внутри — номера вершин по совпадению в куске; ребро, пересекающее
    /// грань (или не найденное), — в <paramref name="lost"/>; снаружи — молча. Новые кромки на гранях — роль
    /// <see cref="BoundaryRole.Free"/>, если разметка у плиты была.
    /// </summary>
    static void DistributeBoundarySegments(
        Ctx c, PlanarRegion source, List<PlanarRegion> regions, IReadOnlyList<PlanarHalfPlane> cuts, List<string> lost)
    {
        if (source.BoundarySegments.Count == 0) return;
        var outer = Points(source.RequireHull());
        var holes = source.Holes.Select(Points).ToList();
        foreach (var seg in source.BoundarySegments)
        {
            var loop = seg.Loop == BoundaryLoop.Outer ? outer : seg.HoleIndex < holes.Count ? holes[seg.HoleIndex] : null;
            if (loop is null || seg.StartVertex >= loop.Count || seg.EndVertex >= loop.Count) continue;
            PlanarPoint2D p = loop[seg.StartVertex], q = loop[seg.EndVertex];
            string label = $"{seg.Role} {seg.Loop} {seg.StartVertex}–{seg.EndVertex}";
            if (Inside(cuts, p, c.Tol) && Inside(cuts, q, c.Tol))
            {
                if (!regions.Any(r => AddMatching(r, seg, p, q, c.Tol))) lost.Add(label);
            }
            else if (ClipPolyline([p, q], cuts).Any(part => Length(part) > c.Tol)) lost.Add(label);
        }
        foreach (var region in regions)
        {
            var f = region.Frame;
            var loops = region.Holes.Select(Points).Prepend(Points(region.RequireHull())).ToList();
            for (int l = 0; l < loops.Count; l++)
                for (int i = 0; i < loops[l].Count; i++)
                {
                    int j = (i + 1) % loops[l].Count;
                    PlanarVector3 a = Global(f, loops[l][i]), b = Global(f, loops[l][j]);
                    if (!c.Lateral.Any(face => Math.Abs(face.Eval(a)) <= c.Tol && Math.Abs(face.Eval(b)) <= c.Tol
                                               && Continues(c, source, (a + b) * 0.5, face.Normal))) continue;
                    region.BoundarySegments.Add(new BoundarySegment
                    {
                        Loop = l == 0 ? BoundaryLoop.Outer : BoundaryLoop.Hole, HoleIndex = Math.Max(0, l - 1),
                        StartVertex = i, EndVertex = j, Role = BoundaryRole.Free, Provenance = LateralCutProvenance,
                    });
                }
        }
    }

    /// <summary>Ребро p–q среди рёбер контуров области: найдено — участок с теми же ролью и происхождением добавлен.</summary>
    static bool AddMatching(PlanarRegion region, BoundarySegment seg, PlanarPoint2D p, PlanarPoint2D q, double tol)
    {
        static bool Same(PlanarPoint2D a, PlanarPoint2D b, double tol) => Math.Abs(a.U - b.U) <= tol && Math.Abs(a.V - b.V) <= tol;
        var loops = region.Holes.Select(Points).Prepend(Points(region.RequireHull())).ToList();
        for (int l = 0; l < loops.Count; l++)
            for (int i = 0; i < loops[l].Count; i++)
            {
                int j = (i + 1) % loops[l].Count;
                var (a, b) = (loops[l][i], loops[l][j]);
                int start, end;
                if (Same(a, p, tol) && Same(b, q, tol)) (start, end) = (i, j);
                else if (Same(a, q, tol) && Same(b, p, tol)) (start, end) = (j, i);
                else continue;
                region.BoundarySegments.Add(new BoundarySegment
                {
                    Loop = l == 0 ? BoundaryLoop.Outer : BoundaryLoop.Hole, HoleIndex = Math.Max(0, l - 1),
                    StartVertex = start, EndVertex = end, Role = seg.Role, Provenance = seg.Provenance,
                });
                return true;
            }
        return false;
    }
}
