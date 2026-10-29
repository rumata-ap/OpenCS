using System.Globalization;
using System.Text.Json;
using CScore.Planar;

namespace CScore.Fem;

/// <summary>Автоматические АЖТ примыканий после сборки сетки схемы и диагностика.</summary>
public sealed record FemRigidJunctionResult(List<FemRigidBody> Bodies, List<FemValidationDiagnostic> Diagnostics);

/// <summary>
/// Тела АЖТ примыканий стержней к плитам по вшитым в снимки следам (спека
/// <c>2026-10-15-fem-auto-rigid-junctions-design.md</c>, разд. 4.2–4.3, 5.3): ведомые — узлы сетки в кусках следа
/// (<see cref="PlanarConstraintMeshMapping.RegionNodeIndices"/>). Колонна — ведущий узел сетки исходного узла КонЭ (или
/// узел в точке пересечения плиты стержнем насквозь); ребро — по телу на каждый узел сетки стержня (станцию), ведомый —
/// к ближайшей по проекции на ось станции, при равенстве — к меньшей. Узел ведом только одним телом: колонны раньше
/// рёбер, внутри вида — по тегу ведущего; ведущий, попавший ведомым в другое тело, сливает тела (ведущий — колонны,
/// иначе меньший тег). Узлы с закреплениями, пружинами, линейными связями и чужими телами ведомыми не становятся.
/// </summary>
public static class FemRigidJunctionMaterializer
{
    const double Tolerance = FemMeshDiscretizer.CollinearToleranceM;

    sealed class Draft(RigidJunctionCase kind, string master)
    {
        public RigidJunctionCase Case { get; set; } = kind;
        public string Master { get; set; } = master;
        public List<string> Slaves { get; } = [];
        public SortedSet<string> Members { get; } = new(StringComparer.Ordinal);
        public int Mask { get; set; }
        public bool Coupled { get; set; } = true;
        public Draft? Parent { get; set; }
    }

