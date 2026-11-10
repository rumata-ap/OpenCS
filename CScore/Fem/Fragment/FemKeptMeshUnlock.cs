using System.Globalization;
using CScore.Fem.Loads;
using CScore.Planar;

namespace CScore.Fem.Fragment;

/// <summary>Данные фрагмента для снятия замка сетки с КонЭ: конструктивный уровень, сохранённая сетка, ГУ, тела,
/// нагрузки сеточного уровня и теги КЭ опорной сетки (<see cref="FragmentRefElement"/>), уже записанные у фрагмента.</summary>
public sealed record FemKeptMeshUnlockInput(
    int SchemaId,
    IReadOnlyList<FemNode> Nodes,
    IReadOnlyList<FemMember> Members,
    IReadOnlyList<FemMeshNode> MeshNodes,
    IReadOnlyList<FemElement> MeshElements,
    IReadOnlyList<FemMeshNodeSupport> MeshNodeSupports,
    IReadOnlyList<FemSpring> Springs,
    IReadOnlyList<FemRigidBody> RigidBodies,
    IReadOnlyList<FemElementLoad> ElementLoads,
    IReadOnlyList<FemMeshNodeLoad> MeshNodeLoads,
    IReadOnlySet<string> RefElementTags);

/// <summary>
/// Итог планирования снятия замка (<see cref="FemKeptMeshUnlock.Plan"/>): вход сборки сетки без снятых КонЭ и изменения
/// данных схемы, которые записываются вместе с прошедшей проверку сеткой. Ни один объект входа не изменён.
/// </summary>
public sealed class FemKeptMeshUnlockPlan
{
    /// <summary>КонЭ, с которых снят замок (сетка — <see cref="FemMember.MeshSourceGenerated"/>).</summary>
    public SortedSet<string> UnlockedMemberTags { get; } = new(StringComparer.Ordinal);
    /// <summary>Все КонЭ схемы; снятые — копии с <c>generated</c>.</summary>
    public List<FemMember> Members { get; } = [];
    /// <summary>Все узлы схемы: прежние (с изменённой маской — копии) и новые.</summary>
    public List<FemNode> Nodes { get; } = [];
    /// <summary>Новые узлы схемы (свободные, в точках ГУ и нагрузок удаляемых узлов сетки).</summary>
    public List<FemNode> NewNodes { get; } = [];
    /// <summary>Новые маски закреплений существующих узлов схемы по тегу.</summary>
    public Dictionary<string, int> NodeMasks { get; } = new(StringComparer.Ordinal);
    /// <summary>Оставшаяся сохранённая сетка.</summary>
    public List<FemMeshNode> MeshNodes { get; } = [];
    public List<FemElement> MeshElements { get; } = [];
    public SortedSet<string> RemovedNodeTags { get; } = new(StringComparer.Ordinal);
    public SortedSet<string> RemovedElementTags { get; } = new(StringComparer.Ordinal);
    /// <summary>Тела, ведущий или ведомый которых — удаляемый узел (удаляются по Id).</summary>
    public List<FemRigidBody> RemovedRigidBodies { get; } = [];
    /// <summary>Узловые нагрузки на узлы схемы (тег узла; загружение — Id фрагмента).</summary>
    public List<FragmentNodeLoad> NodeLoads { get; } = [];
    /// <summary>Пружины узлов схемы (<see cref="FemSpringTargetKinds.Node"/>).</summary>
    public List<FemSpring> NodeSprings { get; } = [];
    /// <summary>Нагрузки КонЭ-стержней (тег КонЭ).</summary>
    public List<FragmentMemberLoad> MemberLoads { get; } = [];
    /// <summary>Нагрузки на КЭ, задевшие удаляемые КЭ: удаляются по Id, взамен — <see cref="AddedElementLoads"/>.</summary>
    public List<FemElementLoad> ReplacedElementLoads { get; } = [];
    /// <summary>Остатки целей заменённых нагрузок и подъёмы на КонЭ.</summary>
    public List<FemElementLoad> AddedElementLoads { get; } = [];
    public List<FragmentFieldLoad> FieldLoads { get; } = [];
    /// <summary>КЭ опорной сетки, которых у фрагмента ещё нет (по ним разрешаются <see cref="FieldLoads"/>).</summary>
    public List<FragmentRefElement> RefElements { get; } = [];
    public List<FemValidationDiagnostic> Diagnostics { get; } = [];
    public bool IsEmpty => UnlockedMemberTags.Count == 0;
}

