using System.Text.Json;
using CScore.Planar;

namespace CScore.Fem;

/// <summary>Случай примыкания стержня к плите.</summary>
public enum RigidJunctionCase
{
    /// <summary>Стержень входит в плиту торцом или проходит насквозь (колонна, свая, подкос).</summary>
    Column,
    /// <summary>Стержень лежит в плоскости плиты (ребро).</summary>
    Rib
}

/// <summary>Параметры построения следов АЖТ.</summary>
/// <param name="AngleDeg">Угол оси стержня к плоскости плиты, начиная с которого стержень «входит» в плиту (случай А); меньше — лежит в ней (случай Б).</param>
/// <param name="PlaneToleranceM">Допуск попадания узла в плоскость и в контур плиты, м.</param>
/// <param name="MinPieceAreaM2">Наименьшая площадь куска следа, м²; null — (0,01·шаг сетки области)².</param>
public sealed record RigidJunctionOptions(double AngleDeg = 10, double PlaneToleranceM = 1e-3, double? MinPieceAreaM2 = null);

/// <summary>
/// След одного АЖТ в осях плоской области. У колонны ведущий — узел стержня в плите (<see cref="MasterNodeTag"/>) или,
/// у стержня насквозь без узла в плите, точка пересечения (<see cref="MasterPoint"/>); у ребра ведущие — узлы сетки
/// стержня по станциям (определяются при сборке сетки), здесь оба null.
/// </summary>
/// <param name="Pieces">Куски следа после вычитания отверстий и обрезки контуром, против часовой стрелки; тело одно.</param>
/// <param name="Coupled">Объединение перемещений вместо жёсткого тела.</param>
public sealed record RigidJunctionTrace(
    string Id,
    RigidJunctionCase Case,
    string? MasterNodeTag,
    PlanarVector3? MasterPoint,
    IReadOnlyList<string> MemberTags,
    IReadOnlyList<IReadOnlyList<PlanarPoint2D>> Pieces,
    PlanarDofMask Mask,
    bool Coupled);

/// <summary>Следы АЖТ области и диагностика построения.</summary>
public sealed record RigidJunctionTraceSet(IReadOnlyList<RigidJunctionTrace> Traces, IReadOnlyList<FemValidationDiagnostic> Diagnostics)
{
    /// <summary>Происхождение автоматических АЖТ примыканий.</summary>
    public const string Provenance = "auto:junction";

    /// <summary>Ограничения сетки области: по Region-ограничению на кусок следа (<c>Id#k</c>), вшиваются в сетку.</summary>
    public IReadOnlyList<PlanarConstraintObject> ToConstraints()
    {
        var result = new List<PlanarConstraintObject>();
        foreach (var trace in Traces)
        {
            var master = trace.MasterNodeTag != null
                ? new PlanarMasterReference("fem-node", trace.MasterNodeTag)
                : new PlanarMasterReference("fem-member", trace.MemberTags[0]);
            for (int k = 0; k < trace.Pieces.Count; k++)
            {
                var constraint = PlanarConstraintObject.Region(
                    $"{trace.Id}#{k}",
                    trace.Pieces[k],
                    new PlanarStructuralFacet(PlanarStructuralKind.RigidBody, master, trace.Mask),
                    new PlanarMeshFacet(PlanarMeshKind.EmbeddedRegion));
                constraint.MasterReference = master;
                constraint.DofMask = trace.Mask;
                constraint.IsDerived = true;
                constraint.Provenance = Provenance;
                result.Add(constraint);
            }
        }
        return result;
    }
}

/// <summary>
/// Следы АЖТ в местах примыкания стержневых КонЭ к плоской области (спека
/// <c>2026-10-15-fem-auto-rigid-junctions-design.md</c>, разд. 4): колонна — проекция контура сечения вдоль оси на
/// плоскость (стержни одного узла — одно тело), ребро — полоса шириной сечения вдоль стержня. Явный след КонЭ
/// (<see cref="FemMember.RigidJunctionTraceJson"/>) важнее сечения. Отверстия вычитаются, внешний контур обрезает.
/// Чистая геометрия, без БД.
/// </summary>
public static class FemRigidJunctions
{
    const PlanarDofMask AllDofs = (PlanarDofMask)63;

