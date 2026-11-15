using System.Globalization;
using CScore.Fem;
using CScore.Fem.Loads;
using CScore.Planar;

namespace CScore.Sp63Punching.Fem;

/// <summary>
/// Поиск узлов «колонна — плита» в сетке (спека 2026-11-15, срез 3): стержень под углом к плите не меньше
/// <see cref="PunchingFemOptions.AngleDeg"/>, конец которого — узел плиты или узел тела АЖТ с ведомыми в плите.
/// Стержни одного узла (одного тела АЖТ) — один узел продавливания. Геометрия — в осях узла
/// (<see cref="PunchingFemJoint"/>). Узел, который не проверяется, возвращается с причиной, а не пропускается.
/// </summary>
public static class PunchingFemJointFinder
{
    static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>Плоскость круче этого — стена, а не плита: узлы в ней не ищутся.</summary>
    public const double MaxSlabTiltDeg = 45;

    /// <summary>Все узлы продавливания схемы в порядке тегов узлов.</summary>
    public static List<PunchingFemJoint> Find(PunchingFemModel model, PunchingFemOptions? options = null) =>
        Find(model, options, out _);

    /// <summary>Узлы продавливания и плиты схемы (<see cref="PunchingFemJoint.SlabIndex"/> — индекс в <paramref name="slabs"/>).</summary>
    public static List<PunchingFemJoint> Find(PunchingFemModel model, PunchingFemOptions? options, out IReadOnlyList<PunchingFemSlab> slabs)
    {
        ArgumentNullException.ThrowIfNull(model);
        options ??= new PunchingFemOptions();
        var ctx = new Context(model, options);
        var joints = new List<PunchingFemJoint>();
        foreach (var ((node, slab), bars) in ctx.Candidates())
            joints.Add(ctx.Build(node, ctx.Slabs[slab], bars));
        joints.AddRange(ctx.ShellPylons());
        slabs = ctx.Slabs;
        return joints.OrderBy(j => j.NodeTag, FemNodeRigidBodies.TagComparer).ToList();
    }

    sealed class Context
    {
        public PunchingFemModel Model { get; }
        public PunchingFemOptions Options { get; }
        public List<PunchingFemSlab> Slabs { get; }
        readonly Dictionary<string, List<PunchingFemSlab>> _nodeSlabs = new(StringComparer.Ordinal);
        readonly Dictionary<string, List<(FemElement E, FemElementGeometry G)>> _at = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> _slabOfShell = new(StringComparer.Ordinal);
        readonly Dictionary<string, FemRigidBody> _bodyOf = new(StringComparer.Ordinal);
        readonly Dictionary<string, (FemElement E, FemElementGeometry G)> _bars = new(StringComparer.Ordinal);

        public Context(PunchingFemModel model, PunchingFemOptions options)
        {
            Model = model;
            Options = options;
            Slabs = PunchingFemSlabs.Build(PunchingFemSlabs.Shells(model), options.PlaneTolM);
            foreach (var s in Slabs)
            {
                foreach (var n in s.NodeTags)
                {
                    if (!_nodeSlabs.TryGetValue(n, out var l)) _nodeSlabs[n] = l = [];
                    l.Add(s);
                }
                foreach (var sh in s.Shells) _slabOfShell[sh.Element.ElemTag] = s.Index;
            }
            foreach (var e in model.Elements)
            {
                if (FemElementGeometry.Of(e, model.Nodes) is not { } g) continue;
                if (g.IsBar && e.ElemType != "shell") _bars[e.ElemTag] = (e, g);
                foreach (var t in g.NodeTags)
                {
                    if (!_at.TryGetValue(t, out var l)) _at[t] = l = [];
                    l.Add((e, g));
                }
            }
            foreach (var b in model.RigidBodies)
            {
                _bodyOf.TryAdd(b.MasterNodeTag, b);
                foreach (var s in b.SlaveNodeTags) _bodyOf.TryAdd(s, b);
            }
        }

        double SinAngle => Math.Sin(Options.AngleDeg * Math.PI / 180);

        /// <summary>Плиты, в которые входит узел: свои узлы плиты или узлы его тела АЖТ.</summary>
        IEnumerable<PunchingFemSlab> HostSlabs(string node)
        {
            if (_nodeSlabs.TryGetValue(node, out var own)) return own;
            if (!_bodyOf.TryGetValue(node, out var body)) return [];
            return body.SlaveNodeTags.Append(body.MasterNodeTag)
                .SelectMany(t => _nodeSlabs.TryGetValue(t, out var l) ? l : [])
                .DistinctBy(s => s.Index);
        }

