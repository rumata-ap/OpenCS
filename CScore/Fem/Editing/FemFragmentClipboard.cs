using System.Text.Json;

namespace CScore.Fem.Editing;

/// <param name="SkippedPlanarMembers">Пластин плоских областей, не вошедших во фрагмент: их геометрия хранится в
/// области (<see cref="FemMember.PlanarRegionId"/>), копия без области была бы неполной.</param>
public sealed record FemFragmentSnapshot(
    IReadOnlyList<FemNode> Nodes,
    IReadOnlyList<FemMember> Members,
    IReadOnlyList<FemMemberGroup> MemberGroups,
    int SkippedPlanarMembers = 0);

/// <summary>Копирует выбранный фрагмент схемы (без узловых нагрузок) для последующей вставки в ту же схему.</summary>
public static class FemFragmentClipboard
{
    public static FemFragmentSnapshot Copy(
        FemSchemaEditSession session,
        IReadOnlySet<string> nodeTags,
        IReadOnlySet<string> memberTags)
    {
        var selected = session.Members.Where(e => memberTags.Contains(e.ElemTag)).ToList();
        var members = selected.Where(m => m.PlanarRegionId == null).ToList();

        // Узлы, на которые ссылаются копируемые элементы, обязаны попасть во фрагмент, даже если
        // пользователь явно не выделял их в 3D — иначе вставка не сможет восстановить стержень
        // (см. PasteFragmentCommand: он ремапит NodeIdsJson через теги узлов этого фрагмента).
        var requiredNodeTags = FemFragmentTargets.NodeTagsOf(members);
        requiredNodeTags.UnionWith(nodeTags);

        var nodes = session.Nodes.Where(n => requiredNodeTags.Contains(n.NodeTag)).ToList();
        // Копируются только группы КонЭ, целиком вошедшие во фрагмент; группы КЭ сетки к фрагменту не относятся.
        var copied = members.Select(m => m.ElemTag).ToHashSet(StringComparer.Ordinal);
        var groups = session.MemberGroups
            .Where(g => !g.IsMeshGroup && g.Tags.Count > 0 && g.Tags.All(copied.Contains))
            .ToList();
        return new FemFragmentSnapshot(nodes, members, groups, selected.Count - members.Count);
    }
}

/// <summary>Что затрагивает перемещение фрагмента: узлы и элементы, геометрию которых двигать нельзя.</summary>
/// <param name="Nodes">Узлы к сдвигу: явно выбранные и узлы выбранных КонЭ.</param>
/// <param name="Blockers">КонЭ, опирающиеся на эти узлы, но не допускающие сдвига: пластины плоских областей
/// (геометрия — в области) и элементы с импортированной сеткой.</param>
public sealed record FemFragmentMove(IReadOnlyList<FemNode> Nodes, IReadOnlyList<FemMember> Blockers);

/// <summary>Состав фрагмента для перемещения и копирования.</summary>
public static class FemFragmentTargets
{
    /// <summary>Узлы к сдвигу и элементы, которые сдвиг запрещают.</summary>
    public static FemFragmentMove ForMove(
        FemSchemaEditSession session, IEnumerable<string> nodeTags, IEnumerable<string> memberTags)
    {
        var memberSet = memberTags.ToHashSet(StringComparer.Ordinal);
        var tags = NodeTagsOf(session.Members.Where(m => memberSet.Contains(m.ElemTag)));
        tags.UnionWith(nodeTags);
        var nodes = session.Nodes.Where(n => tags.Contains(n.NodeTag)).ToList();
        var blockers = session.Members
            .Where(m => (m.PlanarRegionId != null || m.IsMeshLocked) && NodeTagsOf([m]).Overlaps(tags))
            .ToList();
        return new FemFragmentMove(nodes, blockers);
    }

    /// <summary>Теги узлов элементов (в <see cref="FemMember.NodeIdsJson"/> узлы записаны тегами-числами).</summary>
    public static HashSet<string> NodeTagsOf(IEnumerable<FemMember> members) =>
        members.SelectMany(m => JsonSerializer.Deserialize<int[]>(m.NodeIdsJson) ?? [])
            .Select(id => id.ToString())
            .ToHashSet(StringComparer.Ordinal);
}