    sealed class Body(string id, RigidJunctionCase kind, string? masterNodeTag, PlanarVector3? masterPoint)
    {
        public string Id { get; } = id;
        public RigidJunctionCase Case { get; } = kind;
        public string? MasterNodeTag { get; } = masterNodeTag;
        public PlanarVector3? MasterPoint { get; } = masterPoint;
        public List<string> MemberTags { get; } = [];
        public List<PlanarPoint2D> Points { get; } = [];
        public PlanarDofMask Mask { get; set; }
        public bool AllCoupled { get; set; } = true;
    }

    /// <param name="sectionOutline">Контур сечения по <see cref="FemMember.CrossSectionId"/> в осях стержня (Y, Z), м, от ц. т.
    /// (<see cref="RigidJunctionSectionOutline.From"/>); null — сечения нет.</param>
    public static RigidJunctionTraceSet Build(
        PlanarRegion region,
        IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemMember> members,
        Func<int, IReadOnlyList<(double Y, double Z)>?> sectionOutline,
        RigidJunctionOptions? options = null)
    {
        options ??= new RigidJunctionOptions();
        var diagnostics = new List<FemValidationDiagnostic>();
        var frame = region.Frame;
        var normal = frame.LocalZ;
        var outer = Open(region.RequireHull());
        var holes = region.Holes.Select(Open).ToList();
        double tol = options.PlaneToleranceM;
        double sinA = Math.Sin(options.AngleDeg * Math.PI / 180);
        double minArea = options.MinPieceAreaM2 ?? Math.Pow(0.01 * region.MeshMaxElementSizeM, 2);
        var nodesByTag = nodes.GroupBy(n => n.NodeTag, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        (double U, double V, double W) Local(PlanarVector3 p)
        {
            var d = p - frame.Origin;
            return (d.Dot(frame.LocalX), d.Dot(frame.LocalY), d.Dot(normal));
        }

        bool OnSlab((double U, double V, double W) p)
        {
            if (Math.Abs(p.W) > tol) return false;
            var q = new PlanarPoint2D(p.U, p.V);
            return PlanarPolygonOps.Contains(outer, q, tol) && !holes.Any(h => PlanarPolygonOps.Contains(h, q, -tol));
        }

        void Warn(string code, string message, string tag) =>
            diagnostics.Add(new FemValidationDiagnostic(code, message, IsError: false, SourceKeys: [tag]));

        var columns = new Dictionary<string, Body>(StringComparer.Ordinal);
        var bodies = new List<Body>();

        foreach (var member in members
                     .Where(m => string.Equals(m.ElemType, "beam", StringComparison.OrdinalIgnoreCase) &&
                                 m.RigidJunctionMode != FemMember.RigidJunctionOff)
                     .OrderBy(m => m.ElemTag, StringComparer.Ordinal))
        {
            var tags = JsonSerializer.Deserialize<int[]>(member.NodeIdsJson) ?? [];
            if (tags.Length != 2) continue;
            if (!nodesByTag.TryGetValue(tags[0].ToString(), out var na) ||
                !nodesByTag.TryGetValue(tags[1].ToString(), out var nb))
                continue;

            var a = new PlanarVector3(na.X, na.Y, na.Z);
            var b = new PlanarVector3(nb.X, nb.Y, nb.Z);
            if ((b - a).Length < 1e-12) continue;
            var la = Local(a);
            var lb = Local(b);
            bool inA = OnSlab(la), inB = OnSlab(lb);
            var axis = (b - a).Normalize();
            double cosN = Math.Abs(axis.Dot(normal));
            var (_, eY, eZ) = BeamLocalAxisConvention.Frame(a, b, member.RotationDeg);
            var mask = member.RigidJunctionMask is { } m ? (PlanarDofMask)m & AllDofs : AllDofs;
            bool coupled = member.RigidJunctionKind == FemMember.RigidJunctionCoupled;

            if (inA && inB && cosN < sinA)
            {
                // Стержень в плоскости плиты без сечения: связь — общими узлами; условная полоса 10 см вшивалась бы в
                // сетку лентой мелких КЭ вдоль всего стержня (фрагмент «Музея» — 31 стержень, 433 лишних тела).
                if ((member.CrossSectionId is { } sid ? sectionOutline(sid) : null) is not { Count: >= 3 } outline)
                {
                    Warn("rigid_junction_rib_section_missing",
                        $"Стержень {member.ElemTag} в плоскости плиты без сечения с контуром — полоса АЖТ не строится, связь общими узлами.",
                        member.ElemTag);
                    continue;
                }
                if (member.RigidJunctionTraceJson != null)
                    Warn("rigid_junction_rib_trace_ignored",
                        $"Явный след стержня {member.ElemTag} в плоскости плиты не используется — полоса строится по сечению.", member.ElemTag);
                var w = normal.Cross(axis).Normalize();
                double sMin = double.MaxValue, sMax = double.MinValue;
                foreach (var (y, z) in outline)
                {
                    double s = (eY * y + eZ * z).Dot(w);
                    sMin = Math.Min(sMin, s); sMax = Math.Max(sMax, s);
                }
                var rib = new Body($"rigid-rib:{member.ElemTag}", RigidJunctionCase.Rib, null, null)
                {
                    Mask = mask, AllCoupled = coupled
                };
                rib.MemberTags.Add(member.ElemTag);
                foreach (var p in new[] { a + w * sMin, b + w * sMin, b + w * sMax, a + w * sMax })
                {
                    var l = Local(p);
                    rib.Points.Add(new PlanarPoint2D(l.U, l.V));
                }
                bodies.Add(rib);
                continue;
            }

            // Ведущая точка колонны: узел в плите или точка пересечения плоскости стержнем насквозь.
            string key;
            string? masterTag = null;
            PlanarVector3 o;
            if (inA || inB)
            {
                var host = inA ? na : nb;
                o = inA ? a : b;
                masterTag = host.NodeTag;
                key = $"rigid-junction:{host.NodeTag}";
            }
            else if (la.W * lb.W < 0)
            {
                o = a + (b - a) * (la.W / (la.W - lb.W));
                if (!OnSlab(Local(o))) continue;
                key = $"rigid-junction:{member.ElemTag}@cross";
            }
            else continue;

            if (cosN < sinA)
            {
                Warn("rigid_junction_shallow_angle",
                    $"Стержень {member.ElemTag} примыкает к плите под углом меньше {options.AngleDeg:0.#}° и не лежит в ней — АЖТ не создаётся.",
                    member.ElemTag);
                continue;
            }

            var points = ExplicitTrace(member, diagnostics);
            if (points == null)
            {
                var outline = Outline(member, sectionOutline, diagnostics);
                double denom = axis.Dot(normal);
                points = outline.Select(yz =>
                {
                    var p = o + eY * yz.Y + eZ * yz.Z;
                    return p - axis * ((p - o).Dot(normal) / denom);
                }).ToList();
            }

            if (!columns.TryGetValue(key, out var body))
            {
                body = new Body(key, RigidJunctionCase.Column, masterTag, masterTag == null ? o : null);
                columns[key] = body;
                bodies.Add(body);
            }
            body.MemberTags.Add(member.ElemTag);
            body.Mask |= mask;
            body.AllCoupled &= coupled;
            foreach (var p in points)
            {
                var l = Local(p);
                body.Points.Add(new PlanarPoint2D(l.U, l.V));
            }
        }

        var traces = new List<RigidJunctionTrace>();
        // Следы не должны перекрываться (ограничения области): ригель, входящий в колонну в плоскости плиты, и рёбра,
        // сходящиеся в узле, — позже построенный вычитает занятые. Колонны — первыми: их след — опора АЖТ.
        var occupied = new List<IReadOnlyList<PlanarPoint2D>>();
        foreach (var body in bodies.OrderBy(b => b.Case == RigidJunctionCase.Rib ? 1 : 0))
        {
            var hull = PlanarPolygonOps.ConvexHull(body.Points);
            var pieces = new List<IReadOnlyList<PlanarPoint2D>>();
            if (hull.Count >= 3 && PlanarPolygonOps.SignedArea(hull) >= minArea)
            {
                List<IReadOnlyList<PlanarPoint2D>> convex = [hull];
                foreach (var hole in holes.Concat(occupied).ToList())
                    convex = convex.SelectMany(piece => PlanarPolygonOps.SubtractFromConvex(piece, hole, minArea)).ToList();
                occupied.AddRange(convex);
                foreach (var piece in convex)
                {
                    var clipped = PlanarPolygonOps.ClipByConvex(outer, piece);
                    if (clipped.Count >= 3 && Math.Abs(PlanarPolygonOps.SignedArea(clipped)) >= minArea)
                        pieces.Add(PlanarPolygonOps.Ccw(clipped));
                }
            }

            string tag = body.MemberTags[0];
            if (pieces.Count == 0)
            {
                Warn("rigid_junction_trace_empty",
                    $"След АЖТ стержня {string.Join(", ", body.MemberTags)} пуст (мал, в отверстии или вне плиты) — АЖТ не создаётся.", tag);
                continue;
            }

            var masterPoint = body.MasterNodeTag != null ? Position(nodesByTag[body.MasterNodeTag]) : body.MasterPoint;
            if (body.Case == RigidJunctionCase.Column && masterPoint is { } mp)
            {
                var l = Local(mp);
                var q = new PlanarPoint2D(l.U, l.V);
                if (!pieces.Any(piece => PlanarPolygonOps.Contains(piece, q, tol)))
                    Warn("rigid_junction_master_outside",
                        $"Ведущий узел АЖТ стержня {string.Join(", ", body.MemberTags)} вне следа (в отверстии плиты) — тело создаётся с жёстким плечом.", tag);
            }

            traces.Add(new RigidJunctionTrace(body.Id, body.Case, body.MasterNodeTag, body.MasterPoint,
                body.MemberTags.ToArray(), pieces, body.Mask, body.AllCoupled));
        }

        return new RigidJunctionTraceSet(traces, diagnostics);
    }

    static PlanarVector3 Position(FemNode node) => new(node.X, node.Y, node.Z);

    /// <summary>Сторона условного квадрата следа стержня без сечения с контуром, м.</summary>
    public const double AssumedOutlineSideM = 0.1;

    /// <summary>
    /// Контур сечения стержня, примыкающего к плите, в его осях; без сечения с контуром — условный квадрат 10×10 см
    /// (решение пользователя 30.10: у импортных стержней сечения часто нет, а АЖТ примыкания нужен). Квадрат — только для
    /// следа: стержню сечение не назначается, жёсткость в расчёте остаётся прежней (импортной).
    /// </summary>
    static IReadOnlyList<(double Y, double Z)> Outline(
        FemMember member, Func<int, IReadOnlyList<(double Y, double Z)>?> sectionOutline, List<FemValidationDiagnostic> diagnostics)
    {
        var outline = member.CrossSectionId is { } id ? sectionOutline(id) : null;
        if (outline is { Count: >= 3 }) return outline;
        diagnostics.Add(new FemValidationDiagnostic("rigid_junction_section_assumed",
            $"У стержня {member.ElemTag} нет сечения с контуром — след АЖТ по условному квадрату 10×10 см.", IsError: false,
            SourceKeys: [member.ElemTag]));
        const double h = AssumedOutlineSideM / 2;
        return [(-h, -h), (h, -h), (h, h), (-h, h)];
    }

    /// <summary>Точки явного следа в общих осях; null — следа нет или он некорректен (тогда — по сечению).</summary>
    static List<PlanarVector3>? ExplicitTrace(FemMember member, List<FemValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(member.RigidJunctionTraceJson)) return null;
        try
        {
            var raw = JsonSerializer.Deserialize<double[][]>(member.RigidJunctionTraceJson);
            if (raw is { Length: >= 3 } && raw.All(p => p is { Length: 3 } && p.All(double.IsFinite)))
                return raw.Select(p => new PlanarVector3(p[0], p[1], p[2])).ToList();
        }
        catch (JsonException) { }
        diagnostics.Add(new FemValidationDiagnostic("rigid_junction_trace_invalid",
            $"Явный след АЖТ стержня {member.ElemTag} некорректен — используется сечение.", IsError: false, SourceKeys: [member.ElemTag]));
        return null;
    }

    static IReadOnlyList<PlanarPoint2D> Open(Contour contour)
    {
        var (x, y) = PlanarRegionTopologyValidator.ToOpenLoop(contour.X, contour.Y);
        return PlanarPolygonOps.Ccw(Enumerable.Range(0, x.Length).Select(i => new PlanarPoint2D(x[i], y[i])).ToArray());
    }
}