/// <summary>
/// Снятие замка сохранённой сетки с КонЭ фрагмента (спека «сохранение сетки родителя», разд. 5): КЭ снятых КонЭ и узлы,
/// на которые больше не ссылается ни один оставшийся КЭ, удаляются; ГУ, нагрузки и тела на них переносятся так же, как
/// построитель переносит их у обрезанных КонЭ: закрепления, пружины и узловые нагрузки — в узел схемы в той же точке
/// (существующий или новый свободный — сетка его встроит), нагрузки на КЭ пластин — на КонЭ или полем по опорной сетке
/// (<see cref="FragmentFieldLoadResolver"/>), на КЭ стержней — нагрузками КонЭ; тела на удаляемых узлах удаляются.
/// </summary>
public static class FemKeptMeshUnlock
{
    const double Tol = FemMeshDiscretizer.CollinearToleranceM;

    public static FemKeptMeshUnlockPlan Plan(FemKeptMeshUnlockInput input, IEnumerable<string> memberTags)
    {
        ArgumentNullException.ThrowIfNull(input);
        var plan = new FemKeptMeshUnlockPlan();
        var requested = memberTags.ToHashSet(StringComparer.Ordinal);
        var noRegion = new List<string>();
        foreach (var m in input.Members)
        {
            if (!requested.Contains(m.ElemTag) || !m.IsMeshLocked) { plan.Members.Add(m); continue; }
            if (m.ElemType != "beam" && m.PlanarRegionId is null) { noRegion.Add(m.ElemTag); plan.Members.Add(m); continue; }
            var copy = m.ShallowCopy();
            copy.MeshSource = FemMember.MeshSourceGenerated;
            plan.Members.Add(copy);
            plan.UnlockedMemberTags.Add(m.ElemTag);
        }
        if (noRegion.Count > 0)
            plan.Diagnostics.Add(new("fragment_mesh_unlock_no_region",
                $"Сетку КонЭ {Join(noRegion)} нельзя перестроить: у пластины нет области — сохранённая сетка оставлена.", false, [.. noRegion]));
        if (plan.IsEmpty)
        {
            plan.Nodes.AddRange(input.Nodes);
            plan.MeshNodes.AddRange(input.MeshNodes);
            plan.MeshElements.AddRange(input.MeshElements);
            return plan;
        }

        // КЭ снятых КонЭ и узлы, на которые ссылаются только они.
        var removed = new List<FemElement>();
        foreach (var e in input.MeshElements)
        {
            if (e.Origin == FemMember.MeshSourceImported && e.SourceMemberTag is { } t && plan.UnlockedMemberTags.Contains(t))
            {
                removed.Add(e);
                plan.RemovedElementTags.Add(e.ElemTag);
            }
            else plan.MeshElements.Add(e);
        }
        var used = plan.MeshElements.SelectMany(NodeTags).ToHashSet(StringComparer.Ordinal);
        foreach (var tag in removed.SelectMany(NodeTags))
            if (!used.Contains(tag)) plan.RemovedNodeTags.Add(tag);
        plan.MeshNodes.AddRange(input.MeshNodes.Where(n => !plan.RemovedNodeTags.Contains(n.NodeTag)));

        var ctx = new Ctx(input, plan);
        TransferBoundary(ctx);
        TransferRigidBodies(ctx);
        TransferNodeLoads(ctx);
        TransferElementLoads(ctx, removed);
        ctx.Finish();
        return plan;
    }