/// <summary>Вставляет ранее скопированный фрагмент <paramref name="copies"/> раз со смещением k·(dx,dy,dz),
/// k = 1…copies, выделяя новые теги узлов/элементов по правилу max+1 и перекладывая внутренние ссылки. Свойства КонЭ
/// копируются целиком, кроме привязок к конкретному элементу (набор усилий, плоская область); сетка копии строится
/// заново. Узловые нагрузки не переносятся. Одна отмена на все копии.</summary>
public sealed class PasteFragmentCommand(FemFragmentSnapshot snapshot, double dx, double dy, double dz, int copies = 1)
    : IFemEditCommand
{
    List<FemNode> _newNodes = [];
    List<FemMember> _newMembers = [];
    List<FemMemberGroup> _newGroups = [];

    public void Do(FemSchemaEditSession session)
    {
        _newNodes = [];
        _newMembers = [];
        _newGroups = [];
        for (int k = 1; k <= Math.Max(1, copies); k++)
            PasteOnce(session, k * dx, k * dy, k * dz);
    }

    void PasteOnce(FemSchemaEditSession session, double ox, double oy, double oz)
    {
        var nodeTagMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in snapshot.Nodes)
        {
            var newTag = FemTopologyValidator.NextNodeTag(session.Nodes);
            nodeTagMap[node.NodeTag] = newTag;
            var copy = new FemNode
            {
                Id = session.AllocateTemporaryNodeId(), SchemaId = session.Schema.Id,
                NodeTag = newTag, X = node.X + ox, Y = node.Y + oy, Z = node.Z + oz, DofMask = node.DofMask
            };
            session.Nodes.Add(copy);
            _newNodes.Add(copy);
        }

        // NodeIdsJson хранит NodeTag как числа (соглашение всей кодовой базы — см. FemTopologyValidator),
        // поэтому старый тег узла — это просто id.ToString().
        var memberTagMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in snapshot.Members)
        {
            var newTag = FemTopologyValidator.NextElemTag(session.Members);
            memberTagMap[member.ElemTag] = newTag;
            var oldIds = JsonSerializer.Deserialize<int[]>(member.NodeIdsJson) ?? [];
            var copy = CopyOf(member, session.Schema.Id, newTag,
                oldIds.Select(id => int.Parse(nodeTagMap[id.ToString()])).ToArray());
            session.Members.Add(copy);
            _newMembers.Add(copy);
        }

        foreach (var group in snapshot.MemberGroups)
        {
            var copy = new FemMemberGroup
            {
                SchemaId = session.Schema.Id,
                Tag = group.Tag + " (копия)", MemberType = group.MemberType,
                Kind = FemMemberGroup.KindMembers, Origin = FemMemberGroup.OriginManual,
                PlateSectionId = group.PlateSectionId,
                DesignParamsJson = group.DesignParamsJson,
            };
            copy.SetTags(group.Tags.Select(t => memberTagMap[t]));
            session.MemberGroups.Add(copy);
            _newGroups.Add(copy);
        }
    }

    /// <summary>Копия КонЭ с новыми тегом и узлами.</summary>
    static FemMember CopyOf(FemMember member, int schemaId, string tag, int[] nodeIds) => new()
    {
        SchemaId = schemaId,
        ElemTag = tag,
        ElemType = member.ElemType,
        NodeIdsJson = JsonSerializer.Serialize(nodeIds),
        SectionTag = member.SectionTag,
        MaterialTag = member.MaterialTag,
        ThicknessM = member.ThicknessM,
        CrossSectionId = member.CrossSectionId,
        GjStrategy = member.GjStrategy,
        GjManualValue = member.GjManualValue,
        GjTorsionTaskId = member.GjTorsionTaskId,
        TargetMeshLengthM = member.TargetMeshLengthM,
        PlateSectionId = member.PlateSectionId,
        DesignParamsJson = member.DesignParamsJson,
        RotationDeg = member.RotationDeg,
        Kind = member.Kind,
        KindSource = member.KindSource,
        // Набор усилий получен для исходного элемента; область и импортированная сетка у копии свои (нет).
        ForceSetId = null,
        PlanarRegionId = null,
        MeshSource = FemMember.MeshSourceGenerated,
    };

    public void Undo(FemSchemaEditSession session)
    {
        foreach (var g in _newGroups) session.MemberGroups.Remove(g);
        foreach (var e in _newMembers) session.Members.Remove(e);
        foreach (var n in _newNodes) session.Nodes.Remove(n);
    }
}