        /// <summary>Ключ узла: ведущий узел тела АЖТ, иначе сам узел.</summary>
        string JointNode(string node) => _bodyOf.TryGetValue(node, out var b) ? b.MasterNodeTag : node;

        public Dictionary<(string Node, int Slab), List<PunchingFemBar>> Candidates()
        {
            var result = new Dictionary<(string, int), List<PunchingFemBar>>();
            foreach (var (e, g) in _bars.Values)
                for (int k = 0; k < 2; k++)
                {
                    string n = g.NodeTags[k];
                    var d = g.Points[1 - k] - g.Points[k];
                    if (d.Length < 1e-12) continue;
                    var axis = d.Normalize();
                    foreach (var slab in HostSlabs(n))
                    {
                        // Стена (наклон больше 45°) — не плита: стержень, входящий в стену, — не узел продавливания.
                        if (slab.TiltDeg > MaxSlabTiltDeg) continue;
                        double c = axis.Dot(slab.Normal);
                        if (Math.Abs(c) < SinAngle) continue;
                        var key = (JointNode(n), slab.Index);
                        if (!result.TryGetValue(key, out var list)) result[key] = list = [];
                        list.Add(new PunchingFemBar(e.ElemTag, n, c < 0));
                    }
                }
            return result;
        }

        public PunchingFemJoint Build(string node, PunchingFemSlab slab, List<PunchingFemBar> bars)
        {
            var warnings = new List<PunchingWarning>();
            var n = slab.Normal;
            var pNode = Position(node) ?? Position(bars[0].NodeTag)!.Value;
            var origin = pNode - n * slab.Distance(pNode);
            var (ax0, ay0) = Basis(n);
            _bodyOf.TryGetValue(node, out var body);
            var junction = body == null ? [node] : body.SlaveNodeTags.Append(body.MasterNodeTag).Distinct(StringComparer.Ordinal).ToList();

            PunchingFemJoint Fail(string code, string text, PlanarVector3? x = null, PlanarVector3? y = null, PlanarVector3? o = null) => new()
            {
                NodeTag = node, SlabIndex = slab.Index, Origin = o ?? origin, AxisX = x ?? ax0, AxisY = y ?? ay0, Normal = n,
                Bars = bars, JunctionNodes = junction, NoCheck = code, NoCheckText = text, Warnings = warnings,
            };

            if (slab.TiltDeg > Options.HorizontalTolDeg)
                return Fail(PunchingFemNoCheck.SlabInclined, string.Create(Ru,
                    $"Плита наклонена на {slab.TiltDeg:0.#}° к горизонтали — проверка продавливания по МКЭ — только для горизонтальных плит."));

            // Стена в теле АЖТ: долю колонны и стены не разделить.
            foreach (var t in junction)
                if (_at.TryGetValue(t, out var es) && es.Any(x => x.E.ElemType == "shell"
                        && (!_slabOfShell.TryGetValue(x.E.ElemTag, out int si) || si != slab.Index)))
                    return Fail(PunchingFemNoCheck.JunctionWallAndColumn,
                        $"В узел {node} (тело АЖТ) входят и колонна, и пластины стены — долю усилия колонны не выделить.");

            // Грузовая площадь — по сечению стержня снизу (иначе сверху).
            var ordered = bars.OrderBy(b => b.Below ? 0 : 1).ThenBy(b => b.ElemTag, FemNodeRigidBodies.TagComparer).ToList();
            (PunchingFemBar Bar, PunchingFemBarSection Section)? chosen = null;
            foreach (var b in ordered)
                if (Model.BarSection(_bars[b.ElemTag].E) is { Outline.Count: >= 3 } s) { chosen = (b, s); break; }
            if (chosen is not { } pick)
                return Fail(PunchingFemNoCheck.AreaUnknown,
                    $"У стержней узла {node} ({string.Join(", ", bars.Select(b => b.ElemTag))}) нет бетонного сечения с контуром — грузовая площадь неизвестна.");

            var (bar, bg) = _bars[pick.Bar.ElemTag];
            var (axisX, axisY) = SectionAxes(bg, Model.BarRotationDeg(bar), n, ax0);
            var local = Project(pick.Section.Outline, bg, Model.BarRotationDeg(bar), origin, n, axisX, axisY);
            var hull = ConvexHull(local);
            if (hull.Count < 3)
                return Fail(PunchingFemNoCheck.AreaUnknown, $"Контур сечения стержня {bar.ElemTag} в плоскости плиты вырожден.", axisX, axisY);
            var c = Centroid(hull);
            origin = origin + axisX * c.X + axisY * c.Y;
            hull = hull.Select(p => p - c).ToList();
            var area = Recognize(hull, out bool concave);
            if (local.Count > 3 && concave)
                warnings.Add(new("fem_area_hull", $"Сечение стержня {bar.ElemTag} невыпуклое — грузовая площадь по выпуклой оболочке."));
            foreach (var other in ordered.Where(b => b.ElemTag != bar.ElemTag && b.Below != pick.Bar.Below))
                if (Model.BarSection(_bars[other.ElemTag].E) is { Outline.Count: >= 3 } os)
                {
                    var og = _bars[other.ElemTag];
                    double a2 = Math.Abs(Area(ConvexHull(Project(os.Outline, og.G, Model.BarRotationDeg(og.E), origin, n, axisX, axisY))));
                    double a1 = Math.Abs(Area(hull));
                    if (Math.Abs(a2 - a1) > 0.05 * a1)
                        warnings.Add(new("fem_area_other_column", string.Create(Ru,
                            $"Сечения колонн {(pick.Bar.Below ? "снизу" : "сверху")} и {(pick.Bar.Below ? "сверху" : "снизу")} разные ({a1:0.###} и {a2:0.###} м²) — грузовая площадь по колонне {(pick.Bar.Below ? "снизу" : "сверху")}.")));
                    break;
                }

            if (area is PunchingLoadArea.Rectangle r && Math.Max(r.Bx, r.By) > Options.WallRatio * Math.Min(r.Bx, r.By))
                return Fail(PunchingFemNoCheck.Wall, string.Create(Ru,
                    $"Сечение {r.Bx:0.###}×{r.By:0.###} м вытянуто больше чем в {Options.WallRatio:0.#} раза — это стена, а не пилон: продавливание у стен (СП 430) пока не проверяется."),
                    axisX, axisY, origin) with { Area = area, SectionLabel = pick.Section.Label };

            var joint = new PunchingFemJoint
            {
                NodeTag = node, SlabIndex = slab.Index, Origin = origin, AxisX = axisX, AxisY = axisY, Normal = n,
                Bars = bars, JunctionNodes = junction, Area = area, SectionLabel = pick.Section.Label,
            };
            return Complete(joint, slab, area, bars.All(b => !b.Below), warnings,
                bars.Select(b => b.ElemTag).ToHashSet(StringComparer.Ordinal));
        }

