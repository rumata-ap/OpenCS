using System.Text.Json.Nodes;
using CScore.Fem.Loads;
using CScore.Planar;
using CScore.PlateRebar;

namespace CScore.Fem.Editing;

/// <summary>Состав схемы для разворота КонЭ. Списки КонЭ, нагрузок и линейных связей правятся на месте (это состав
/// сессии схемы); области, снимки и КЭ сетки не меняются — их новые копии возвращает <see cref="FemOrientationResult"/>.</summary>
public sealed class FemOrientationInput
{
    public required IReadOnlyList<FemNode> Nodes { get; init; }
    public required IReadOnlyList<FemMember> Members { get; init; }
    public required List<FemMemberLoad> MemberLoads { get; init; }
    public required List<FemElementLoad> ElementLoads { get; init; }
    public required List<FemLineSupport> LineSupports { get; init; }
    public IReadOnlyList<FemMemberGroup> Groups { get; init; } = [];
    /// <summary>Области пластин по Id.</summary>
    public IReadOnlyDictionary<int, PlanarRegion> Regions { get; init; } = new Dictionary<int, PlanarRegion>();
    /// <summary>Последние снимки сетки областей по Id области (только те, что надо отразить вместе с областью).</summary>
    public IReadOnlyDictionary<int, PlanarMeshSnapshot> Snapshots { get; init; } = new Dictionary<int, PlanarMeshSnapshot>();
    public IReadOnlyList<FemMeshNode> MeshNodes { get; init; } = [];
    public IReadOnlyList<FemElement> MeshElements { get; init; } = [];
}

/// <summary>Почему нагрузка на КЭ не развёрнута вместе с КонЭ.</summary>
public enum FemOrientationLoadSkip
{
    /// <summary>Цель шире разворачиваемых КонЭ (КонЭ или группа, где есть и неразвёрнутые).</summary>
    PartialTarget,
    /// <summary>Значения на разных КЭ цели после разворота разные, а цель — КонЭ или группа (одна запись — одни значения).</summary>
    PerElementValues,
    /// <summary>Местное направление x/y пластины не ложится на ось нового КЭ.</summary>
    InPlaneAxis,
}

/// <summary>Итог разворота: что развёрнуто и новые копии областей, снимков и КЭ.</summary>
public sealed class FemOrientationResult
{
    /// <summary>Стержни с развёрнутой осью.</summary>
    public List<string> Bars { get; } = [];
    /// <summary>Пластины со сменённой нормалью.</summary>
    public List<string> Shells { get; } = [];
    /// <summary>Отражённые области (те же Id).</summary>
    public List<PlanarRegion> Regions { get; } = [];
    /// <summary>Отражённые снимки сетки по Id области: без отпечатка входа — его ставит сборка сетки.</summary>
    public Dictionary<int, PlanarMeshSnapshot> Snapshots { get; } = [];
    /// <summary>Развёрнутые копии КЭ сетки КонЭ (те же Id и теги).</summary>
    public List<FemElement> Elements { get; } = [];
    public int MemberLoads { get; set; }
    public int ElementLoads { get; set; }
    public int LineSupports { get; set; }
    /// <summary>Нагрузки на КЭ, оставленные как были.</summary>
    public List<(FemElementLoad Load, FemOrientationLoadSkip Reason)> SkippedLoads { get; } = [];
    /// <summary>Линейные связи, не нашедшие свою грань после разворота (не должно случаться).</summary>
    public List<FemLineSupport> LostLineSupports { get; } = [];
}

/// <summary>
/// «Развернуть ось» стержня (начало ↔ конец) и «Сменить нормаль» пластины (верх ↔ низ) без изменения физики: геометрия,
/// опоры и нагрузки остаются на месте и в ту же сторону в пространстве; меняется то, что по определению привязано к
/// направлению. Повтор возвращает исходное.
/// <list type="bullet">
/// <item>Стержень: узлы I ↔ J, поворот сечения β → −β (местная Y сохраняется, X и Z — в обратную сторону); нагрузки:
/// отступы и значения начала и конца меняются местами, в местных осях Qx, Qz, Mx, Mz — со знаком минус; КЭ — порядок
/// узлов обратный, шарниры концов меняются местами.</item>
/// <item>Пластина: оси области (O, X, −Y, −Z), у стены — (O, −X, Y, −Z) (вертикальная Y остаётся вверх); контуры и зоны
/// армирования — в новых осях с прежним направлением обхода и прежней первой вершиной; КЭ — обратный обход с тем же
/// первым узлом, ось выдачи усилий — та же в пространстве; нагрузки на КЭ — в новом порядке узлов и осях.</item>
/// </list>
/// </summary>
public static class FemOrientationFlip
{
    const double AxisTolerance = 1e-6;