    static IEnumerable<string> NodeTags(FemElement e) => FemMemberGroup.ParseTags(e.NodeIdsJson);

    static string Join(IEnumerable<string> tags)
    {
        var list = tags.Distinct(StringComparer.Ordinal).ToList();
        string head = string.Join(", ", list.Take(10).Select(t => $"«{t}»"));
        return list.Count > 10 ? $"{head} и ещё {list.Count - 10}" : head;
    }

    static string Short(IEnumerable<string> tags)
    {
        var list = tags.Distinct(StringComparer.Ordinal).ToList();
        string head = string.Join(", ", list.Take(10));
        return list.Count > 10 ? $"{head} и ещё {list.Count - 10}" : head;
    }

    /// <summary>Состояние планирования: узлы схемы по точке, новые теги, сетка до снятия (геометрия удаляемых КЭ).</summary>
    sealed class Ctx
    {
        public readonly FemKeptMeshUnlockInput Input;
        public readonly FemKeptMeshUnlockPlan Plan;
        public readonly FemLoadMeshContext Mesh;
        public readonly Dictionary<string, FemMeshNode> MeshNodeByTag;
        readonly Dictionary<string, FemNode> _nodeByTag = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> _mask = new(StringComparer.Ordinal);
        readonly List<FemNode> _all;
        int _nextTag;

        public Ctx(FemKeptMeshUnlockInput input, FemKeptMeshUnlockPlan plan)
        {
            Input = input;
            Plan = plan;
            Mesh = new FemLoadMeshContext(input.MeshNodes, input.MeshElements);
            MeshNodeByTag = input.MeshNodes.GroupBy(n => n.NodeTag).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            _all = [.. input.Nodes];
            foreach (var n in input.Nodes) _nodeByTag.TryAdd(n.NodeTag, n);
            _nextTag = input.Nodes.Select(n => Int(n.NodeTag)).Concat(input.MeshNodes.Select(n => Int(n.NodeTag)))
                .DefaultIfEmpty(0).Max() + 1;
        }

        static int Int(string tag) => int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int t) ? t : 0;

        /// <summary>Узел схемы в точке удаляемого узла сетки: по <see cref="FemMeshNode.SourceNodeTag"/>, по координатам,
        /// иначе новый свободный.</summary>
        public FemNode NodeFor(FemMeshNode meshNode)
        {
            var p = new PlanarVector3(meshNode.X, meshNode.Y, meshNode.Z);
            if (meshNode.SourceNodeTag is { } src && _nodeByTag.TryGetValue(src, out var bySource) && Distance(bySource, p) <= Tol)
                return bySource;
            return NodeAt(p);
        }

        public FemNode NodeAt(PlanarVector3 p)
        {
            foreach (var n in _all)
                if (Distance(n, p) <= Tol) return n;
            var node = new FemNode
            {
                SchemaId = Input.SchemaId, NodeTag = (_nextTag++).ToString(CultureInfo.InvariantCulture), X = p.X, Y = p.Y, Z = p.Z,
            };
            _all.Add(node);
            _nodeByTag[node.NodeTag] = node;
            Plan.NewNodes.Add(node);
            return node;
        }

        public void AddMask(FemNode node, int mask) =>
            _mask[node.NodeTag] = (_mask.TryGetValue(node.NodeTag, out int m) ? m : node.DofMask) | mask;

        static double Distance(FemNode n, PlanarVector3 p)
        {
            double dx = n.X - p.X, dy = n.Y - p.Y, dz = n.Z - p.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>Узлы схемы плана: новые маски — на копиях (вход не меняется).</summary>
        public void Finish()
        {
            var isNew = Plan.NewNodes.ToHashSet(ReferenceEqualityComparer.Instance);
            foreach (var n in _all)
            {
                if (!_mask.TryGetValue(n.NodeTag, out int mask) || mask == n.DofMask) { Plan.Nodes.Add(n); continue; }
                if (isNew.Contains(n)) { n.DofMask = mask; Plan.Nodes.Add(n); continue; }
                Plan.NodeMasks[n.NodeTag] = mask;
                Plan.Nodes.Add(new FemNode { Id = n.Id, SchemaId = n.SchemaId, NodeTag = n.NodeTag, X = n.X, Y = n.Y, Z = n.Z, DofMask = mask });
            }
        }
    }