        /// <summary>
        /// Общая часть узла после грузовой площади: свойства плиты, привязки (по стороне растяжения), края и отверстия,
        /// основание пирамиды, КЭ основания, сечения в основании и прочие опоры (<paramref name="own"/> — свои КЭ узла).
        /// </summary>
        PunchingFemJoint Complete(PunchingFemJoint joint, PunchingFemSlab slab, PunchingLoadArea area, bool onlyAbove,
            List<PunchingWarning> warnings, IReadOnlySet<string> own)
        {
            string node = joint.NodeTag;
            var n = joint.Normal;
            var origin = joint.Origin;
            var junction = joint.JunctionNodes;
            PunchingFemJoint Partial(string code, string text) => joint with { NoCheck = code, NoCheckText = text, Warnings = warnings };

            // Свойства плиты у узла.
            var near = slab.Shells.Where(s => s.Geometry.NodeTags.Any(junction.Contains)).ToList();
            if (near.Count == 0) near = [slab.Shells.MinBy(s => (s.Centroid - origin).Length)!];
            if (Model.ShellProps(near[0].Element) is not { } props)
                return Partial(PunchingFemNoCheck.SlabSectionUnknown, $"У пластин плиты у узла {node} нет сечения (толщины).");
            var cover = (onlyAbove ? props.Bottom : props.Top) ?? Options.DefaultCover;
            bool coverDefault = (onlyAbove ? props.Bottom : props.Top) == null;
            cover ??= PunchingFemOptions.StandardCover;
            if (coverDefault)
                warnings.Add(new("fem_cover_default", string.Create(Ru,
                    $"У сечения плиты «{props.Label}» нет {(onlyAbove ? "нижней" : "верхней")} арматуры — привязки ax = {cover.Ax * 1000:0} мм, ay = {cover.Ay * 1000:0} мм по умолчанию.")));
            joint = joint with
            {
                H = props.H, ConcreteMaterialId = props.ConcreteMaterialId, Ax = cover.Ax, Ay = cover.Ay,
                CoverDefault = coverDefault, SlabLabel = props.Label,
            };
            double h0 = joint.H0;
            if (!(h0 > 0))
                return Partial(PunchingFemNoCheck.H0NonPositive, $"Рабочая высота h0 = h − 0,5(ax + ay) ≤ 0 у сечения «{props.Label}».");

            // Края и отверстия плиты.
            var (edges, holes) = EdgesAndHoles(slab, joint, area, h0, warnings);
            joint = joint with { Edges = edges, Holes = holes };
            var basePoly = PunchingContourBuilder.PyramidBasePolygon(area, h0, edges);
            if (basePoly.Count < 3)
                return joint with { NoCheck = PunchingFemNoCheck.BaseOutside, NoCheckText = "Основание пирамиды продавливания вне плиты.", Warnings = warnings };
            joint = joint with { PyramidBase = basePoly };

            // КЭ основания: центр внутри основания; окружение — КЭ с общим узлом.
            var inside = slab.Shells.Where(s => Contains(basePoly, joint.ToLocal(s.Centroid), 1e-9)).ToList();
            var insideNodes = inside.SelectMany(s => s.Geometry.NodeTags).ToHashSet(StringComparer.Ordinal);
            var baseShells = slab.Shells.Where(s => s.Geometry.NodeTags.Any(insideNodes.Contains))
                .Select(s => s.Element.ElemTag).ToList();
            joint = joint with { BaseShells = baseShells };

            var mixed = inside.Select(s => Model.ShellProps(s.Element)).Where(p => p != null)
                .Select(p => p!).DistinctBy(p => p.SectionKey).ToList();
            if (mixed.Count > 1 || inside.Any(s => Model.ShellProps(s.Element) == null))
                return joint with
                {
                    NoCheck = PunchingFemNoCheck.SlabSectionMixed,
                    NoCheckText = $"В основании пирамиды у узла {node} пластины с разными сечениями ({string.Join(", ", mixed.Select(p => p.Label).Take(4))}) " +
                                  "или без сечения — перепад толщины в зоне продавливания не проверяется.",
                    Warnings = warnings,
                };

            // Прочие элементы и опоры в основании пирамиды.
            var barTags = own;
            var junctionSet = junction.ToHashSet(StringComparer.Ordinal);
            var supportBars = new SortedSet<string>(FemNodeRigidBodies.TagComparer);
            var walls = new SortedSet<string>(FemNodeRigidBodies.TagComparer);
            var ribs = new SortedSet<string>(FemNodeRigidBodies.TagComparer);
            foreach (var t in insideNodes.Concat(junction))
            {
                if (!_at.TryGetValue(t, out var es)) continue;
                foreach (var (e, g) in es)
                {
                    if (barTags.Contains(e.ElemTag)) continue;
                    if (e.ElemType == "shell")
                    {
                        if (!_slabOfShell.TryGetValue(e.ElemTag, out int si) || si != slab.Index) walls.Add(e.ElemTag);
                        continue;
                    }
                    if (!g.IsBar) continue;
                    var d = g.Points[1] - g.Points[0];
                    bool inPlane = d.Length > 1e-12 && Math.Abs(d.Normalize().Dot(n)) < SinAngle;
                    (inPlane ? ribs : supportBars).Add(e.ElemTag);
                }
            }
            if (walls.Count > 0 || supportBars.Count > 0)
                return joint with
                {
                    NoCheck = PunchingFemNoCheck.SupportInsideBase,
                    NoCheckText = $"В основании пирамиды у узла {node} есть другие опоры плиты " +
                                  $"({string.Join(", ", walls.Concat(supportBars).Take(6))}{(walls.Count + supportBars.Count > 6 ? ", …" : "")}) — " +
                                  "сила колонны идёт не только через контур продавливания.",
                    Warnings = warnings,
                };
            if (ribs.Count > 0)
                warnings.Add(new("fem_beam_in_base",
                    $"В основании пирамиды есть стержни в плоскости плиты ({string.Join(", ", ribs.Take(6))}{(ribs.Count > 6 ? ", …" : "")}): " +
                    "часть силы колонны идёт в них, сила по стержню колонны завышена — точнее сила по контуру."));
            int supported = insideNodes.Count(t => !junctionSet.Contains(t) && Model.SupportedNodes.Contains(t));
            if (supported > 0)
                warnings.Add(new("fem_supports_in_base", string.Create(Ru,
                    $"В основании пирамиды {supported} узлов с опорами или пружинами (упругое основание): их отпор вычитается из силы только при расчёте по контуру.")));

            return joint with { Ribs = ribs.Count > 0, Warnings = warnings };
        }

