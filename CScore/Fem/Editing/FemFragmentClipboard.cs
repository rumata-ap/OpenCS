using System.Text.Json;

namespace CScore.Fem.Editing;

public sealed record FemFragmentSnapshot(
    IReadOnlyList<FemNode> Nodes,
    IReadOnlyList<FemElement> Elements,
    IReadOnlyList<FemMember> Members);

/// <summary>Копирует выбранный фрагмент схемы (без узловых нагрузок) для последующей вставки в ту же схему.</summary>
public static class FemFragmentClipboard
{
    public static FemFragmentSnapshot Copy(
        FemSchemaEditSession session,
        IReadOnlySet<string> nodeTags,
        IReadOnlySet<string> elemTags)
    {
        var nodes = session.Nodes.Where(n => nodeTags.Contains(n.NodeTag)).ToList();
        var elements = session.Elements.Where(e => elemTags.Contains(e.ElemTag)).ToList();
        var members = session.Members
            .Where(m => (JsonSerializer.Deserialize<int[]>(m.ElemIdsJson) ?? [])
                .Select(id => id.ToString())
                .All(elemTags.Contains) &&
                (JsonSerializer.Deserialize<int[]>(m.ElemIdsJson) ?? []).Length > 0)
            .ToList();
        return new FemFragmentSnapshot(nodes, elements, members);
    }
}

/// <summary>Вставляет ранее скопированный фрагмент со смещением (dx,dy,dz), выделяя новые теги узлов/элементов
/// по правилу max+1 и перекладывая внутренние ссылки. Узловые нагрузки не переносятся. Обратимо.</summary>
public sealed class PasteFragmentCommand(FemFragmentSnapshot snapshot, double dx, double dy, double dz)
    : IFemEditCommand
{
    List<FemNode> _newNodes = [];
    List<FemElement> _newElements = [];
    List<FemMember> _newMembers = [];

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
                NodeTag = newTag, X = node.X + dx, Y = node.Y + dy, Z = node.Z + dz, DofMask = node.DofMask
            });
        }
        foreach (var n in _newNodes) session.Nodes.Add(n);

        var elemTagMap = new Dictionary<string, string>(StringComparer.Ordinal);
        _newElements = [];
        var nodeByOldTag = snapshot.Nodes.ToDictionary(n => n.NodeTag, n => n);
        var nodeIdByNewTag = session.Nodes.ToDictionary(n => n.NodeTag, n => n.Id);
        foreach (var element in snapshot.Elements)
        {
            var newTag = FemTopologyValidator.NextElemTag(session.Elements.Concat(_newElements).ToList());
            elemTagMap[element.ElemTag] = newTag;
            var oldIds = JsonSerializer.Deserialize<int[]>(element.NodeIdsJson) ?? [];
            var newIds = oldIds
                .Select(id => nodeByOldTag.Values.First(n => n.Id == id).NodeTag)
                .Select(oldTag => nodeIdByNewTag[nodeTagMap[oldTag]])
                .ToArray();
            _newElements.Add(new FemElement
            {
                ElemTag = newTag, ElemType = element.ElemType,
                NodeIdsJson = JsonSerializer.Serialize(newIds),
                SectionTag = element.SectionTag, MaterialTag = element.MaterialTag,
                ThicknessM = element.ThicknessM
            });
        }
        foreach (var e in _newElements) session.Elements.Add(e);

        _newMembers = [];
        foreach (var member in snapshot.Members)
        {
            var oldElemIds = JsonSerializer.Deserialize<int[]>(member.ElemIdsJson) ?? [];
            var elemIdByNewTag = session.Elements.ToDictionary(e => e.ElemTag, e => e.Id);
            var newElemIds = oldElemIds
                .Select(id => snapshot.Elements.First(e => e.Id == id).ElemTag)
                .Select(oldTag => elemIdByNewTag[elemTagMap[oldTag]])
                .ToArray();
            _newMembers.Add(new FemMember
            {
                Tag = member.Tag + " (копия)", MemberType = member.MemberType,
                ElemIdsJson = JsonSerializer.Serialize(newElemIds),
                CrossSectionId = member.CrossSectionId, PlateSectionId = member.PlateSectionId,
                DesignParamsJson = member.DesignParamsJson,
                GjStrategy = member.GjStrategy, GjManualValue = member.GjManualValue,
                GjTorsionTaskId = member.GjTorsionTaskId
            });
        }
        foreach (var m in _newMembers) session.Members.Add(m);
    }

    public void Undo(FemSchemaEditSession session)
    {
        foreach (var m in _newMembers) session.Members.Remove(m);
        foreach (var e in _newElements) session.Elements.Remove(e);
        foreach (var n in _newNodes) session.Nodes.Remove(n);
    }
}