    public static FemRigidJunctionResult Build(
        FemPlanarMaterializationResult mesh,
        IReadOnlyList<FemPlanarRegionMesh> regions,
        IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemMember> members,
        IReadOnlySet<string> blockedSlaves)
    {
        var diagnostics = new List<FemValidationDiagnostic>();
        var meshByTag = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var n in mesh.Nodes) meshByTag.TryAdd(n.NodeTag, n);
        var meshBySource = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var n in mesh.Nodes)
            if (n.SourceNodeTag != null) meshBySource.TryAdd(n.SourceNodeTag, n.NodeTag);
        var femByTag = nodes.GroupBy(n => n.NodeTag, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var memberByTag = members.GroupBy(m => m.ElemTag, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var drafts = new List<Draft>();
        var byMaster = new Dictionary<string, Draft>(StringComparer.Ordinal);
        var slaveOf = new Dictionary<string, Draft>(StringComparer.Ordinal);
        var blockedHits = new SortedSet<string>(TagComparer);

        Draft DraftFor(RigidJunctionCase kind, string master)
        {
            if (byMaster.TryGetValue(master, out var existing)) return existing;
            var draft = new Draft(kind, master);
            drafts.Add(draft);
            byMaster[master] = draft;
            return draft;
        }

        void AddSlave(Draft draft, string tag)
        {
            if (tag == draft.Master || slaveOf.ContainsKey(tag)) return;
            if (blockedSlaves.Contains(tag)) { blockedHits.Add(tag); return; }
            draft.Slaves.Add(tag);
            slaveOf[tag] = draft;
        }

        void Join(Draft draft, RigidJunctionTrace trace)
        {
            foreach (var m in trace.MemberTags) draft.Members.Add(m);
            draft.Mask |= (int)trace.Mask & FemBoundaryDofs.All;
            draft.Coupled &= trace.Coupled;
        }

        // Узлы сетки в кусках следа по всем областям.
        var columns = new List<(RigidJunctionTrace Trace, string Master, List<string> Nodes)>();
        var ribs = new List<(RigidJunctionTrace Trace, List<string> Nodes)>();
        for (int r = 0; r < regions.Count; r++)
        {
            if (regions[r].Junctions is not { } junctions || r >= mesh.RegionNodeTags.Count) continue;
            var tags = mesh.RegionNodeTags[r];
            if (tags.Count == 0) continue;
            var mappings = regions[r].Snapshot.ConstraintMappings
                .GroupBy(m => m.ConstraintObjectId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            foreach (var trace in junctions.Traces)
            {
                var inTrace = new List<string>();
                for (int k = 0; k < trace.Pieces.Count; k++)
                    if (mappings.TryGetValue($"{trace.Id}#{k}", out var mapping))
                        foreach (var index in mapping.RegionNodeIndices)
                            if (index >= 0 && index < tags.Count) inTrace.Add(tags[index]);
                inTrace = inTrace.Distinct(StringComparer.Ordinal).ToList();

                if (trace.Case == RigidJunctionCase.Rib)
                {
                    ribs.Add((trace, inTrace));
                    continue;
                }
                var master = ColumnMaster(trace, meshBySource, mesh.Nodes, inTrace, meshByTag);
                if (master == null)
                {
                    diagnostics.Add(new("rigid_junction_master_missing",
                        $"Ведущий узел АЖТ стержня {string.Join(", ", trace.MemberTags)} не найден в сетке — АЖТ не создаётся.",
                        false, [.. trace.MemberTags]));
                    continue;
                }
                columns.Add((trace, master, inTrace));
            }
        }

        // Колонны раньше рёбер; внутри вида — по тегу ведущего / КонЭ.
        foreach (var (trace, master, inTrace) in columns.OrderBy(c => c.Master, TagComparer))
        {
            var draft = DraftFor(RigidJunctionCase.Column, master);
            draft.Case = RigidJunctionCase.Column;
            Join(draft, trace);
            foreach (var tag in inTrace.OrderBy(t => t, TagComparer)) AddSlave(draft, tag);
        }

        var beamNodes = mesh.Elements
            .Where(e => e.ElemType == "beam" && e.SourceMemberTag != null)
            .GroupBy(e => e.SourceMemberTag!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.SelectMany(e => FemMeshTopology.ReadNodeTags(e) ?? []).Distinct(StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);
        foreach (var (trace, inTrace) in ribs.OrderBy(r => r.Trace.MemberTags[0], StringComparer.Ordinal))
        {
            string tag = trace.MemberTags[0];
            if (!memberByTag.TryGetValue(tag, out var member) || !TryAxis(member, femByTag, out var a, out var d) ||
                !beamNodes.TryGetValue(tag, out var stationTags))
            {
                diagnostics.Add(new("rigid_junction_rib_no_stations",
                    $"У ребра {tag} нет узлов сетки стержня — АЖТ полосы не создаются.", false, [tag]));
                continue;
            }
            double T(FemMeshNode n) => ((n.X - a.X) * d.X + (n.Y - a.Y) * d.Y + (n.Z - a.Z) * d.Z) / d.Dot(d);
            var stations = stationTags.Where(meshByTag.ContainsKey).Select(t => (Tag: t, T: T(meshByTag[t])))
                .OrderBy(s => s.T).ToList();
            if (stations.Count == 0) continue;
            var stationSet = stations.Select(s => s.Tag).ToHashSet(StringComparer.Ordinal);
            var assigned = stations.Select(_ => new List<string>()).ToArray();
            foreach (var node in inTrace.Where(t => !stationSet.Contains(t) && meshByTag.ContainsKey(t)))
            {
                double t = T(meshByTag[node]);
                int best = 0;
                for (int i = 1; i < stations.Count; i++)
                    if (Math.Abs(stations[i].T - t) < Math.Abs(stations[best].T - t) - 1e-9) best = i;
                assigned[best].Add(node);
            }
            for (int i = 0; i < stations.Count; i++)
            {
                var draft = DraftFor(RigidJunctionCase.Rib, stations[i].Tag);
                Join(draft, trace);
                foreach (var node in assigned[i].OrderBy(t => t, TagComparer)) AddSlave(draft, node);
            }
        }

        // Слияние: ведущий, ведомый в другом теле, сливает тела.
        Draft Root(Draft x) { while (x.Parent != null) x = x.Parent; return x; }
        foreach (var draft in drafts)
            if (slaveOf.TryGetValue(draft.Master, out var host) && Root(host) != Root(draft))
                Root(draft).Parent = Root(host);
        var bodies = new List<FemRigidBody>();
        var empty = new List<string>();
        foreach (var group in drafts.GroupBy(Root))
        {
            var chosen = group.OrderBy(d => d.Case == RigidJunctionCase.Column ? 0 : 1).ThenBy(d => d.Master, TagComparer).First();
            var memberTags = new SortedSet<string>(StringComparer.Ordinal);
            var slaves = new List<string>();
            int mask = 0;
            bool coupled = true;
            foreach (var d in group)
            {
                foreach (var m in d.Members) memberTags.Add(m);
                mask |= d.Mask;
                coupled &= d.Coupled;
                slaves.AddRange(d.Slaves);
                if (d != chosen && !slaveOf.ContainsKey(d.Master))
                {
                    if (blockedSlaves.Contains(d.Master)) blockedHits.Add(d.Master);
                    else slaves.Add(d.Master);
                }
            }
            slaves = slaves.Where(t => t != chosen.Master).Distinct(StringComparer.Ordinal).OrderBy(t => t, TagComparer).ToList();
            if (slaves.Count == 0)
            {
                if (chosen.Case == RigidJunctionCase.Column) empty.AddRange(memberTags);
                continue;
            }
            var body = new FemRigidBody
            {
                MasterNodeTag = chosen.Master,
                Mask = mask == 0 ? FemBoundaryDofs.All : mask,
                Kind = coupled ? FemRigidBodyKinds.Coupled : FemRigidBodyKinds.Rigid,
                Origin = RigidJunctionTraceSet.Provenance,
                SourceElemTag = string.Join(",", memberTags),
            };
            body.SetSlaveNodeTags(slaves);
            bodies.Add(body);
        }

        if (blockedHits.Count > 0)
            diagnostics.Add(new("rigid_junction_slave_blocked",
                $"Узлы {Short(blockedHits)} с закреплениями, пружинами, линейными связями или другими АЖТ не включены в АЖТ примыканий.",
                false, [.. blockedHits]));
        if (empty.Count > 0)
            diagnostics.Add(new("rigid_junction_no_slaves",
                $"В следе АЖТ стержней {Short(empty)} нет узлов плиты — АЖТ не создаётся (след мельче шага сетки?).", false, [.. empty]));

        return new FemRigidJunctionResult(bodies.OrderBy(b => b.MasterNodeTag, TagComparer).ToList(), diagnostics);
    }

    /// <summary>
    /// Узлы сетки, которые не могут быть ведомыми АЖТ примыканий: закреплённые (<see cref="FemNode.DofMask"/> через
    /// <see cref="FemMeshNode.SourceNodeTag"/>, закрепления сетки, в том числе от линейных связей), с пружинами и
    /// входящие в жёсткие тела другого происхождения (ручные, импортные; не по узлам схемы).
    /// </summary>
    public static HashSet<string> BlockedNodes(
        IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemMeshNode> meshNodes,
        IReadOnlyList<FemMeshNodeSupport> supports,
        IReadOnlyList<FemSpring> springs,
        IReadOnlyList<FemRigidBody> rigidBodies)
    {
        var bySource = meshNodes.Where(n => n.SourceNodeTag != null)
            .ToLookup(n => n.SourceNodeTag!, n => n.NodeTag, StringComparer.Ordinal);
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes.Where(n => (n.DofMask & FemBoundaryDofs.All) != 0))
            result.UnionWith(bySource[node.NodeTag]);
        foreach (var s in supports.Where(s => (s.Mask & FemBoundaryDofs.All) != 0)) result.Add(s.NodeTag);
        foreach (var s in springs.Where(s => s.ActiveMask != 0))
        {
            if (s.TargetKind == FemSpringTargetKinds.Node) result.UnionWith(bySource[s.NodeTag]);
            else result.Add(s.NodeTag);
        }
        // Тела, которые пересобирает сама сборка сетки (примыкания и по узлам схемы), не блокируют.
        foreach (var b in rigidBodies.Where(b => b.Origin != RigidJunctionTraceSet.Provenance && b.Origin != FemNodeRigidBodies.Provenance))
        {
            result.Add(b.MasterNodeTag);
            result.UnionWith(b.SlaveNodeTags);
        }
        return result;
    }

    /// <summary>Ведущий узел колонны: узел сетки исходного узла КонЭ, иначе узел сетки в точке пересечения.</summary>
    static string? ColumnMaster(RigidJunctionTrace trace, IReadOnlyDictionary<string, string> meshBySource,
        IReadOnlyList<FemMeshNode> meshNodes, List<string> inTrace, IReadOnlyDictionary<string, FemMeshNode> meshByTag)
    {
        if (trace.MasterNodeTag != null)
            return meshBySource.TryGetValue(trace.MasterNodeTag, out var tag) ? tag : null;
        if (trace.MasterPoint is not { } p) return null;
        bool Near(FemMeshNode n) =>
            Math.Abs(n.X - p.X) <= Tolerance && Math.Abs(n.Y - p.Y) <= Tolerance && Math.Abs(n.Z - p.Z) <= Tolerance;
        return inTrace.Where(meshByTag.ContainsKey).FirstOrDefault(t => Near(meshByTag[t]))
               ?? meshNodes.FirstOrDefault(Near)?.NodeTag;
    }

    static bool TryAxis(FemMember member, IReadOnlyDictionary<string, FemNode> femByTag, out PlanarVector3 a, out PlanarVector3 d)
    {
        a = d = default;
        int[]? ids;
        try { ids = JsonSerializer.Deserialize<int[]>(member.NodeIdsJson); }
        catch (JsonException) { return false; }
        if (ids is not { Length: 2 } ||
            !femByTag.TryGetValue(ids[0].ToString(CultureInfo.InvariantCulture), out var na) ||
            !femByTag.TryGetValue(ids[1].ToString(CultureInfo.InvariantCulture), out var nb))
            return false;
        a = new PlanarVector3(na.X, na.Y, na.Z);
        d = new PlanarVector3(nb.X, nb.Y, nb.Z) - a;
        return d.Length > Tolerance;
    }

    static readonly Comparer<string> TagComparer = Comparer<string>.Create((x, y) =>
    {
        bool nx = int.TryParse(x, out int a), ny = int.TryParse(y, out int b);
        if (nx && ny) return a.CompareTo(b);
        if (nx != ny) return nx ? -1 : 1;
        return string.CompareOrdinal(x, y);
    });

    static string Short(IEnumerable<string> tags)
    {
        var list = tags.Distinct().ToList();
        return list.Count <= 10 ? string.Join(", ", list) : string.Join(", ", list.Take(10)) + $" … (всего {list.Count})";
    }
}