        /// <summary>
        /// Пилоны из пластин: вертикальная плита (стена) примыкает к горизонтальной по общим узлам на одной прямой длиной
        /// L ≤ <see cref="PunchingFemOptions.WallRatio"/>·t (t — толщина стены); длиннее — стена (срез 5), узлом не
        /// считается. Стены снизу и сверху по той же линии — один узел. Грузовая площадь — прямоугольник L × t по оси
        /// линии, ось x узла — вдоль линии.
        /// </summary>
        public List<PunchingFemJoint> ShellPylons()
        {
            var found = new List<(PunchingFemSlab Slab, PlanarVector3 A, PlanarVector3 B, double T, List<string> Shared, List<PunchingFemSlab> Walls, bool Below, PunchingFemShellProps Props)>();
            foreach (var slab in Slabs.Where(s => s.TiltDeg <= Options.HorizontalTolDeg))
                foreach (var wall in Slabs.Where(s => s.TiltDeg >= 80))
                {
                    var shared = wall.NodeTags.Where(slab.NodeTags.Contains).ToList();
                    if (shared.Count < 2) continue;
                    var pts = shared.Select(t => Position(t)!.Value).ToList();
                    var (a, b) = Farthest(pts);
                    var d = b - a;
                    if (d.Length < 1e-6) continue;
                    var dir = d.Normalize();
                    if (pts.Any(p => ((p - a) - dir * (p - a).Dot(dir)).Length > Options.PlaneTolM)) continue;
                    var touching = wall.Shells.Where(s => s.Geometry.NodeTags.Any(shared.Contains)).ToList();
                    if (touching.Count == 0 || Model.ShellProps(touching[0].Element) is not { } props || !(props.H > 0)) continue;
                    if (d.Length > Options.WallRatio * props.H) continue;
                    bool below = touching.Any(s => slab.Distance(s.Centroid) < 0);
                    var same = found.FindIndex(f => f.Slab == slab && ((f.A + f.B) * 0.5 - (a + b) * 0.5).Length < 1e-3);
                    if (same >= 0)
                    {
                        var f = found[same];
                        found[same] = f with { Walls = [.. f.Walls, wall], Shared = f.Shared.Union(shared).ToList(), Below = f.Below || below };
                    }
                    else found.Add((slab, a, b, props.H, shared, [wall], below, props));
                }

            var joints = new List<PunchingFemJoint>();
            foreach (var (slab, a, b, t, shared, walls, below, props) in found)
            {
                var n = slab.Normal;
                var mid = (a + b) * 0.5;
                mid = mid - n * slab.Distance(mid);
                var dir = (b - a) - n * (b - a).Dot(n);
                var x = dir.Normalize();
                var y = n.Cross(x).Normalize();
                string node = shared.MinBy(s => (Position(s)!.Value - mid).Length)!;
                var wallShells = walls.SelectMany(w => w.Shells).Select(s => s.Element.ElemTag).ToHashSet(StringComparer.Ordinal);
                var touching = walls.SelectMany(w => w.Shells).Where(s => s.Geometry.NodeTags.Any(shared.Contains))
                    .Select(s => s.Element.ElemTag).OrderBy(s => s, FemNodeRigidBodies.TagComparer).ToList();
                var area = new PunchingLoadArea.Rectangle((b - a).Length, t);
                var joint = new PunchingFemJoint
                {
                    NodeTag = node, SlabIndex = slab.Index, Origin = mid, AxisX = x, AxisY = y, Normal = n,
                    Kind = PunchingFemJointKind.PylonShells, WallShells = touching, JunctionNodes = shared, Area = area,
                    SectionLabel = string.Create(Ru, $"пилон {area.Bx * 1000:0}×{t * 1000:0} ({props.Label})"),
                };
                var warnings = new List<PunchingWarning>
                {
                    new("fem_pylon_shells", "Пилон из пластин: сила пилона на плиту — по узловым силам его пластин; проверены также торцы — каждый по своему П-контуру с силой торца по разрезу."),
                };
                if (slab.TiltDeg > Options.HorizontalTolDeg)
                {
                    joints.Add(joint with { NoCheck = PunchingFemNoCheck.SlabInclined, NoCheckText = "Плита наклонная.", Warnings = warnings });
                    continue;
                }
                joints.Add(Complete(joint, slab, area, !below, warnings, wallShells));
            }
            return joints;
        }

