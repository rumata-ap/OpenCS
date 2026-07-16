using System.Text.Json;

namespace CScore.Fem.Editing;

public sealed class AddElementCommand(FemElement element) : IFemEditCommand
{
    public void Do(FemSchemaEditSession session) => session.Elements.Add(element);
    public void Undo(FemSchemaEditSession session) => session.Elements.Remove(element);
}

/// <summary>Удаляет элемент и убирает его тег из FemMember.ElemIdsJson всех членов, где он был. Обратимо.</summary>
public sealed class DeleteElementCommand(FemElement element) : IFemEditCommand
{
    List<(FemMember member, string oldJson)> _memberEdits = [];

    public void Do(FemSchemaEditSession session)
    {
        session.Elements.Remove(element);
        _memberEdits = [];
        foreach (var member in session.Members)
        {
            var ids = JsonSerializer.Deserialize<int[]>(member.ElemIdsJson) ?? [];
            var kept = ids.Where(id => id.ToString() != element.ElemTag).ToArray();
            if (kept.Length == ids.Length) continue;
            _memberEdits.Add((member, member.ElemIdsJson));
            member.ElemIdsJson = JsonSerializer.Serialize(kept);
        }
    }

    public void Undo(FemSchemaEditSession session)
    {
        session.Elements.Add(element);
        foreach (var (member, oldJson) in _memberEdits) member.ElemIdsJson = oldJson;
    }
}
