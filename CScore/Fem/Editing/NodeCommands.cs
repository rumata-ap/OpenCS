using System.Text.Json;

namespace CScore.Fem.Editing;

public sealed class AddNodeCommand(FemNode node) : IFemEditCommand
{
    public void Do(FemSchemaEditSession session)
    {
        if (node.Id == 0) node.Id = session.AllocateTemporaryNodeId();
        session.Nodes.Add(node);
    }
    public void Undo(FemSchemaEditSession session) => session.Nodes.Remove(node);
}

public sealed class MoveNodeCommand(FemNode node, double x, double y, double z) : IFemEditCommand
{
    double _oldX, _oldY, _oldZ;

    public void Do(FemSchemaEditSession session)
    {
        _oldX = node.X; _oldY = node.Y; _oldZ = node.Z;
        node.X = x; node.Y = y; node.Z = z;
    }

    public void Undo(FemSchemaEditSession session)
    {
        node.X = _oldX; node.Y = _oldY; node.Z = _oldZ;
    }
}

public sealed class SetDofMaskCommand(FemNode node, int mask) : IFemEditCommand
{
    int _oldMask;

    public void Do(FemSchemaEditSession session)
    {
        _oldMask = node.DofMask;
        node.DofMask = mask;
    }

    public void Undo(FemSchemaEditSession session) => node.DofMask = _oldMask;
}

/// <summary>Удаляет узел и каскадно — ссылающиеся на него конструктивные элементы, узловые нагрузки
/// и ссылки на удалённые элементы в FemMemberGroup.MemberTagsJson. Полностью обратимо.</summary>
public sealed class DeleteNodeCommand(FemNode node) : IFemEditCommand
{
    List<FemMember>   _removedMembers = [];
    List<FemNodeLoad> _removedLoads   = [];
    List<(FemMemberGroup group, string oldJson)> _groupEdits = [];

    public void Do(FemSchemaEditSession session)
    {
        session.Nodes.Remove(node);

        // NodeIdsJson хранит NodeTag узла как число, не БД-Id (см. FemTopologyValidator).
        int nodeTagId = int.Parse(node.NodeTag);
        _removedMembers = session.Members
            .Where(e => (JsonSerializer.Deserialize<int[]>(e.NodeIdsJson) ?? []).Contains(nodeTagId))
            .ToList();
        foreach (var e in _removedMembers) session.Members.Remove(e);

        _removedLoads = session.NodeLoads.Where(l => l.NodeId == node.Id).ToList();
        foreach (var l in _removedLoads) session.NodeLoads.Remove(l);

        var removedMemberTags = _removedMembers.Select(e => e.ElemTag).ToHashSet(StringComparer.Ordinal);
        _groupEdits = [];
        foreach (var group in session.MemberGroups)
        {
            var ids = JsonSerializer.Deserialize<int[]>(group.MemberTagsJson) ?? [];
            var kept = ids.Where(id => !removedMemberTags.Contains(id.ToString())).ToArray();
            if (kept.Length == ids.Length) continue;
            _groupEdits.Add((group, group.MemberTagsJson));
            group.MemberTagsJson = JsonSerializer.Serialize(kept);
        }
    }

    public void Undo(FemSchemaEditSession session)
    {
        session.Nodes.Add(node);
        foreach (var e in _removedMembers) session.Members.Add(e);
        foreach (var l in _removedLoads) session.NodeLoads.Add(l);
        foreach (var (group, oldJson) in _groupEdits) group.MemberTagsJson = oldJson;
    }
}

/// <summary>Сшивает узлы конструктивного слоя, совпадающие по координатам (в пределах допуска
/// FemMeshDiscretizer.CollinearToleranceM), в один узел на группу. Выживает узел с наименьшим
/// числовым NodeTag; закрепления объединяются по ИЛИ; ссылки стержней переписываются на
/// выжившего; узловые нагрузки переносятся (суммируясь при совпадении LoadCaseId). Полностью
/// обратима. Узлы с нечисловым NodeTag в слиянии не участвуют.</summary>
public sealed class MergeCoincidentNodesCommand : IFemEditCommand
{
    /// <summary>Одна слитая группа: выживший тег + теги влитых в него узлов.</summary>
    public sealed record MergedGroup(string SurvivorTag, IReadOnlyList<string> MergedTags);

    /// <summary>Результат последнего Do() — пусто, если совпадающих узлов не найдено.</summary>
    public IReadOnlyList<MergedGroup> LastResult { get; private set; } = [];

    sealed record DofMaskEdit(FemNode Node, int OldDofMask);
    sealed record MemberEdit(FemMember Member, string OldNodeIdsJson);
    sealed record LoadReassign(FemNodeLoad Load, int OldNodeId);
    sealed record LoadMerge(
        FemNodeLoad SurvivingLoad, FemNodeLoad RemovedLoad,
        double OldFx, double OldFy, double OldFz, double OldMx, double OldMy, double OldMz);