        static (PlanarVector3 A, PlanarVector3 B) Farthest(IReadOnlyList<PlanarVector3> pts)
        {
            var a = pts[0];
            var b = pts.MaxBy(p => (p - a).Length);
            a = pts.MaxBy(p => (p - b).Length);
            return (a, b);
        }

        PlanarVector3? Position(string tag) => Model.Nodes.TryGetValue(tag, out var m) ? new PlanarVector3(m.X, m.Y, m.Z) : null;

        (IReadOnlyList<PunchingSlabEdge> Edges, IReadOnlyList<PunchingHole> Holes) EdgesAndHoles(
            PunchingFemSlab slab, PunchingFemJoint joint, PunchingLoadArea area, double h0, List<PunchingWarning> warnings)
        {
            var areaPoly = PunchingContourBuilder.AreaPolygon(area);
            var loops = slab.Loops
                .Select(l => l.Select(t => Position(t)).Where(p => p != null).Select(p => joint.ToLocal(p!.Value)).ToList())
                .Where(l => l.Count >= 3).ToList();
            var holes = new List<PunchingHole>();
            var candidates = new List<(PunchingSlabEdge Edge, double Dist)>();
            double edgeLimit = Options.EdgeInfluence * h0, holeLimit = Options.HoleInfluence * h0;
            foreach (var loop in loops)
            {
                double sa = Area(loop);
                if (sa < 0)
                {
                    if (PolygonDistance(areaPoly, loop) < holeLimit)
                        holes.Add(new PunchingHole(Enumerable.Reverse(loop).ToList()));
                    continue;
                }
                foreach (var (a, b) in MergeCollinear(loop))
                {
                    double dist = SegmentPolygonDistance(a, b, areaPoly);
                    if (dist >= edgeLimit) continue;
                    var dir = (b - a).Normalized();
                    var inward = new PlanPoint(-dir.Y, dir.X);
                    var edge = new PunchingSlabEdge(a, inward);
                    if (edge.SignedDistance(default) <= 0) continue;
                    candidates.Add((edge, dist));
                }
            }

            var chosen = new List<PunchingSlabEdge>();
            var sides = new List<PlanPoint>();
            int skipped = 0;
            foreach (var (edge, _) in candidates.OrderBy(x => x.Dist))
            {
                var side = SideOf(edge.InwardNormal);
                if (chosen.Count >= 2 || sides.Any(s => s.Dot(side) > 0.5 || s.Dot(side) < -0.5)) { skipped++; continue; }
                chosen.Add(edge);
                sides.Add(side);
            }
            if (skipped > 0)
                warnings.Add(new("fem_edges_skipped", string.Create(Ru,
                    $"Ближе {Options.EdgeInfluence:0.#}h0 к площади ещё {skipped} участков края плиты (с той же или противоположной стороны) — учтены два ближайших с соседних сторон.")));
            return (chosen, holes);
        }
    }

