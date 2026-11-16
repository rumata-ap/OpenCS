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
        {
            var joint = ctx.Build(node, ctx.Slabs[slab], bars);
            // Стержень-стена (b/a > 4): два торца по СП 430 п. 6.3.8.
            if (joint.NoCheck == PunchingFemNoCheck.Wall && joint.Area is PunchingLoadArea.Rectangle wr)
                joints.AddRange(ctx.BarWallEnds(joint, ctx.Slabs[slab], wr));
            else joints.Add(joint);
        }
        joints.AddRange(ctx.ShellPylons());
        joints.AddRange(ctx.ShellWalls());
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
        PunchingFemJoint Complete(PunchingFemJoint joint, PunchingFemSlab slab, PunchingLoadArea? area, bool onlyAbove,
            List<PunchingWarning> warnings, IReadOnlySet<string> own)
        {
            var wall = joint.Wall;
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
            // Узел стены: грузовая площадь — кусок стены в зоне контура (длина зависит от h0).
            if (wall != null)
            {
                area = new PunchingLoadArea.Polygon(PunchingWallGeometry.AreaPolygon(wall, h0));
                joint = joint with { Area = area };
            }

            // Края и отверстия плиты.
            var (edges, holes) = EdgesAndHoles(slab, joint, area!, h0, warnings);
            joint = joint with { Edges = edges, Holes = holes };
            var basePoly = wall != null ? PunchingWallGeometry.BasePolygon(wall, h0, edges) : PunchingContourBuilder.PyramidBasePolygon(area!, h0, edges);
            if (basePoly.Count < 3)
                return joint with { NoCheck = PunchingFemNoCheck.BaseOutside, NoCheckText = "Основание пирамиды продавливания вне плиты.", Warnings = warnings };
            joint = joint with { PyramidBase = basePoly };

            // КЭ основания: центр внутри основания; окружение — КЭ с общим узлом.
            var inside = slab.Shells.Where(s => Contains(basePoly, joint.ToLocal(s.Centroid), 1e-9)).ToList();
            var insideNodes = inside.SelectMany(s => s.Geometry.NodeTags).ToHashSet(StringComparer.Ordinal);
            if (wall != null)
            {
                // Узлы линии стены — только узлы КЭ основания (тело разреза): дальние узлы длинной стены к узлу не относятся.
                junction = junction.Where(insideNodes.Contains).ToList();
                if (junction.Count == 0) junction = [node];
                joint = joint with { JunctionNodes = junction };
            }
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

        /// <summary>
        /// Стержень с сечением b/a &gt; <see cref="PunchingFemOptions.WallRatio"/> — стена (п. 5.1.8 СП 430): два торца, каждый
        /// по рис. 6.1 с осью x наружу от стены. Сила торца — по разрезу плиты (сила стержня на торцы не делится).
        /// </summary>
        public IEnumerable<PunchingFemJoint> BarWallEnds(PunchingFemJoint j, PunchingFemSlab slab, PunchingLoadArea.Rectangle r)
        {
            bool alongX = r.Bx >= r.By;
            double len = Math.Max(r.Bx, r.By), t = Math.Min(r.Bx, r.By);
            var axis = alongX ? j.AxisX : j.AxisY;
            var own = j.Bars.Select(b => b.ElemTag).ToHashSet(StringComparer.Ordinal);
            int k = 0;
            foreach (int s in new[] { 1, -1 })
            {
                k++;
                var x = axis * s;
                var end = j with
                {
                    NodeTag = $"{j.NodeTag}/{k}", Origin = j.Origin + axis * (s * len / 2), AxisX = x, AxisY = j.Normal.Cross(x).Normalize(),
                    Kind = PunchingFemJointKind.WallEnd, Wall = new PunchingWall(PunchingWallKind.End, t), NoCheck = null, NoCheckText = null,
                    SectionLabel = string.Create(Ru, $"торец {k} стены {len * 1000:0}×{t * 1000:0} ({j.SectionLabel})"),
                };
                var warnings = new List<PunchingWarning>(j.Warnings)
                {
                    new("fem_wall_bar", "Стена задана стержнем (b/a > 4): каждый торец проверяется по СП 430 п. 6.3.8 с силой по разрезу плиты у торца."),
                };
                yield return Complete(end, slab, null, j.Bars.All(b => !b.Below), warnings, own);
            }
        }

        /// <summary>Отрезок примыкания стены из пластин к плите (между проёмами).</summary>
        sealed record WallSeg(string A, string B, PlanarVector3 PA, PlanarVector3 PB, double T, bool Mixed, bool Below,
            IReadOnlyList<string> Nodes, IReadOnlyList<string> Shells, string Label)
        {
            public PlanarVector3 Dir => (PB - PA).Normalize();
            public double Length => (PB - PA).Length;
            public string Other(string end) => end == A ? B : A;
            public PlanarVector3 At(string end) => end == A ? PA : PB;
        }

        /// <summary>
        /// Торцы и углы стен из пластин (СП 430 п. 6.3.8): вертикальные плиты, примыкающие к горизонтальной по линии
        /// длиннее <see cref="PunchingFemOptions.WallRatio"/>·t. Отрезки примыкания (между проёмами) снизу и сверху по одной
        /// линии и продолжающие друг друга сливаются. Свободный конец — торец, два конца под 90° ± 10° — угол, прочие
        /// примыкания (Т, крест, другой угол, разная толщина) — узел «нет проверки» с причиной. Середина стены не проверяется.
        /// </summary>
        public List<PunchingFemJoint> ShellWalls()
        {
            var joints = new List<PunchingFemJoint>();
            foreach (var slab in Slabs.Where(s => s.TiltDeg <= Options.HorizontalTolDeg))
            {
                var segs = new List<WallSeg>();
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
                    var props = touching.Select(s => Model.ShellProps(s.Element)).ToList();
                    if (props.Count == 0 || props[0] is not { H: > 0 } p0) continue;
                    if (d.Length <= Options.WallRatio * p0.H) continue;     // пилон из пластин
                    bool mixed = props.Any(p => p is null || Math.Abs(p.H - p0.H) > 1e-6);
                    bool below = touching.Any(s => slab.Distance(s.Centroid) < 0);
                    // Рёбра пластин стены у плиты: соседние по длине узлы примыкания без ребра — проём.
                    var ribs = new HashSet<(string, string)>();
                    foreach (var s in touching)
                    {
                        var g = s.Geometry;
                        for (int i = 0; i < g.Contour.Count; i++)
                        {
                            string u = g.NodeTags[g.Contour[i]], v = g.NodeTags[g.Contour[(i + 1) % g.Contour.Count]];
                            ribs.Add(string.CompareOrdinal(u, v) < 0 ? (u, v) : (v, u));
                        }
                    }
                    var order = shared.OrderBy(t => (Position(t)!.Value - a).Dot(dir)).ToList();
                    int start = 0;
                    for (int i = 1; i <= order.Count; i++)
                    {
                        bool linked = i < order.Count && ribs.Contains(string.CompareOrdinal(order[i - 1], order[i]) < 0
                            ? (order[i - 1], order[i]) : (order[i], order[i - 1]));
                        if (linked) continue;
                        if (i - 1 > start)
                        {
                            var run = order.GetRange(start, i - start);
                            var runShells = touching.Where(s => s.Geometry.NodeTags.Any(run.Contains)).Select(s => s.Element.ElemTag).ToList();
                            segs.Add(new WallSeg(run[0], run[^1], Position(run[0])!.Value, Position(run[^1])!.Value, p0.H, mixed, below,
                                run, runShells, p0.Label));
                        }
                        start = i;
                    }
                }
                segs = MergeWallSegs(segs);
                joints.AddRange(WallNodes(slab, segs));
            }
            return joints;
        }

        /// <summary>Слияние отрезков стен: совпадающие (снизу и сверху) и продолжающие друг друга по одной прямой.</summary>
        List<WallSeg> MergeWallSegs(List<WallSeg> segs)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < segs.Count && !changed; i++)
                    for (int j = i + 1; j < segs.Count && !changed; j++)
                    {
                        var s = segs[i];
                        var q = segs[j];
                        if (Math.Abs(Math.Abs(s.Dir.Dot(q.Dir)) - 1) > 1e-6) continue;
                        string? common = s.A == q.A || s.A == q.B ? s.A : s.B == q.A || s.B == q.B ? s.B : null;
                        bool same = (s.A == q.A && s.B == q.B) || (s.A == q.B && s.B == q.A);
                        if (!same && common == null) continue;
                        string a = same ? s.A : s.Other(common!), b = same ? s.B : q.Other(common!);
                        segs[i] = new WallSeg(a, b, Position(a)!.Value, Position(b)!.Value, s.T,
                            s.Mixed || q.Mixed || Math.Abs(s.T - q.T) > 1e-6, s.Below || q.Below,
                            s.Nodes.Union(q.Nodes).ToList(), s.Shells.Union(q.Shells).ToList(), s.Label);
                        segs.RemoveAt(j);
                        changed = true;
                    }
            }
            return segs;
        }

        /// <summary>Узлы стен по концам отрезков: торцы, углы и примыкания «нет проверки».</summary>
        IEnumerable<PunchingFemJoint> WallNodes(PunchingFemSlab slab, List<WallSeg> segs)
        {
            var n = slab.Normal;
            double tol = Math.Max(Options.PlaneTolM, 1e-6);
            foreach (string p in segs.SelectMany(s => new[] { s.A, s.B }).Distinct(StringComparer.Ordinal))
            {
                var at = Position(p)!.Value;
                var inc = segs.Where(s => s.A == p || s.B == p).ToList();
                // Конец, лежащий внутри другого отрезка: по той же прямой — стена продолжается (узла нет), поперёк — Т-примыкание.
                var through = segs.Where(s => s.A != p && s.B != p && OnSegment(at, s.PA, s.PB, tol)).ToList();
                if (through.Count > 0 && through.All(s => inc.All(c => Math.Abs(Math.Abs(s.Dir.Dot(c.Dir)) - 1) < 1e-6))) continue;
                var origin = at - n * slab.Distance(at);
                var nodes = inc.SelectMany(s => s.Nodes).Concat(through.SelectMany(s => s.Nodes)).Distinct(StringComparer.Ordinal).ToList();
                var shells = inc.Concat(through).SelectMany(s => s.Shells).Distinct(StringComparer.Ordinal).ToList();
                var (bx, by) = Basis(n);
                PunchingFemJoint Base(PunchingFemJointKind kind, PlanarVector3 x, PlanarVector3 o, PunchingWall? wall, string label) => new()
                {
                    NodeTag = p, SlabIndex = slab.Index, Origin = o, AxisX = x, AxisY = n.Cross(x).Normalize(), Normal = n,
                    Kind = kind, Wall = wall, WallShells = shells, JunctionNodes = nodes, SectionLabel = label,
                };
                PunchingFemJoint NoCheck(string code, string text) =>
                    Base(PunchingFemJointKind.WallEnd, bx, origin, null, "стены") with { NoCheck = code, NoCheckText = text };

                if (through.Count > 0 || inc.Count >= 3)
                {
                    yield return NoCheck(PunchingFemNoCheck.WallTee,
                        $"В узле {p} сходятся три стены и больше (Т- или крестовое примыкание) — по п. 6.3.8 СП 430 такой узел не рассматривается.");
                    continue;
                }
                bool below = inc.Any(s => s.Below);
                var warnings = new List<PunchingWarning>();
                if (inc.Count == 1)
                {
                    var s = inc[0];
                    if (s.Mixed)
                    {
                        yield return NoCheck(PunchingFemNoCheck.WallThicknessMixed,
                            $"У торца стены в узле {p} пластины стены разной толщины (снизу и сверху или по длине) — толщину стены не выбрать.");
                        continue;
                    }
                    var x = (at - s.At(s.Other(p)));
                    x = (x - n * x.Dot(n)).Normalize();
                    var wall = new PunchingWall(PunchingWallKind.End, s.T);
                    var j = Base(PunchingFemJointKind.WallEnd, x, origin, wall, string.Create(Ru, $"торец стены t = {s.T * 1000:0} ({s.Label})"));
                    yield return Complete(j, slab, null, !below, warnings, shells.ToHashSet(StringComparer.Ordinal));
                    continue;
                }
                // Два отрезка: угол.
                var u1 = Away(inc[0], p, n);
                var u2 = Away(inc[1], p, n);
                double angle = Math.Acos(Math.Clamp(u1.Dot(u2), -1, 1)) * 180 / Math.PI;
                if (Math.Abs(angle - 90) > 10)
                {
                    yield return NoCheck(PunchingFemNoCheck.WallAngle, string.Create(Ru,
                        $"Стены в узле {p} сходятся под углом {angle:0.#}° — по п. 6.3.8 СП 430 рассматривается угол 90°."));
                    continue;
                }
                if (inc[0].Mixed || inc[1].Mixed)
                {
                    yield return NoCheck(PunchingFemNoCheck.WallThicknessMixed,
                        $"У угла стен в узле {p} пластины стены разной толщины (снизу и сверху или по длине) — толщину стены не выбрать.");
                    continue;
                }
                // Ось x — вдоль стены a1, y = n × x — вдоль стены a2 (правая тройка с нормалью вверх).
                var (sx, sy) = n.Cross(u1).Dot(u2) >= 0 ? (inc[0], inc[1]) : (inc[1], inc[0]);
                var ax = Away(sx, p, n);
                var ay = n.Cross(ax).Normalize();
                double a1 = sx.T, a2 = sy.T;
                var o = origin - ax * (a2 / 2) - ay * (a1 / 2);
                if (Math.Abs(a1 - a2) > 1e-6)
                    warnings.Add(new("fem_wall_corner_thickness", string.Create(Ru, $"Стены угла разной толщины: a1 = {a1 * 1000:0} мм (вдоль x), a2 = {a2 * 1000:0} мм (вдоль y).")));
                var cj = Base(PunchingFemJointKind.WallCorner, ax, o, new PunchingWall(PunchingWallKind.Corner, a1, a2),
                    string.Create(Ru, $"угол стен {a1 * 1000:0}/{a2 * 1000:0} ({sx.Label})"));
                yield return Complete(cj, slab, null, !below, warnings, shells.ToHashSet(StringComparer.Ordinal));
            }
        }

        /// <summary>Единичное направление вдоль стены от конца <paramref name="end"/> внутрь отрезка, в плоскости плиты.</summary>
        static PlanarVector3 Away(WallSeg s, string end, PlanarVector3 n)
        {
            var d = s.At(s.Other(end)) - s.At(end);
            return (d - n * d.Dot(n)).Normalize();
        }

        static bool OnSegment(PlanarVector3 p, PlanarVector3 a, PlanarVector3 b, double tol)
        {
            var ab = b - a;
            double l2 = ab.Dot(ab);
            if (l2 < 1e-18) return false;
            double t = (p - a).Dot(ab) / l2;
            if (t <= 0 || t >= 1) return false;
            return (p - (a + ab * t)).Length <= tol;
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
            // Внутренняя точка площади: у стены — центр площадки передачи нагрузки (край может проходить через начало осей).
            var inner = joint.Wall is { } jw ? PunchingWallGeometry.LoadCenter(jw) : default;
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
                    if (edge.SignedDistance(inner) <= 0) continue;
                    // У стены края — только со сторон, где их допускает п. 6.3.8: у торца — за торцом и вдоль стены,
                    // у угла — снаружи (с прочих сторон край отделён стеной и на контур не влияет).
                    if (joint.Wall is { } w && !WallEdgeSide(w, SideOf(edge.InwardNormal))) continue;
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

    /// <summary>Край с этой стороны узла стены учитывается: у торца — кроме −x (за стеной), у угла — только −x и −y.</summary>
    static bool WallEdgeSide(PunchingWall wall, PlanPoint side)
        => wall.Kind == PunchingWallKind.End ? side.X > -0.5 : side.X < -0.5 || side.Y < -0.5;

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
