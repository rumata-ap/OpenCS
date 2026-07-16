using System.Text.Json;

namespace CScore.Fem.Editing;

public sealed class AddNodeCommand(FemNode node) : IFemEditCommand
{
    public void Do(FemSchemaEditSession session) => session.Nodes.Add(node);
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

/// <summary>Удаляет узел и каскадно — ссылающиеся на него элементы, узловые нагрузки
/// и ссылки на удалённые элементы в FemMember.ElemIdsJson. Полностью обратимо.</summary>
public sealed class DeleteNodeCommand(FemNode node) : IFemEditCommand
{
    List<FemElement>  _removedElements  = [];
    List<FemNodeLoad> _removedLoads     = [];
    List<(FemMember member, string oldJson)> _memberEdits = [];

    public void Do(FemSchemaEditSession session)
    {
        session.Nodes.Remove(node);

        // NodeIdsJson хранит NodeTag узла как число, не БД-Id (см. FemTopologyValidator).
        int nodeTagId = int.Parse(node.NodeTag);
        _removedElements = session.Elements
            .Where(e => (JsonSerializer.Deserialize<int[]>(e.NodeIdsJson) ?? []).Contains(nodeTagId))
            .ToList();
        foreach (var e in _removedElements) session.Elements.Remove(e);

        _removedLoads = session.NodeLoads.Where(l => l.NodeId == node.Id).ToList();
        foreach (var l in _removedLoads) session.NodeLoads.Remove(l);

        var removedElemTags = _removedElements.Select(e => e.ElemTag).ToHashSet(StringComparer.Ordinal);
        _memberEdits = [];
        foreach (var member in session.Members)
        {
            var ids = JsonSerializer.Deserialize<int[]>(member.ElemIdsJson) ?? [];
            var kept = ids.Where(id => !removedElemTags.Contains(id.ToString())).ToArray();
            if (kept.Length == ids.Length) continue;
            _memberEdits.Add((member, member.ElemIdsJson));
            member.ElemIdsJson = JsonSerializer.Serialize(kept);
        }
    }

    public void Undo(FemSchemaEditSession session)
    {
        session.Nodes.Add(node);
        foreach (var e in _removedElements) session.Elements.Add(e);
        foreach (var l in _removedLoads) session.NodeLoads.Add(l);
        foreach (var (member, oldJson) in _memberEdits) member.ElemIdsJson = oldJson;
    }
}