    /// <summary>Стержневой КонЭ (балка или ферма).</summary>
    public static bool IsBar(FemMember m) => m.ElemType is "beam" or "truss";

    /// <summary>Пластинчатый КонЭ.</summary>
    public static bool IsShell(FemMember m) => m.ElemType == "shell";

    /// <summary>Разворачивает КонЭ <paramref name="memberTags"/> (стержни — ось, пластины — нормаль).</summary>
    public static FemOrientationResult Apply(FemOrientationInput input, IEnumerable<string> memberTags)
    {
        var tags = memberTags.ToHashSet(StringComparer.Ordinal);
        var result = new FemOrientationResult();
        var members = input.Members.Where(m => tags.Contains(m.ElemTag) && (IsBar(m) || IsShell(m))).ToList();
        if (members.Count == 0) return result;

        var nodeByTag = new Dictionary<string, FemNode>(StringComparer.Ordinal);
        foreach (var n in input.Nodes) nodeByTag.TryAdd(n.NodeTag, n);
        var meshNodes = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var n in input.MeshNodes) meshNodes.TryAdd(n.NodeTag, n);

        var flipped = members.Select(m => m.ElemTag).ToHashSet(StringComparer.Ordinal);
        foreach (var m in members)
        {
            if (IsBar(m))
            {
                double length = BarLength(m, nodeByTag);
                FlipBarMember(m);
                foreach (var load in input.MemberLoads.Where(l => l.MemberId == m.Id && m.Id != 0))
                {
                    FlipMemberLoad(load, length);
                    result.MemberLoads++;
                }
                result.Bars.Add(m.ElemTag);
            }
            else
            {
                if (m.PlanarRegionId is int rid && input.Regions.TryGetValue(rid, out var region))
                {
                    var (mirrored, map) = MirrorRegion(region);
                    result.Regions.Add(mirrored);
                    if (input.Snapshots.TryGetValue(rid, out var snapshot))
                        result.Snapshots[rid] = MirrorSnapshot(snapshot, region, map);
                    var edges = FemLineSupportGeometry.Edges(m, mirrored);
                    foreach (var s in input.LineSupports.Where(s => s.MemberTag == m.ElemTag))
                    {
                        if (FemLineSupportGeometry.Match(s, edges) is { } edge)
                        {
                            s.AssignEdge(edge);
                            result.LineSupports++;
                        }
                        else result.LostLineSupports.Add(s);
                    }
                }
                result.Shells.Add(m.ElemTag);
            }
        }