    /// <summary>Закрепления и пружины удаляемых узлов сетки → маска и пружины узла схемы в той же точке.</summary>
    static void TransferBoundary(Ctx c)
    {
        foreach (var s in c.Input.MeshNodeSupports)
        {
            if (!c.Plan.RemovedNodeTags.Contains(s.NodeTag) || (s.Mask & FemBoundaryDofs.All) == 0
                || !c.MeshNodeByTag.TryGetValue(s.NodeTag, out var n)) continue;
            c.AddMask(c.NodeFor(n), s.Mask & FemBoundaryDofs.All);
        }
        var byNode = new Dictionary<string, FemSpring>(StringComparer.Ordinal);
        foreach (var s in c.Input.Springs)
        {
            if (s.TargetKind != FemSpringTargetKinds.MeshNode || !c.Plan.RemovedNodeTags.Contains(s.NodeTag) || s.ActiveMask == 0
                || !c.MeshNodeByTag.TryGetValue(s.NodeTag, out var n)) continue;
            var node = c.NodeFor(n);
            if (byNode.TryGetValue(node.NodeTag, out var sum))
            {
                var k = sum.Stiffnesses;
                var add = s.Stiffnesses;
                for (int i = 0; i < 6; i++) k[i] += add[i];
                sum.SetStiffnesses(k);
                continue;
            }
            var copy = new FemSpring
            {
                SchemaId = c.Input.SchemaId, TargetKind = FemSpringTargetKinds.Node, NodeTag = node.NodeTag, Origin = s.Origin,
                SourceElemTag = s.SourceElemTag,
            };
            copy.SetStiffnesses(s.Stiffnesses);
            byNode[node.NodeTag] = copy;
            c.Plan.NodeSprings.Add(copy);
        }
    }

    /// <summary>Тела на удаляемых узлах удаляются; авто-АЖТ перестраиваемой области сборка строит заново.</summary>
    static void TransferRigidBodies(Ctx c)
    {
        foreach (var b in c.Input.RigidBodies)
            if (c.Plan.RemovedNodeTags.Contains(b.MasterNodeTag) || b.SlaveNodeTags.Any(c.Plan.RemovedNodeTags.Contains))
                c.Plan.RemovedRigidBodies.Add(b);
        if (c.Plan.RemovedRigidBodies.Count == 0) return;
        var origins = c.Plan.RemovedRigidBodies.GroupBy(b => b.Origin, StringComparer.Ordinal)
            .Select(g => $"{g.Key} — {g.Count()}");
        c.Plan.Diagnostics.Add(new("fragment_mesh_unlock_rigid_removed",
            $"АЖТ на узлах перестраиваемой сетки удалены: {c.Plan.RemovedRigidBodies.Count} ({string.Join(", ", origins)}); " +
            "автоматические АЖТ перестроенных областей строятся заново.", false,
            [.. c.Plan.RemovedRigidBodies.Select(b => b.MasterNodeTag).Distinct(StringComparer.Ordinal)]));
    }

    static void TransferNodeLoads(Ctx c)
    {
        foreach (var l in c.Input.MeshNodeLoads)
        {
            if (!c.Plan.RemovedNodeTags.Contains(l.MeshNodeTag) || !c.MeshNodeByTag.TryGetValue(l.MeshNodeTag, out var n)) continue;
            c.Plan.NodeLoads.Add(new FragmentNodeLoad(c.NodeFor(n).NodeTag, new FemNodeLoad
            {
                LoadCaseId = l.LoadCaseId, Fx = l.Fx, Fy = l.Fy, Fz = l.Fz, Mx = l.Mx, My = l.My, Mz = l.Mz,
            }));
        }
    }