    /// <summary>Сторона площади (±x, ±y), на которой лежит край с внутренней нормалью <paramref name="inward"/>.</summary>
    public static PlanPoint SideOf(PlanPoint inward)
    {
        var o = new PlanPoint(-inward.X, -inward.Y);
        return Math.Abs(o.X) >= Math.Abs(o.Y) ? new PlanPoint(Math.Sign(o.X), 0) : new PlanPoint(0, Math.Sign(o.Y));
    }

    /// <summary>Базис плоскости по нормали: X — проекция общей X (или Y), Y = n × X.</summary>
    static (PlanarVector3 X, PlanarVector3 Y) Basis(PlanarVector3 n)
    {
        var x = new PlanarVector3(1, 0, 0);
        var px = x - n * x.Dot(n);
        if (px.Length < 1e-6) px = new PlanarVector3(0, 1, 0) - n * n.Y;
        px = px.Normalize();
        return (px, n.Cross(px).Normalize());
    }

    /// <summary>Оси узла: x — проекция местной оси Y стержня на плиту (вырождена — оси Z), y = n × x.</summary>
    static (PlanarVector3 X, PlanarVector3 Y) SectionAxes(FemElementGeometry g, double rotationDeg, PlanarVector3 n, PlanarVector3 fallback)
    {
        var (_, ey, ez) = BeamLocalAxisConvention.Frame(g.Points[0], g.Points[1], rotationDeg);
        foreach (var v in new[] { ey, ez, fallback })
        {
            var p = v - n * v.Dot(n);
            if (p.Length > 1e-6)
            {
                var x = p.Normalize();
                return (x, n.Cross(x).Normalize());
            }
        }
        return Basis(n);
    }

