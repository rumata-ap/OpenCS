namespace CScore.Fem.Editing;

public sealed class AddMemberCommand(FemMember member) : IFemEditCommand
{
    public void Do(FemSchemaEditSession session) => session.Members.Add(member);
    public void Undo(FemSchemaEditSession session) => session.Members.Remove(member);
}

/// <summary>Удаляет элемент и убирает его тег из состава всех групп КонЭ, где он был. Обратимо.</summary>
public sealed class DeleteMemberCommand(FemMember member) : IFemEditCommand
{
    List<(FemMemberGroup group, string oldJson)> _groupEdits = [];
    List<FemMemberLoad> _loadEdits = [];

    public void Do(FemSchemaEditSession session)
    {
        session.Members.Remove(member);
        _loadEdits = session.MemberLoads.Where(load => load.MemberId == member.Id).ToList();
        foreach (var load in _loadEdits) session.MemberLoads.Remove(load);
        _groupEdits = FemGroupComposition.RemoveMemberTags(session.MemberGroups, [member.ElemTag]);
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

/// <summary>Новое состояние GJ одного конструктивного стержня для составной команды.</summary>
public readonly record struct MemberGjAssignment(
    FemMember Member,
    string Strategy,
    double? ManualValue,
    int? TorsionTaskId);

/// <summary>Атомарно применяет новые состояния GJ к нескольким стержням и поддерживает undo/redo.</summary>
public sealed class SetMembersGjCommand : IFemEditCommand
{
    readonly IReadOnlyList<MemberGjAssignment> _assignments;
    List<OldGjState> _oldStates = [];
    bool _captured;

    public SetMembersGjCommand(IReadOnlyList<MemberGjAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        _assignments = assignments.ToArray();
    }

    public void Do(FemSchemaEditSession session)
    {
        if (!_captured)
        {
            _oldStates = _assignments
                .Select(a => new OldGjState(a.Member, a.Member.GjStrategy, a.Member.GjManualValue, a.Member.GjTorsionTaskId))
                .ToList();
            _captured = true;
        }

        foreach (var assignment in _assignments)
        {
            assignment.Member.GjStrategy = assignment.Strategy;
            assignment.Member.GjManualValue = assignment.ManualValue;
            assignment.Member.GjTorsionTaskId = assignment.TorsionTaskId;
        }
    }

    public void Undo(FemSchemaEditSession session)
    {
        foreach (var old in _oldStates)
        {
            old.Member.GjStrategy = old.Strategy;
            old.Member.GjManualValue = old.ManualValue;
            old.Member.GjTorsionTaskId = old.TorsionTaskId;
        }
    }

    readonly record struct OldGjState(
        FemMember Member,
        string Strategy,
        double? ManualValue,
        int? TorsionTaskId);
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

/// <summary>Локальный шаг сетки КонЭ (<see cref="FemMember.TargetMeshLengthM"/>); null — общий шаг схемы.</summary>
public sealed class SetMembersMeshStepCommand(IReadOnlyList<FemMember> members, double? stepM) : IFemEditCommand
{
    double?[] _old = [];

    public void Do(FemSchemaEditSession session)
    {
        _old = members.Select(m => m.TargetMeshLengthM).ToArray();
        foreach (var m in members) m.TargetMeshLengthM = stepM;
    }

    public void Undo(FemSchemaEditSession session)
    {
        for (int i = 0; i < members.Count; i++) members[i].TargetMeshLengthM = _old[i];
    }
}