    /// <summary>
    /// Нагрузки на удаляемые КЭ: цель «КЭ» теряет их теги (остаток — копия), сами нагрузки — на КонЭ, полем или нагрузкой
    /// КонЭ-стержня; не перенесённое — предупреждение.
    /// </summary>
    static void TransferElementLoads(Ctx c, List<FemElement> removed)
    {
        var removedByMember = removed.GroupBy(e => e.SourceMemberTag!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ElemTag).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        var lost = new List<string>();
        var field = new Dictionary<(int LoadCase, string Kind, string Axis), List<FragmentFieldEntry>>();
        var fieldRefs = new SortedSet<string>(StringComparer.Ordinal);
        var members = c.Input.Members.GroupBy(m => m.ElemTag, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var nodes = c.Input.Nodes.GroupBy(n => n.NodeTag, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var load in c.Input.ElementLoads)
        {
            if (load.TargetKind != FemLoadTargetKinds.Elements) continue;
            var hit = load.TargetTags.Where(c.Plan.RemovedElementTags.Contains).ToList();
            if (hit.Count == 0) continue;
            c.Plan.ReplacedElementLoads.Add(load);
            var rest = load.TargetTags.Where(t => !c.Plan.RemovedElementTags.Contains(t)).ToList();
            if (rest.Count > 0) c.Plan.AddedElementLoads.Add(Copy(load, rest));

            foreach (var group in hit.GroupBy(t => c.Mesh.ElementsByTag[t].SourceMemberTag!, StringComparer.Ordinal))
            {
                var tags = group.ToList();
                bool whole = removedByMember[group.Key].SetEquals(tags);
                var v = load.Values;
                if (load.LoadKind == FemElementLoadKinds.SelfWeight)
                {
                    if (whole && v.Count >= 1) c.Plan.AddedElementLoads.Add(Lift(load, group.Key, FemElementLoadKinds.SelfWeight, "z", v[0]));
                    else lost.AddRange(tags);
                    continue;
                }
                var uniform = new List<(string Tag, PlanarVector3 Q)>();
                foreach (var tag in tags)
                {
                    var e = c.Mesh.ElementsByTag[tag];
                    if (c.Mesh.Geometry(e) is not { } g || FemElementLoadNodalizer.Direction(load, g, c.Mesh.BarRotationDeg(e)) is not { } dir)
                    {
                        lost.Add(tag);
                        continue;
                    }
                    if (g.IsBar)
                    {
                        if (!BarLoad(c, load, e, g, dir, members, nodes)) lost.Add(tag);
                        continue;
                    }
                    switch (load.LoadKind)
                    {
                        case FemElementLoadKinds.Uniform when v.Count >= 1:
                            uniform.Add((tag, dir * v[0]));
                            break;
                        case FemElementLoadKinds.Nodal when v.Count == g.Points.Count && v.All(x => Math.Abs(x - v[0]) <= 1e-12 * Math.Max(1, Math.Abs(v[0]))):
                            uniform.Add((tag, dir * v[0]));
                            break;
                        case FemElementLoadKinds.Nodal when v.Count == g.Points.Count:
                            Field(field, load.LoadCaseId, FemElementLoadKinds.Nodal, dir, tag, d => v.Select(x => x * d).ToArray());
                            fieldRefs.Add(tag);
                            break;
                        case FemElementLoadKinds.Edge when v.Count >= 4 && FemElementLoadNodalizer.ShellEdge(g, v[0], v[1]) is not null:
                            Field(field, load.LoadCaseId, FemElementLoadKinds.Edge, dir, tag, d => [v[0], v[1], v[2] * d, v[3] * d]);
                            fieldRefs.Add(tag);
                            break;
                        case FemElementLoadKinds.Point when v.Count >= 3:
                        {
                            var f = dir * v[0];
                            var node = c.NodeAt(FemElementLoadNodalizer.ShellPointAt(g, v[1], v[2]));
                            c.Plan.NodeLoads.Add(new FragmentNodeLoad(node.NodeTag, new FemNodeLoad
                            {
                                LoadCaseId = load.LoadCaseId, Fx = f.X, Fy = f.Y, Fz = f.Z,
                            }));
                            break;
                        }
                        default:
                            lost.Add(tag);
                            break;
                    }
                }
                if (uniform.Count == 0) continue;
                double scale = uniform.Max(x => x.Q.Length);
                if (scale == 0) continue;
                if (whole && uniform.Count == tags.Count && uniform.All(x => (x.Q - uniform[0].Q).Length <= 1e-9 * scale))
                    foreach (var (axis, value) in Components(uniform[0].Q))
                        c.Plan.AddedElementLoads.Add(Lift(load, group.Key, FemElementLoadKinds.Uniform, axis, value));
                else
                    foreach (var (tag, q) in uniform)
                    {
                        Field(field, load.LoadCaseId, FemElementLoadKinds.Uniform, q, tag, d => [d]);
                        fieldRefs.Add(tag);
                    }
            }
        }

        foreach (var ((lc, kind, axis), entries) in field) c.Plan.FieldLoads.Add(new FragmentFieldLoad(lc, kind, axis, entries));
        var missingRefs = fieldRefs.Where(t => !c.Input.RefElementTags.Contains(t)).Select(t => c.Mesh.ElementsByTag[t]).ToList();
        c.Plan.RefElements.AddRange(FragmentRefElement.From(missingRefs, c.MeshNodeByTag));
        if (lost.Count > 0)
            c.Plan.Diagnostics.Add(new("fragment_mesh_unlock_load_lost",
                $"Нагрузки на КЭ перестраиваемой сетки не перенесены (вид не поддержан, собственный вес не на всех КЭ КонЭ, " +
                $"оси КЭ неизвестны): КЭ {Short(lost)}.", false, [.. lost.Distinct(StringComparer.Ordinal)]));
    }

    /// <summary>Нагрузка КЭ-стержня (равномерная, трапеция, сосредоточенная) → нагрузка КонЭ в глобальных осях на том
    /// же участке, по длине КонЭ от его начала.</summary>
    static bool BarLoad(Ctx c, FemElementLoad load, FemElement e, FemElementGeometry g, PlanarVector3 dir,
        Dictionary<string, FemMember> members, Dictionary<string, FemNode> nodes)
    {
        if (e.SourceMemberTag is not { } mt || !members.TryGetValue(mt, out var m)) return false;
        var ids = FemMemberGroup.ParseTags(m.NodeIdsJson);
        if (ids.Count < 2 || !nodes.TryGetValue(ids[0], out var na) || !nodes.TryGetValue(ids[^1], out var nb)) return false;
        var a = new PlanarVector3(na.X, na.Y, na.Z);
        var b = new PlanarVector3(nb.X, nb.Y, nb.Z);
        double length = (b - a).Length, len = g.Length;
        if (length < 1e-9 || len < 1e-9) return false;
        var u = (b - a) * (1 / length);
        var axis = (g.Points[1] - g.Points[0]) * (1 / len);
        if (1 - Math.Abs(axis.Dot(u)) > 1e-6) return false;
        double sign = axis.Dot(u) > 0 ? 1 : -1;
        double si = (g.Points[0] - a).Dot(u);
        var v = load.Values;
        if (load.LoadKind == FemElementLoadKinds.Point && v.Count >= 2)
        {
            var f = dir * v[0];
            c.Plan.MemberLoads.Add(new FragmentMemberLoad(mt, new FemMemberLoad
            {
                LoadCaseId = load.LoadCaseId, CoordinateSystem = "global", DistributionType = "point",
                StartOffsetM = Math.Clamp(si + sign * Math.Clamp(v[1], 0, len), 0, length),
                QxStart = f.X, QyStart = f.Y, QzStart = f.Z, QxEnd = f.X, QyEnd = f.Y, QzEnd = f.Z,
            }));
            return true;
        }
        (double A, double QA, double B, double QB)? seg = load.LoadKind switch
        {
            FemElementLoadKinds.Uniform when v.Count >= 1 => (0, v[0], len, v[0]),
            FemElementLoadKinds.Trapezoid when v.Count >= 4 => FemElementLoadNodalizer.TrapezoidOnBar(v[0], v[1], v[2], v[3], len),
            _ => null,
        };
        if (seg is not var (sa0, qa, sb0, qb)) return load.LoadKind == FemElementLoadKinds.Trapezoid;
        double sa = si + sign * sa0, sb = si + sign * sb0;
        if (sa > sb) (sa, sb, qa, qb) = (sb, sa, qb, qa);
        double x0 = Math.Max(sa, 0), x1 = Math.Min(sb, length);
        if (x1 - x0 <= 1e-9) return false;
        double Q(double s) => qa + (qb - qa) * (s - sa) / (sb - sa);
        PlanarVector3 q0 = dir * Q(x0), q1 = dir * Q(x1);
        bool uniform = (q1 - q0).Length <= 1e-12 * Math.Max(1, q0.Length);
        c.Plan.MemberLoads.Add(new FragmentMemberLoad(mt, new FemMemberLoad
        {
            LoadCaseId = load.LoadCaseId, CoordinateSystem = "global", DistributionType = uniform ? "uniform" : "trapezoidal",
            StartOffsetM = x0, EndOffsetM = Math.Max(0, length - x1),
            QxStart = q0.X, QyStart = q0.Y, QzStart = q0.Z, QxEnd = q1.X, QyEnd = q1.Y, QzEnd = q1.Z,
        }));
        return true;
    }

    static FemElementLoad Copy(FemElementLoad load, IEnumerable<string> tags)
    {
        var copy = new FemElementLoad
        {
            SchemaId = load.SchemaId, LoadCaseId = load.LoadCaseId, Origin = load.Origin, TargetKind = load.TargetKind,
            LoadKind = load.LoadKind, CoordinateSystem = load.CoordinateSystem, Axis = load.Axis,
        };
        copy.SetTargetTags(tags);
        copy.SetValues([.. load.Values]);
        return copy;
    }

    static FemElementLoad Lift(FemElementLoad load, string memberTag, string kind, string axis, double value)
    {
        var lift = new FemElementLoad
        {
            SchemaId = load.SchemaId, LoadCaseId = load.LoadCaseId, Origin = load.Origin, TargetKind = FemLoadTargetKinds.Members,
            LoadKind = kind, CoordinateSystem = "global", Axis = axis,
        };
        lift.SetTargetTags([memberTag]);
        lift.SetValues([value]);
        return lift;
    }

    /// <summary>Записи поля по глобальным осям: компонента d вектора <paramref name="direction"/> масштабирует значения.</summary>
    static void Field(Dictionary<(int, string, string), List<FragmentFieldEntry>> field, int lc, string kind, PlanarVector3 direction,
        string refTag, Func<double, double[]> values)
    {
        double eps = 1e-12 * direction.Length;
        foreach (var (axis, d) in new[] { ("x", direction.X), ("y", direction.Y), ("z", direction.Z) })
        {
            if (Math.Abs(d) <= eps) continue;
            if (!field.TryGetValue((lc, kind, axis), out var list)) field[(lc, kind, axis)] = list = [];
            list.Add(new FragmentFieldEntry(refTag, values(d)));
        }
    }

    static IEnumerable<(string Axis, double Value)> Components(PlanarVector3 q)
    {
        double eps = 1e-12 * q.Length;
        if (Math.Abs(q.X) > eps) yield return ("x", q.X);
        if (Math.Abs(q.Y) > eps) yield return ("y", q.Y);
        if (Math.Abs(q.Z) > eps) yield return ("z", q.Z);
    }
}