    /// <summary>Контур сечения (Y, Z) стержня, спроецированный вдоль оси на плоскость плиты, в осях узла.</summary>
    static List<PlanPoint> Project(IReadOnlyList<(double Y, double Z)> outline, FemElementGeometry g, double rotationDeg,
        PlanarVector3 o, PlanarVector3 n, PlanarVector3 x, PlanarVector3 y)
    {
        var (axis, ey, ez) = BeamLocalAxisConvention.Frame(g.Points[0], g.Points[1], rotationDeg);
        double denom = axis.Dot(n);
        return outline.Select(yz =>
        {
            var p = o + ey * yz.Y + ez * yz.Z;
            p = p - axis * ((p - o).Dot(n) / denom);
            var d = p - o;
            return new PlanPoint(d.Dot(x), d.Dot(y));
        }).ToList();
    }

    /// <summary>
    /// Форма площади по выпуклому контуру с центром в начале: прямоугольник вдоль осей узла (4 вершины, прямые углы,
    /// стороны вдоль x и y с точностью 0,5°), круг (≥ 12 вершин, радиусы в пределах 1 %) или многоугольник.
    /// </summary>
    public static PunchingLoadArea Recognize(IReadOnlyList<PlanPoint> hull, out bool concave)
    {
        concave = false;
        if (hull.Count == 4)
        {
            bool aligned = true;
            for (int i = 0; i < 4; i++)
            {
                var d = (hull[(i + 1) % 4] - hull[i]).Normalized();
                if (Math.Min(Math.Abs(d.X), Math.Abs(d.Y)) > Math.Sin(0.5 * Math.PI / 180)) aligned = false;
            }
            if (aligned)
            {
                double bx = hull.Max(p => p.X) - hull.Min(p => p.X), by = hull.Max(p => p.Y) - hull.Min(p => p.Y);
                return new PunchingLoadArea.Rectangle(bx, by);
            }
        }
        if (hull.Count >= 12)
        {
            var r = hull.Select(p => p.Length).ToList();
            double mean = r.Average();
            if (mean > 0 && r.All(v => Math.Abs(v - mean) <= 0.01 * mean))
                return new PunchingLoadArea.Circle(2 * mean);
        }
        return new PunchingLoadArea.Polygon(hull.ToList());
    }

    /// <summary>Отрезки петли с объединением соседних почти коллинеарных (отклонение до 2°).</summary>
    static List<(PlanPoint A, PlanPoint B)> MergeCollinear(IReadOnlyList<PlanPoint> loop)
    {
        int m = loop.Count;
        var segs = Enumerable.Range(0, m).Select(i => (A: loop[i], B: loop[(i + 1) % m]))
            .Where(s => (s.B - s.A).Length > 1e-9).ToList();
        if (segs.Count == 0) return [];
        double cosTol = Math.Cos(2 * Math.PI / 180);
        bool Same((PlanPoint A, PlanPoint B) p, (PlanPoint A, PlanPoint B) q) =>
            (p.B - p.A).Normalized().Dot((q.B - q.A).Normalized()) >= cosTol;
        // Начать с излома, чтобы прямой участок не разрезался на стыке начала петли.
        int start = Enumerable.Range(0, segs.Count).FirstOrDefault(i => !Same(segs[(i - 1 + segs.Count) % segs.Count], segs[i]), 0);
        var result = new List<(PlanPoint, PlanPoint)>();
        var cur = segs[start];
        for (int k = 1; k <= segs.Count; k++)
        {
            var s = segs[(start + k) % segs.Count];
            if (k < segs.Count && Same(cur, s)) { cur = (cur.A, s.B); continue; }
            result.Add(cur);
            cur = s;
        }
        return result;
    }

