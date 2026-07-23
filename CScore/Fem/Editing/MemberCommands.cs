namespace CScore.Fem.Editing;

public sealed class AddMemberCommand(FemMember member) : IFemEditCommand
{
    public void Do(FemSchemaEditSession session) => session.Members.Add(member);
    public void Undo(FemSchemaEditSession session) => session.Members.Remove(member);
}

/// <summary>Удаляет элемент и убирает его тег из FemMemberGroup.MemberTagsJson всех групп, где он был. Обратимо.</summary>
public sealed class DeleteMemberCommand(FemMember member) : IFemEditCommand
{
    List<(FemMemberGroup group, string oldJson)> _groupEdits = [];
    List<FemMemberLoad> _loadEdits = [];

    public void Do(FemSchemaEditSession session)
    {
        session.Members.Remove(member);
        _loadEdits = session.MemberLoads.Where(load => load.MemberId == member.Id).ToList();
        foreach (var load in _loadEdits) session.MemberLoads.Remove(load);
        _groupEdits = [];
        foreach (var group in session.MemberGroups)
        {
            var ids = System.Text.Json.JsonSerializer.Deserialize<int[]>(group.MemberTagsJson) ?? [];
            var kept = ids.Where(id => id.ToString() != member.ElemTag).ToArray();
            if (kept.Length == ids.Length) continue;
            _groupEdits.Add((group, group.MemberTagsJson));
            group.MemberTagsJson = System.Text.Json.JsonSerializer.Serialize(kept);
        }
    }

    public void Undo(FemSchemaEditSession session)
    {
        session.Members.Add(member);
        foreach (var load in _loadEdits) session.MemberLoads.Add(load);
        foreach (var (group, oldJson) in _groupEdits) group.MemberTagsJson = oldJson;
    }
}

public sealed class SetMemberSectionCommand(FemMember member, int? crossSectionId) : IFemEditCommand
{
    int? _old;

    public void Do(FemSchemaEditSession session)
    {
        _old = member.CrossSectionId;
        member.CrossSectionId = crossSectionId;
    }

    public void Undo(FemSchemaEditSession session) => member.CrossSectionId = _old;
}

public sealed class SetMemberGjCommand(FemMember member, string strategy, double? manualValue, int? torsionTaskId)
    : IFemEditCommand
{
    string _oldStrategy = "manual";
    double? _oldManual;
    int? _oldTaskId;

    public void Do(FemSchemaEditSession session)
    {
        _oldStrategy = member.GjStrategy;
        _oldManual = member.GjManualValue;
        _oldTaskId = member.GjTorsionTaskId;
        member.GjStrategy = strategy;
        member.GjManualValue = manualValue;
        member.GjTorsionTaskId = torsionTaskId;
    }

    public void Undo(FemSchemaEditSession session)
    {
        member.GjStrategy = _oldStrategy;
        member.GjManualValue = _oldManual;
        member.GjTorsionTaskId = _oldTaskId;
    }
}

public sealed class SetMemberRotationCommand(FemMember member, double rotationDeg) : IFemEditCommand
{
    double _old;

    public void Do(FemSchemaEditSession session)
    {
        _old = member.RotationDeg;
        member.RotationDeg = rotationDeg;
    }

    public void Undo(FemSchemaEditSession session) => member.RotationDeg = _old;
}
