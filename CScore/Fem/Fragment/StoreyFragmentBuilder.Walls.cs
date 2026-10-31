using CScore.Planar;
using CScore.PlateRebar;

namespace CScore.Fem.Fragment;

public static partial class StoreyFragmentBuilder
{
    /// <summary>Кусок стены: многоугольник в осях стены, применённые к нему полуплоскости, уровень между плитами.</summary>
    sealed record Piece(PlanarPolygonWithHoles Polygon, IReadOnlyList<PlanarHalfPlane> Cuts, int Level);

    /// <summary>
    /// Стена (невертикальная — с предупреждением): клип полосой z₁…z₂, деление по отметкам примыкающих плит, клип зон
    /// армирования, рассечения нижней/верхней кромки.
    /// </summary>
    static void CutWall(Ctx c, SourceMember s)
    {
        var region = s.Region!;
        var f = region.Frame;
        string tag = s.Member.ElemTag;
        double minArea = c.Tol * c.Tol;
        var polygon = new PlanarPolygonWithHoles(Points(region.RequireHull()), region.Holes.Select(Points).ToList());

        var pieces = ClipAll([new Piece(polygon, [], 0)], Above(f, c.Z1), c.Tol, minArea);
        pieces = ClipAll(pieces, Below(f, c.Z2), c.Tol, minArea);
        if (pieces.Count == 0) return;
        if (!ClipLateral(c, f, tag, ref pieces, minArea)) return;
        if (Math.Abs(f.LocalZ.Z) > 1e-4)
            c.Warn("fragment_wall_inclined", $"Стена {tag} не вертикальна — рассечена плоскостями z₁/z₂ как есть.", tag);

        foreach (var slab in c.Slabs.OrderBy(sl => sl.Z))
        {
            var next = new List<Piece>();
            foreach (var piece in pieces)
            {
                var lower = ClipAll([piece], Below(f, slab.Z), c.Tol, minArea);
                var upper = ClipAll([piece], Above(f, slab.Z), c.Tol, minArea);
                if (lower.Count == 0 || upper.Count == 0 || !Touches(c, f, lower, slab)) { next.Add(piece); continue; }
                next.AddRange(lower);
                next.AddRange(upper.Select(p => p with { Level = piece.Level + 1 }));
            }
            pieces = next;
        }

        var ordered = pieces.OrderBy(p => p.Level).ThenBy(p => p.Polygon.Outer.Min(q => q.U)).ToList();
        var names = PartNames(tag, ordered.Count, twoLevels: ordered.Count == 2 && ordered[0].Level != ordered[1].Level);
        if (region.ConstraintObjects.Count > 0 || region.BoundarySegments.Count > 0)
            c.Warn("fragment_wall_constraints_lost", $"Ручные ограничения сетки стены {tag} не переносятся в рассечённые куски.", tag);
        for (int i = 0; i < ordered.Count; i++)
        {
            var piece = ordered[i];
            var part = NewRegion(region, names[i], piece.Polygon.Outer, piece.Polygon.Holes);
            foreach (var zone in region.RebarZones) part.RebarZones.AddRange(ClipZone(zone, piece.Cuts, c.Tol, minArea));
            part.RecalcFingerprint();
            c.Add(s.Member, CopyPart(s.Member, names[i], []), part);
            AddEdgeCuts(c, f, names[i], piece.Polygon);
            AddLateralEdgeCuts(c, region, names[i], piece.Polygon);
        }
    }

    /// <summary>
    /// Клип кусков следами боковых граней призмы на плоскости области. Область, параллельная грани: снаружи — не входит,
    /// на грани (в пределах остальных граней) — отказ «измените d». false — область не входит или отказ.
    /// </summary>
    static bool ClipLateral(Ctx c, Frame3D f, string tag, ref List<Piece> pieces, double minArea)
    {
        var parallel = new List<FragmentBoundPlane>();
        foreach (var face in c.Lateral)
        {
            if (Trace(f, face) is not { } h)
            {
                parallel.Add(face);
                continue;
            }
            pieces = ClipAll(pieces, h, c.Tol, minArea);
            if (pieces.Count == 0) return false;
        }
        foreach (var face in parallel)
        {
            double e = face.Eval(f.Origin);
            if (e > c.Tol) return false;
            if (e < -c.Tol) continue;
            c.Error("fragment_member_on_prism_face",
                $"Пластина {tag} лежит в плоскости боковой грани фрагмента — измените отступ d.", tag);
            return false;
        }
        return true;
    }

    /// <summary>След грани на плоскости области: полуплоскость внутренней стороны; null — область параллельна грани.</summary>
    static PlanarHalfPlane? Trace(Frame3D f, FragmentBoundPlane face)
    {
        double nu = face.Normal.Dot(f.LocalX), nv = face.Normal.Dot(f.LocalY);
        if (Math.Sqrt(nu * nu + nv * nv) < 1e-9) return null;
        // n·(O + u·X + v·Y) ≤ смещения ⇔ −(n·X)·u − (n·Y)·v ≥ n·O − смещение.
        return new PlanarHalfPlane(-nu, -nv, face.Normal.Dot(f.Origin) - face.Offset);
    }