    static double Area(IReadOnlyList<PlanPoint> p)
    {
        double a = 0;
        for (int i = 0; i < p.Count; i++) a += p[i].Cross(p[(i + 1) % p.Count]);
        return a / 2;
    }

    static PlanPoint Centroid(IReadOnlyList<PlanPoint> p)
    {
        double a = 0, cx = 0, cy = 0;
        for (int i = 0; i < p.Count; i++)
        {
            var q = p[(i + 1) % p.Count];
            double cr = p[i].Cross(q);
            a += cr; cx += (p[i].X + q.X) * cr; cy += (p[i].Y + q.Y) * cr;
        }
        return Math.Abs(a) > 1e-18 ? new PlanPoint(cx / (3 * a), cy / (3 * a)) : p[0];
    }

    /// <summary>Выпуклая оболочка против часовой стрелки (без коллинеарных вершин).</summary>
    static List<PlanPoint> ConvexHull(IReadOnlyList<PlanPoint> points)
    {
        var p = points.Select(q => new PlanPoint(Math.Round(q.X, 9), Math.Round(q.Y, 9))).Distinct()
            .OrderBy(q => q.X).ThenBy(q => q.Y).ToList();
        if (p.Count < 3) return p;
        var h = new List<PlanPoint>();
        foreach (var pass in new[] { p, Enumerable.Reverse(p).ToList() })
        {
            int start = h.Count;
            foreach (var q in pass)
            {
                while (h.Count >= start + 2 && (h[^1] - h[^2]).Cross(q - h[^2]) <= 1e-12) h.RemoveAt(h.Count - 1);
                h.Add(q);
            }
            h.RemoveAt(h.Count - 1);
        }
        return h;
    }

    /// <summary>Точка внутри выпуклого многоугольника (против часовой) или на границе в допуске.</summary>
    public static bool Contains(IReadOnlyList<PlanPoint> poly, PlanPoint q, double tol)
    {
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var d = poly[(i + 1) % poly.Count] - a;
            double len = d.Length;
            if (len < 1e-15) continue;
            if (d.Cross(q - a) / len < -tol) return false;
        }
        return true;
    }

    static double PointSegment(PlanPoint p, PlanPoint a, PlanPoint b)
    {
        var ab = b - a;
        double l2 = ab.Dot(ab);
        double t = l2 > 0 ? Math.Clamp((p - a).Dot(ab) / l2, 0, 1) : 0;
        return (p - (a + t * ab)).Length;
    }

    static bool SegmentsCross(PlanPoint a, PlanPoint b, PlanPoint c, PlanPoint d)
    {
        double d1 = (b - a).Cross(c - a), d2 = (b - a).Cross(d - a), d3 = (d - c).Cross(a - c), d4 = (d - c).Cross(b - c);
        return d1 * d2 < 0 && d3 * d4 < 0;
    }

    static double SegmentPolygonDistance(PlanPoint a, PlanPoint b, IReadOnlyList<PlanPoint> poly)
    {
        if (Contains(poly, a, 0) || Contains(poly, b, 0)) return 0;
        double d = double.MaxValue;
        for (int i = 0; i < poly.Count; i++)
        {
            var p = poly[i]; var q = poly[(i + 1) % poly.Count];
            if (SegmentsCross(a, b, p, q)) return 0;
            d = Math.Min(d, Math.Min(PointSegment(p, a, b), Math.Min(PointSegment(a, p, q), PointSegment(b, p, q))));
        }
        return d;
    }

    static double PolygonDistance(IReadOnlyList<PlanPoint> area, IReadOnlyList<PlanPoint> other)
    {
        double d = double.MaxValue;
        for (int j = 0; j < other.Count; j++)
            d = Math.Min(d, SegmentPolygonDistance(other[j], other[(j + 1) % other.Count], area));
        return d;
    }
}
