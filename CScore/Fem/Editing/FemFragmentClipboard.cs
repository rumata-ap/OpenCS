using System.Text.Json;

namespace CScore.Fem.Editing;

public sealed record FemFragmentSnapshot(
    IReadOnlyList<FemNode> Nodes,
    IReadOnlyList<FemMember> Members,
    IReadOnlyList<FemMemberGroup> MemberGroups);

/// <summary>Копирует выбранный фрагмент схемы (без узловых нагрузок) для последующей вставки в ту же схему.</summary>
public static class FemFragmentClipboard
{
    public static FemFragmentSnapshot Copy(
        FemSchemaEditSession session,
        IReadOnlySet<string> nodeTags,
        IReadOnlySet<string> memberTags)
    {
        var members = session.Members.Where(e => memberTags.Contains(e.ElemTag)).ToList();

        // Узлы, на которые ссылаются копируемые элементы, обязаны попасть во фрагмент, даже если
        // пользователь явно не выделял их в 3D — иначе вставка не сможет восстановить стержень
        // (см. PasteFragmentCommand: он ремапит NodeIdsJson через теги узлов этого фрагмента).
        var requiredNodeTags = members
            .SelectMany(e => JsonSerializer.Deserialize<int[]>(e.NodeIdsJson) ?? [])
            .Select(id => id.ToString())
            .ToHashSet(StringComparer.Ordinal);
        requiredNodeTags.UnionWith(nodeTags);

        var nodes = session.Nodes.Where(n => requiredNodeTags.Contains(n.NodeTag)).ToList();
        var groups = session.MemberGroups
            .Where(g => (JsonSerializer.Deserialize<int[]>(g.MemberTagsJson) ?? [])
                .Select(id => id.ToString())
                .All(memberTags.Contains) &&
                (JsonSerializer.Deserialize<int[]>(g.MemberTagsJson) ?? []).Length > 0)
            .ToList();
        return new FemFragmentSnapshot(nodes, members, groups);
    }
}

/// <summary>Вставляет ранее скопированный фрагмент со смещением (dx,dy,dz), выделяя новые теги узлов/элементов
/// по правилу max+1 и перекладывая внутренние ссылки. Узловые нагрузки не переносятся. Обратимо.</summary>
public sealed class PasteFragmentCommand(FemFragmentSnapshot snapshot, double dx, double dy, double dz)
    : IFemEditCommand
{
    List<FemNode> _newNodes = [];
    List<FemMember> _newMembers = [];
    List<FemMemberGroup> _newGroups = [];

    public void Do(FemSchemaEditSession session)
    {
        var nodeTagMap = new Dictionary<string, string>(StringComparer.Ordinal);
        _newNodes = [];
        foreach (var node in snapshot.Nodes)
        {
            var newTag = FemTopologyValidator.NextNodeTag(session.Nodes.Concat(_newNodes).ToList());
            nodeTagMap[node.NodeTag] = newTag;
            _newNodes.Add(new FemNode
            {
                SchemaId = session.Schema.Id,
                NodeTag = newTag, X = node.X + dx, Y = node.Y + dy, Z = node.Z + dz, DofMask = node.DofMask
            });
        }
        foreach (var n in _newNodes) session.Nodes.Add(n);

        // NodeIdsJson/MemberTagsJson хранят NodeTag/ElemTag как числа (соглашение всей кодовой базы —
        // см. FemTopologyValidator), поэтому старый тег узла/элемента — это просто id.ToString().
        var memberTagMap = new Dictionary<string, string>(StringComparer.Ordinal);
        _newMembers = [];
        foreach (var member in snapshot.Members)
        {
            var newTag = FemTopologyValidator.NextElemTag(session.Members.Concat(_newMembers).ToList());
            memberTagMap[member.ElemTag] = newTag;
            var oldIds = JsonSerializer.Deserialize<int[]>(member.NodeIdsJson) ?? [];
            var newIds = oldIds
                .Select(id => int.Parse(nodeTagMap[id.ToString()]))
                .ToArray();
            _newMembers.Add(new FemMember
            {
                SchemaId = session.Schema.Id,
                ElemTag = newTag, ElemType = member.ElemType,
                NodeIdsJson = JsonSerializer.Serialize(newIds),
                SectionTag = member.SectionTag, MaterialTag = member.MaterialTag,
                ThicknessM = member.ThicknessM,
                CrossSectionId = member.CrossSectionId,
                GjStrategy = member.GjStrategy, GjManualValue = member.GjManualValue,
                GjTorsionTaskId = member.GjTorsionTaskId
            });
        }
        foreach (var e in _newMembers) session.Members.Add(e);

        _newGroups = [];
        foreach (var group in snapshot.MemberGroups)
        {
            var oldMemberIds = JsonSerializer.Deserialize<int[]>(group.MemberTagsJson) ?? [];
            var newMemberIds = oldMemberIds
                .Select(id => int.Parse(memberTagMap[id.ToString()]))
                .ToArray();
            _newGroups.Add(new FemMemberGroup
            {
                SchemaId = session.Schema.Id,
                Tag = group.Tag + " (копия)", MemberType = group.MemberType,
                MemberTagsJson = JsonSerializer.Serialize(newMemberIds),
                PlateSectionId = group.PlateSectionId,
                DesignParamsJson = group.DesignParamsJson,
            });
        }
        foreach (var g in _newGroups) session.MemberGroups.Add(g);
    }

    public void Undo(FemSchemaEditSession session)
    {
        foreach (var g in _newGroups) session.MemberGroups.Remove(g);
        foreach (var e in _newMembers) session.Members.Remove(e);
        foreach (var n in _newNodes) session.Nodes.Remove(n);
    }
}