        // КЭ сетки развёрнутых КонЭ: копии с новым порядком узлов; старые — для пересчёта нагрузок.
        var oldGeometry = new Dictionary<string, FemElementGeometry>(StringComparer.Ordinal);
        var newGeometry = new Dictionary<string, FemElementGeometry>(StringComparer.Ordinal);
        var flippedElements = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in input.MeshElements)
        {
            if (e.SourceMemberTag is not { } owner || !flipped.Contains(owner)) continue;
            var copy = FlipElement(e, meshNodes);
            if (copy == null) continue;
            result.Elements.Add(copy);
            flippedElements.Add(e.ElemTag);
            if (FemElementGeometry.Of(e, meshNodes) is { } g0 && FemElementGeometry.Of(copy, meshNodes) is { } g1)
            {
                oldGeometry[e.ElemTag] = g0;
                newGeometry[e.ElemTag] = g1;
            }
        }

        FlipElementLoads(input, result, flippedElements, oldGeometry, newGeometry);
        return result;
    }

    // ── Стержень ────────────────────────────────────────────────────────────────────────────

    /// <summary>Узлы I ↔ J и β → −β.</summary>
    public static void FlipBarMember(FemMember m)
    {
        m.NodeIdsJson = Reversed(m.NodeIdsJson);
        m.RotationDeg = Negate(m.RotationDeg);
    }

    /// <summary>Нагрузка на стержень длиной <paramref name="length"/> после разворота его оси.</summary>
    public static void FlipMemberLoad(FemMemberLoad l, double length)
    {
        bool local = string.Equals(l.CoordinateSystem, "local", StringComparison.OrdinalIgnoreCase);
        double sx = local ? -1 : 1, sz = local ? -1 : 1;
        if (string.Equals(l.DistributionType, "point", StringComparison.OrdinalIgnoreCase))
        {
            l.StartOffsetM = Clean(Math.Max(0, length - l.StartOffsetM));
            (l.QxStart, l.QzStart) = (sx * l.QxStart, sz * l.QzStart);
        }
        else
        {
            (l.StartOffsetM, l.EndOffsetM) = (l.EndOffsetM, l.StartOffsetM);
            (l.QxStart, l.QxEnd) = (sx * l.QxEnd, sx * l.QxStart);
            (l.QyStart, l.QyEnd) = (l.QyEnd, l.QyStart);
            (l.QzStart, l.QzEnd) = (sz * l.QzEnd, sz * l.QzStart);
        }
        // Моменты — вокруг местных осей: X и Z развёрнуты.
        if (local) (l.Mx, l.Mz) = (Negate(l.Mx), Negate(l.Mz));
    }

    static double BarLength(FemMember m, IReadOnlyDictionary<string, FemNode> nodes)
    {
        var tags = NodeTags(m.NodeIdsJson);
        if (tags.Count != 2 || !nodes.TryGetValue(tags[0], out var a) || !nodes.TryGetValue(tags[1], out var b)) return 0;
        return Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y) + (b.Z - a.Z) * (b.Z - a.Z));
    }

    // ── КЭ ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Развёрнутая копия КЭ: стержень — узлы в обратном порядке, шарниры концов меняются местами, β → −β; пластина —
    /// обратный обход с тем же первым узлом (T3 [a,b,c] → [a,c,b]; Q4 хранится «1 2 4 3»: [a,b,c,d] → [a,c,b,d]), угол оси
    /// выдачи усилий — так, чтобы ось осталась той же в пространстве. Null — не 2–4 узла.
    /// </summary>
    public static FemElement? FlipElement(FemElement e, IReadOnlyDictionary<string, FemMeshNode> meshNodes)
    {
        var ids = ElementIds(e.NodeIdsJson);
        if (ids == null || ids.Length is < 2 or > 4) return null;
        var copy = e.ShallowCopy();
        if (ids.Length == 2)
        {
            copy.NodeIdsJson = System.Text.Json.JsonSerializer.Serialize(new[] { ids[1], ids[0] });
            (copy.ReleaseI, copy.ReleaseJ) = (e.ReleaseJ, e.ReleaseI);
            if (e.BeamRotationDeg is double b) copy.BeamRotationDeg = Negate(b);
            return copy;
        }
        int[] flipped = ids.Length == 3 ? [ids[0], ids[2], ids[1]] : [ids[0], ids[2], ids[1], ids[3]];
        copy.NodeIdsJson = System.Text.Json.JsonSerializer.Serialize(flipped);
        if (e.LocalAxisAngleDeg is double angle
            && FemElementGeometry.Of(e, meshNodes) is { } g0 && FemElementGeometry.Of(copy, meshNodes) is { } g1)
        {
            var (x0, _, n0) = g0.ShellFrame();
            double a = angle * Math.PI / 180;
            var output = x0 * Math.Cos(a) + n0.Cross(x0) * Math.Sin(a);
            var (x1, _, n1) = g1.ShellFrame();
            double deg = Math.Atan2(output.Dot(n1.Cross(x1)), output.Dot(x1)) * 180 / Math.PI;
            copy.LocalAxisAngleDeg = Clean(deg <= -180 ? deg + 360 : deg);
        }
        return copy;
    }

    // ── Нагрузки на КЭ ──────────────────────────────────────────────────────────────────────

    static void FlipElementLoads(FemOrientationInput input, FemOrientationResult result, HashSet<string> flippedElements,
        Dictionary<string, FemElementGeometry> oldGeometry, Dictionary<string, FemElementGeometry> newGeometry)
    {
        if (flippedElements.Count == 0) return;
        var elementsByMember = input.MeshElements.Where(e => e.SourceMemberTag != null)
            .GroupBy(e => e.SourceMemberTag!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ElemTag).ToList(), StringComparer.Ordinal);
        var groups = input.Groups.ToDictionary(g => g.Id);
        List<string> TargetElements(FemElementLoad load) => load.TargetKind switch
        {
            FemLoadTargetKinds.Elements => [.. load.TargetTags],
            FemLoadTargetKinds.Members => load.TargetTags.SelectMany(t => elementsByMember.GetValueOrDefault(t) ?? []).ToList(),
            FemLoadTargetKinds.Group when load.GroupId is int id && groups.TryGetValue(id, out var g) => g.IsMeshGroup
                ? [.. g.Tags]
                : g.Tags.SelectMany(t => elementsByMember.GetValueOrDefault(t) ?? []).ToList(),
            _ => [],
        };

        foreach (var load in input.ElementLoads.ToList())
        {
            if (load.LoadKind == FemElementLoadKinds.SelfWeight) continue;
            var target = TargetElements(load);
            var hit = target.Where(flippedElements.Contains).ToList();
            if (hit.Count == 0) continue;
            // Равномерная в общих осях от порядка узлов не зависит.
            if (load.LoadKind == FemElementLoadKinds.Uniform && !load.IsLocal) continue;

            var transformed = new List<(string Tag, string Axis, double[] Values)>();
            bool failed = false;
            foreach (var tag in hit)
            {
                if (!oldGeometry.TryGetValue(tag, out var g0) || Transform(load, g0, newGeometry[tag]) is not { } t)
                {
                    failed = true;
                    break;
                }
                transformed.Add((tag, t.Axis, t.Values));
            }
            if (failed)
            {
                result.SkippedLoads.Add((load, FemOrientationLoadSkip.InPlaneAxis));
                continue;
            }
            var variants = transformed.GroupBy(t => Key(t.Axis, t.Values), StringComparer.Ordinal).ToList();
            bool whole = hit.Count == target.Distinct(StringComparer.Ordinal).Count();
            if (load.TargetKind != FemLoadTargetKinds.Elements)
            {
                if (!whole) { result.SkippedLoads.Add((load, FemOrientationLoadSkip.PartialTarget)); continue; }
                if (variants.Count > 1) { result.SkippedLoads.Add((load, FemOrientationLoadSkip.PerElementValues)); continue; }
                Set(load, variants[0].First().Axis, variants[0].First().Values);
                result.ElementLoads++;
                continue;
            }
            // Список КЭ: развёрнутые с одинаковыми новыми значениями — своей записью, остальные остаются в исходной.
            var rest = target.Where(t => !flippedElements.Contains(t)).ToList();
            int index = input.ElementLoads.IndexOf(load);
            if (rest.Count == 0 && variants.Count == 1)
                Set(load, variants[0].First().Axis, variants[0].First().Values);
            else
            {
                if (rest.Count > 0) load.SetTargetTags(rest);
                else input.ElementLoads.RemoveAt(index--);
                foreach (var v in variants)
                {
                    var part = Clone(load);
                    part.SetTargetTags(v.Select(x => x.Tag));
                    Set(part, v.First().Axis, v.First().Values);
                    input.ElementLoads.Insert(++index, part);
                }
            }
            result.ElementLoads++;
        }
    }

    /// <summary>Новые ось и значения нагрузки на КЭ после разворота; null — направление не переносится на одну ось.</summary>
    static (string Axis, double[] Values)? Transform(FemElementLoad load, FemElementGeometry g0, FemElementGeometry g1)
    {
        var v = load.Values.ToArray();
        string axis = load.Axis;
        double sign = 1;
        if (load.IsLocal)
        {
            if (g0.IsBar) sign = load.AxisIndex is 0 or 2 ? -1 : 1;
            else if (load.AxisIndex == 2) sign = -1;
            else
            {
                var (ox, oy, _) = g0.ShellFrame();
                var (nx, ny, _) = g1.ShellFrame();
                var d = load.AxisIndex == 0 ? ox : oy;
                double cx = d.Dot(nx), cy = d.Dot(ny);
                if (Math.Abs(Math.Abs(cx) - 1) < AxisTolerance) { axis = "x"; sign = Math.Sign(cx); }
                else if (Math.Abs(Math.Abs(cy) - 1) < AxisTolerance) { axis = "y"; sign = Math.Sign(cy); }
                else return null;
            }
        }
        double length = g0.IsBar ? g0.Length : 0;
        switch (load.LoadKind)
        {
            case FemElementLoadKinds.Uniform when v.Length >= 1:
                v[0] *= sign;
                break;
            case FemElementLoadKinds.Nodal when g0.IsShell:
                if (v.Length >= 3) (v[1], v[2]) = (v[2], v[1]);
                for (int i = 0; i < v.Length; i++) v[i] *= sign;
                break;
            case FemElementLoadKinds.Point when g0.IsBar && v.Length >= 2:
                v[0] *= sign;
                v[1] = Clean(length - v[1]);
                break;
            case FemElementLoadKinds.Point when g0.IsShell && v.Length >= 3:
            {
                var p = FemElementLoadNodalizer.ShellPointAt(g0, v[1], v[2]);
                var (ex, ey, _) = g1.ShellFrame();
                var r = p - g1.Points[g1.Contour[0]];
                v[0] *= sign;
                v[1] = Clean(r.Dot(ex));
                v[2] = Clean(r.Dot(ey));
                break;
            }
            case FemElementLoadKinds.Trapezoid when g0.IsBar && v.Length >= 4:
                v[0] *= sign; v[2] *= sign;
                v[1] = Clean(length - v[1]);
                v[3] = Clean(length - v[3]);
                break;
            case FemElementLoadKinds.Edge when g0.IsShell && v.Length >= 4:
                v[0] = EdgeIndex(v[0]); v[1] = EdgeIndex(v[1]);
                v[2] *= sign; v[3] *= sign;
                break;
            default:
                for (int i = 0; i < v.Length; i++) v[i] *= sign;
                break;
        }
        return (axis, v);
    }

    /// <summary>Номер узла КЭ (с 1, порядок хранения) после обратного обхода: 2 ↔ 3.</summary>
    static double EdgeIndex(double i) => i switch { 2 => 3, 3 => 2, _ => i };

    static void Set(FemElementLoad load, string axis, double[] values)
    {
        load.Axis = axis;
        load.SetValues(values);
    }

    static string Key(string axis, double[] values) =>
        axis + "|" + string.Join(";", values.Select(x => Math.Round(x, 9).ToString("R", System.Globalization.CultureInfo.InvariantCulture)));

    static FemElementLoad Clone(FemElementLoad l) => new()
    {
        SchemaId = l.SchemaId, LoadCaseId = l.LoadCaseId, Origin = l.Origin, TargetKind = l.TargetKind, GroupId = l.GroupId,
        LoadKind = l.LoadKind, CoordinateSystem = l.CoordinateSystem, Axis = l.Axis, TargetTagsJson = l.TargetTagsJson,
        ValuesJson = l.ValuesJson,
    };

    // ── Область и снимок ────────────────────────────────────────────────────────────────────

    /// <summary>Перенумерация вершин контуров: (контур, номер отверстия) → новая вершина по старой.</summary>
    public sealed class VertexMap
    {
        readonly Dictionary<(BoundaryLoop, int), int> _count = [];
        internal void Add(BoundaryLoop loop, int hole, int count) => _count[(loop, hole)] = count;
        /// <summary>Число вершин контура (0 — нет такого).</summary>
        public int Count(BoundaryLoop loop, int hole) => _count.GetValueOrDefault((loop, loop == BoundaryLoop.Outer ? 0 : hole));
        /// <summary>Новый номер вершины k: первая остаётся первой, остальные — в обратном порядке.</summary>
        public int Vertex(BoundaryLoop loop, int hole, int k)
        {
            int n = Count(loop, hole);
            return n == 0 ? k : (n - k) % n;
        }
    }

    /// <summary>Стена: местная Y области вертикальна — при смене нормали разворачивается X, Y остаётся вверх.</summary>
    public static bool KeepsLocalY(Frame3D f) => Math.Abs(Math.Abs(f.LocalY.Z) - 1) < AxisTolerance;

    /// <summary>Координаты точки области в новых осях: у плиты (u, −v), у стены (−u, v).</summary>
    static (double U, double V) Mirror(bool keepY, double u, double v) => keepY ? (Negate(u), v) : (u, Negate(v));

    /// <summary>Область с противоположной нормалью: оси (O, X, −Y, −Z) или у стены (O, −X, Y, −Z), контуры и разметка —
    /// в новых осях, обход контуров прежний (внешний — против часовой), первая вершина та же.</summary>
    public static (PlanarRegion Region, VertexMap Map) MirrorRegion(PlanarRegion r)
    {
        bool keepY = KeepsLocalY(r.Frame);
        var f = r.Frame;
        var frame = keepY
            ? new Frame3D(f.Origin, Negate(f.LocalX), f.LocalY, Negate(f.LocalZ))
            : new Frame3D(f.Origin, f.LocalX, Negate(f.LocalY), Negate(f.LocalZ));
        var map = new VertexMap();
        var contours = new List<Contour>();
        int hole = 0;
        foreach (var c in r.Contours)
        {
            var (x, y) = PlanarRegionTopologyValidator.ToOpenLoop(c.X, c.Y);
            var nx = new List<double>(x.Length + 1);
            var ny = new List<double>(x.Length + 1);
            for (int k = 0; k < x.Length; k++)
            {
                int old = k == 0 ? 0 : x.Length - k;
                var (u, v) = Mirror(keepY, x[old], y[old]);
                nx.Add(u); ny.Add(v);
            }
            if (x.Length > 0) { nx.Add(nx[0]); ny.Add(ny[0]); }
            if (c.Type == ContourType.Hull) map.Add(BoundaryLoop.Outer, 0, x.Length);
            else if (c.Type == ContourType.Hole) map.Add(BoundaryLoop.Hole, hole++, x.Length);
            var copy = new Contour { Tag = c.Tag, Type = c.Type, Num = c.Num, GeometrySet = c.GeometrySet, IsPolyline = c.IsPolyline, X = nx, Y = ny };
            copy.Id = c.Id;
            copy.SetWKT();
            copy.Points = copy.XYsToPoints();
            contours.Add(copy);
        }
        var region = new PlanarRegion
        {
            Id = r.Id, Tag = r.Tag, Frame = frame, FrameIsRecovered = r.FrameIsRecovered, Contours = contours,
            SourceContourId = r.SourceContourId, RebarSectionGridStep = r.RebarSectionGridStep, MeshMaxElementSizeM = r.MeshMaxElementSizeM,
            BoundarySegments = r.BoundarySegments.Select(s => new BoundarySegment
            {
                Loop = s.Loop, HoleIndex = s.HoleIndex, Role = s.Role, Provenance = s.Provenance,
                StartVertex = map.Vertex(s.Loop, s.HoleIndex, s.EndVertex),
                EndVertex = map.Vertex(s.Loop, s.HoleIndex, s.StartVertex),
            }).ToList(),
            ConstraintObjects = r.ConstraintObjects.Select(o => MirrorConstraint(o, keepY)).ToList(),
            RebarZones = r.RebarZones.Select(z =>
            {
                var copy = z.Clone();
                foreach (var p in copy.Polygon) (p.U, p.V) = Mirror(keepY, p.U, p.V);
                copy.Layout.Angle = Negate(copy.Layout.Angle);
                return copy;
            }).ToList(),
        };
        region.RecalcFingerprint();
        return (region, map);
    }

    static PlanarConstraintObject MirrorConstraint(PlanarConstraintObject o, bool keepY)
    {
        var points = o.Geometry.Points.Select(p =>
        {
            var (u, v) = Mirror(keepY, p.U, p.V);
            return new PlanarPoint2D(u, v);
        }).ToList();
        // Область ограничения — с прежним направлением обхода (первая точка та же).
        if (o.Geometry.Kind == PlanarConstraintGeometryKind.Region && points.Count > 2)
            points = [points[0], .. Enumerable.Range(1, points.Count - 1).Select(k => points[points.Count - k])];
        return new PlanarConstraintObject
        {
            Id = o.Id, Tag = o.Tag, Geometry = o.Geometry with { Points = points }, StructuralFacet = o.StructuralFacet,
            MeshFacet = o.MeshFacet, MasterReference = o.MasterReference, DofMask = o.DofMask, StructuralFrame = o.StructuralFrame,
            ToleranceM = o.ToleranceM, Provenance = o.Provenance, IsDerived = o.IsDerived, KeepVertices = o.KeepVertices,
            SourceReferences = [.. o.SourceReferences], StructuralRelations = [.. o.StructuralRelations],
        };
    }

    /// <summary>Снимок сетки области в новых осях: узлы те же (u, v — в новых осях), КЭ — обратный обход с тем же первым
    /// узлом, участки границы — по новой нумерации вершин. Отпечаток входа пуст: его ставит сборка сетки.</summary>
    public static PlanarMeshSnapshot MirrorSnapshot(PlanarMeshSnapshot s, PlanarRegion oldRegion, VertexMap map)
    {
        bool keepY = KeepsLocalY(oldRegion.Frame);
        return new PlanarMeshSnapshot
        {
            RegionId = s.RegionId, InputFingerprint = "", IsCalculable = s.IsCalculable, Settings = s.Settings,
            Provenance = s.Provenance, Diagnostics = s.Diagnostics, MeshFormatVersion = s.MeshFormatVersion,
            EntityProvenance = s.EntityProvenance, ConstraintMappings = s.ConstraintMappings,
            Nodes = s.Nodes.Select(n =>
            {
                var (u, v) = Mirror(keepY, n.U, n.V);
                return n with { U = u, V = v };
            }).ToList(),
            Elements = s.Elements.Select(e => e with
            {
                NodeIndices = e.NodeIndices.Count < 3 ? e.NodeIndices
                    : [e.NodeIndices[0], .. Enumerable.Range(1, e.NodeIndices.Count - 1).Select(k => e.NodeIndices[e.NodeIndices.Count - k])],
            }).ToList(),
            BoundaryMappings = s.BoundaryMappings.Select(b => new PlanarMeshBoundaryMapping
            {
                Key = b.Key with
                {
                    StartVertex = map.Vertex(b.Key.Loop, b.Key.HoleIndex, b.Key.EndVertex),
                    EndVertex = map.Vertex(b.Key.Loop, b.Key.HoleIndex, b.Key.StartVertex),
                },
                NodeIndices = [.. b.NodeIndices.Reverse()],
            }).ToList(),
        };
    }

    // ── Мелочи ──────────────────────────────────────────────────────────────────────────────

    /// <summary>JSON-массив узлов в обратном порядке (элементы — как были: числа или строки).</summary>
    static string Reversed(string json)
    {
        if (JsonNode.Parse(json) is not JsonArray a) return json;
        var items = a.Select(x => x?.DeepClone()).Reverse().ToArray();
        return new JsonArray(items).ToJsonString();
    }

    static List<string> NodeTags(string json) =>
        JsonNode.Parse(json) is JsonArray a
            ? a.Select(x => x is JsonValue v && v.TryGetValue(out long l) ? l.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : x?.GetValue<string>() ?? "").ToList()
            : [];

    static int[]? ElementIds(string json)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<int[]>(json); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    /// <summary>−v без «−0» в компонентах.</summary>
    static PlanarVector3 Negate(PlanarVector3 v) => new(Negate(v.X), Negate(v.Y), Negate(v.Z));

    /// <summary>−x без «−0».</summary>
    static double Negate(double x) => x == 0 ? 0 : -x;

    /// <summary>Сброс шума округления: |x| &lt; 1e-12 → 0.</summary>
    static double Clean(double x) => Math.Abs(x) < 1e-12 ? 0 : x;
}