    /// <summary>
    /// Боковые рассечения куска: его рёбра на грани призмы, за которой исходная область продолжается (своя кромка
    /// элемента, совпавшая с гранью, — не разрез); Z — средняя отметка середин отрезков, нормаль — грани.
    /// </summary>
    static void AddLateralEdgeCuts(Ctx c, PlanarRegion source, string memberTag, PlanarPolygonWithHoles polygon)
    {
        var f = source.Frame;
        foreach (var face in c.Lateral)
        {
            var segments = new List<(PlanarVector3 P, PlanarVector3 Q)>();
            foreach (var contour in polygon.Holes.Prepend(polygon.Outer))
                for (int i = 0; i < contour.Count; i++)
                {
                    PlanarVector3 p = Global(f, contour[i]), q = Global(f, contour[(i + 1) % contour.Count]);
                    if (Math.Abs(face.Eval(p)) <= c.Tol && Math.Abs(face.Eval(q)) <= c.Tol
                        && (q - p).Length > c.Tol && Continues(c, source, (p + q) * 0.5, face.Normal))
                        segments.Add((p, q));
                }
            if (segments.Count > 0)
                c.Draft.Cuts.Add(new(memberTag, FragmentCutKind.Edge, FragmentCutSide.Lateral,
                    segments.Average(sg => (sg.P.Z + sg.Q.Z) / 2), null, segments, face.Normal));
        }
    }

    /// <summary>Исходная область продолжается за грань: точка, сдвинутая от <paramref name="at"/> наружу в её плоскости, — в ней.</summary>
    static bool Continues(Ctx c, PlanarRegion source, PlanarVector3 at, PlanarVector3 normal)
    {
        var z = source.Frame.LocalZ;
        var step = normal - z * normal.Dot(z);
        double len = step.Length;
        if (len < 1e-9) return false;
        return RegionContains(source, at + step * (Math.Max(10 * c.Tol, 0.01) / len), c.Tol);
    }

    // z(u, v) = O.z + u·X.z + v·Y.z
    static double ZOf(Frame3D f, PlanarPoint2D p) => f.Origin.Z + p.U * f.LocalX.Z + p.V * f.LocalY.Z;

    static PlanarHalfPlane Above(Frame3D f, double z) => new(f.LocalX.Z, f.LocalY.Z, z - f.Origin.Z);

    static PlanarHalfPlane Below(Frame3D f, double z) => new(-f.LocalX.Z, -f.LocalY.Z, f.Origin.Z - z);

    static PlanarVector3 Global(Frame3D f, PlanarPoint2D p) =>
        PlanarBoundaryFrameConverter.ToGlobalPoint(f, new PlanarVector3(p.U, p.V, 0));

    static List<Piece> ClipAll(IEnumerable<Piece> pieces, PlanarHalfPlane h, double tol, double minArea) =>
        pieces.SelectMany(p => PlanarHalfPlaneClip.Clip(p.Polygon, h, tol, minArea)
            .Select(poly => new Piece(poly, [.. p.Cuts, h], p.Level))).ToList();

    /// <summary>Рёбра контуров куска, лежащие на горизонтали <paramref name="z"/>.</summary>
    static IEnumerable<(PlanarPoint2D P, PlanarPoint2D Q)> EdgesAt(Frame3D f, PlanarPolygonWithHoles polygon, double z, double tol)
    {
        foreach (var contour in polygon.Holes.Prepend(polygon.Outer))
            for (int i = 0; i < contour.Count; i++)
            {
                var p = contour[i];
                var q = contour[(i + 1) % contour.Count];
                if (Math.Abs(ZOf(f, p) - z) <= tol && Math.Abs(ZOf(f, q) - z) <= tol) yield return (p, q);
            }
    }

    /// <summary>Плита примыкает к стене: линия стыка (верх нижних кусков на отметке плиты) хотя бы частью в контуре плиты.</summary>
    static bool Touches(Ctx c, Frame3D f, List<Piece> lower, Slab slab)
    {
        double[] samples = [0.1, 0.3, 0.5, 0.7, 0.9];
        return lower.SelectMany(p => EdgesAt(f, p.Polygon, slab.Z, c.Tol)).Any(e => samples.Any(t =>
            RegionContains(slab.Region, Global(f, new PlanarPoint2D(e.P.U + (e.Q.U - e.P.U) * t, e.P.V + (e.Q.V - e.P.V) * t)), c.Tol)));
    }

    /// <summary>Рассечения нижней/верхней кромки куска: его рёбра на z₁/z₂ в общих осях.</summary>
    static void AddEdgeCuts(Ctx c, Frame3D f, string memberTag, PlanarPolygonWithHoles polygon)
    {
        foreach (var (z, side) in new[] { (c.Z1, FragmentCutSide.Bottom), (c.Z2, FragmentCutSide.Top) })
        {
            var segments = EdgesAt(f, polygon, z, c.Tol).Select(e => (Global(f, e.P), Global(f, e.Q))).ToList();
            if (segments.Count > 0) c.Draft.Cuts.Add(new(memberTag, FragmentCutKind.Edge, side, z, null, segments));
        }
    }

    /// <summary>Зона армирования, обрезанная теми же полуплоскостями, что и кусок; несвязная — «Имя · k».</summary>
    static IEnumerable<RebarZone> ClipZone(RebarZone zone, IReadOnlyList<PlanarHalfPlane> cuts, double tol, double minArea)
    {
        if (zone.Polygon.Count < 3) yield break;
        var polys = new List<PlanarPolygonWithHoles> { new(zone.Polygon.Select(p => new PlanarPoint2D(p.U, p.V)).ToList(), []) };
        foreach (var h in cuts) polys = polys.SelectMany(p => PlanarHalfPlaneClip.Clip(p, h, tol, minArea)).ToList();
        for (int k = 0; k < polys.Count; k++)
        {
            var copy = zone.Clone();
            copy.Name = polys.Count == 1 ? zone.Name : $"{zone.Name} · {k + 1}";
            copy.Polygon = polys[k].Outer.Select(q => new RebarZonePoint { U = q.U, V = q.V }).ToList();
            yield return copy;
        }
    }
}