    List<FemNode> _removedNodes = [];
    List<DofMaskEdit> _dofMaskEdits = [];
    List<MemberEdit> _memberEdits = [];
    List<LoadReassign> _loadReassigns = [];
    List<LoadMerge> _loadMerges = [];

    public void Do(FemSchemaEditSession session)
    {
        _removedNodes = [];
        _dofMaskEdits = [];
        _memberEdits = [];
        _loadReassigns = [];
        _loadMerges = [];
        var report = new List<MergedGroup>();

        foreach (var group in GroupCoincidentNodes(session.Nodes))
        {
            if (group.Count < 2) continue;

            var survivor = group.OrderBy(n => int.Parse(n.NodeTag)).First();
            var removed = group.Where(n => n != survivor).ToList();

            var mergedMask = group.Aggregate(0, (mask, n) => mask | n.DofMask);
            if (mergedMask != survivor.DofMask)
            {
                _dofMaskEdits.Add(new DofMaskEdit(survivor, survivor.DofMask));
                survivor.DofMask = mergedMask;
            }

            var removedTagIds = removed.Select(n => int.Parse(n.NodeTag)).ToHashSet();
            var survivorTagId = int.Parse(survivor.NodeTag);
            foreach (var member in session.Members)
            {
                var ids = JsonSerializer.Deserialize<int[]>(member.NodeIdsJson) ?? [];
                if (!ids.Any(removedTagIds.Contains)) continue;
                _memberEdits.Add(new MemberEdit(member, member.NodeIdsJson));
                var newIds = ids.Select(id => removedTagIds.Contains(id) ? survivorTagId : id).ToArray();
                member.NodeIdsJson = JsonSerializer.Serialize(newIds);
            }

            foreach (var node in removed)
            {
                foreach (var load in session.NodeLoads.Where(l => l.NodeId == node.Id).ToList())
                {
                    var existing = session.NodeLoads.FirstOrDefault(l =>
                        l.NodeId == survivor.Id && l.LoadCaseId == load.LoadCaseId);
                    if (existing != null)
                    {
                        _loadMerges.Add(new LoadMerge(existing, load,
                            existing.Fx, existing.Fy, existing.Fz, existing.Mx, existing.My, existing.Mz));
                        existing.Fx += load.Fx; existing.Fy += load.Fy; existing.Fz += load.Fz;
                        existing.Mx += load.Mx; existing.My += load.My; existing.Mz += load.Mz;
                        session.NodeLoads.Remove(load);
                    }
                    else
                    {
                        _loadReassigns.Add(new LoadReassign(load, load.NodeId));
                        load.NodeId = survivor.Id;
                    }
                }

                session.Nodes.Remove(node);
                _removedNodes.Add(node);
            }

            report.Add(new MergedGroup(survivor.NodeTag, removed.Select(n => n.NodeTag).ToList()));
        }

        LastResult = report;
    }

    public void Undo(FemSchemaEditSession session)
    {
        foreach (var reassign in _loadReassigns)
            reassign.Load.NodeId = reassign.OldNodeId;

        foreach (var merge in _loadMerges)
        {
            merge.SurvivingLoad.Fx = merge.OldFx; merge.SurvivingLoad.Fy = merge.OldFy; merge.SurvivingLoad.Fz = merge.OldFz;
            merge.SurvivingLoad.Mx = merge.OldMx; merge.SurvivingLoad.My = merge.OldMy; merge.SurvivingLoad.Mz = merge.OldMz;
            session.NodeLoads.Add(merge.RemovedLoad);
        }

        // Восстанавливаем ссылки стержней в обратном порядке — один стержень мог быть переписан
        // дважды (оба конца оказались в разных слитых группах), и только реверс корректно
        // отматывает такую цепочку правок.
        for (int i = _memberEdits.Count - 1; i >= 0; i--)
            _memberEdits[i].Member.NodeIdsJson = _memberEdits[i].OldNodeIdsJson;

        foreach (var node in _removedNodes)
            session.Nodes.Add(node);

        foreach (var edit in _dofMaskEdits)
            edit.Node.DofMask = edit.OldDofMask;
    }

    static List<List<FemNode>> GroupCoincidentNodes(IReadOnlyList<FemNode> nodes)
    {
        var candidates = nodes
            .Select((node, index) => (node, index))
            .Where(t => int.TryParse(t.node.NodeTag, out _))
            .ToList();

        var parent = new int[candidates.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int i) => parent[i] == i ? i : (parent[i] = Find(parent[i]));
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }

        for (int i = 0; i < candidates.Count; i++)
            for (int j = i + 1; j < candidates.Count; j++)
                if (Distance(candidates[i].node, candidates[j].node) <= FemMeshDiscretizer.CollinearToleranceM)
                    Union(i, j);

        return Enumerable.Range(0, candidates.Count)
            .GroupBy(Find)
            .Select(g => g.Select(i => candidates[i].node).ToList())
            .ToList();
    }

    static double Distance(FemNode a, FemNode b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
