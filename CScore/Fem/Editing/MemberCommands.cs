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
    List<(int Index, FemLineSupport Support)> _lineSupports = [];
    // Нагрузки на КЭ с целью «КонЭ по тегам»: без удалённого тега цели нагрузка теряет его, без целей — удаляется.
    List<(FemElementLoad Load, string OldJson)> _elementTargetEdits = [];
    List<(int Index, FemElementLoad Load)> _elementLoadsRemoved = [];

    public void Do(FemSchemaEditSession session)
    {
        session.Members.Remove(member);
        _lineSupports = LineSupportCascade.RemoveForMembers(session, [member.ElemTag]);
        _loadEdits = session.MemberLoads.Where(load => load.MemberId == member.Id).ToList();
        foreach (var load in _loadEdits) session.MemberLoads.Remove(load);
        _groupEdits = FemGroupComposition.RemoveMemberTags(session.MemberGroups, [member.ElemTag]);

        _elementTargetEdits = [];
        _elementLoadsRemoved = [];
        for (int i = session.ElementLoads.Count - 1; i >= 0; i--)
        {
            var load = session.ElementLoads[i];
            if (load.TargetKind != FemLoadTargetKinds.Members || !load.TargetTags.Contains(member.ElemTag)) continue;
            var rest = load.TargetTags.Where(t => t != member.ElemTag).ToList();
            if (rest.Count == 0)
            {
                _elementLoadsRemoved.Add((i, load));
                session.ElementLoads.RemoveAt(i);
            }
            else
            {
                _elementTargetEdits.Add((load, load.TargetTagsJson));
                load.SetTargetTags(rest);
            }
        }
    }

    public void Undo(FemSchemaEditSession session)
    {
        session.Members.Add(member);
        LineSupportCascade.Restore(session, _lineSupports);
        foreach (var load in _loadEdits) session.MemberLoads.Add(load);
        foreach (var (group, oldJson) in _groupEdits) group.MemberTagsJson = oldJson;
        // Удалялись с конца списка — возвращаются в обратном порядке, на прежние места.
        for (int k = _elementLoadsRemoved.Count - 1; k >= 0; k--)
        {
            var (index, load) = _elementLoadsRemoved[k];
            session.ElementLoads.Insert(Math.Min(index, session.ElementLoads.Count), load);
        }
        foreach (var (load, oldJson) in _elementTargetEdits) load.TargetTagsJson = oldJson;
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

/// <summary>Параметры АЖТ примыкания у стержневых КонЭ (режим, маска, вид); явный след — отдельно, только сброс.</summary>
public sealed class SetRigidJunctionParamsCommand(IReadOnlyList<FemMember> members, string? mode, int? mask, string? kind)
    : IFemEditCommand
{
    (string? Mode, int? Mask, string? Kind)[] _old = [];

    public void Do(FemSchemaEditSession session)
    {
        _old = members.Select(m => (m.RigidJunctionMode, m.RigidJunctionMask, m.RigidJunctionKind)).ToArray();
        foreach (var m in members)
        {
            m.RigidJunctionMode = mode;
            m.RigidJunctionMask = mask;
            m.RigidJunctionKind = kind;
        }
    }

    public void Undo(FemSchemaEditSession session)
    {
        for (int i = 0; i < members.Count; i++)
            (members[i].RigidJunctionMode, members[i].RigidJunctionMask, members[i].RigidJunctionKind) = _old[i];
    }
}

/// <summary>Сброс явного следа АЖТ у КонЭ (след снова по сечению).</summary>
public sealed class ClearRigidJunctionTraceCommand(IReadOnlyList<FemMember> members) : IFemEditCommand
{
    string?[] _old = [];

    public void Do(FemSchemaEditSession session)
    {
        _old = members.Select(m => m.RigidJunctionTraceJson).ToArray();
        foreach (var m in members) m.RigidJunctionTraceJson = null;
    }

    public void Undo(FemSchemaEditSession session)
    {
        for (int i = 0; i < members.Count; i++) members[i].RigidJunctionTraceJson = _old[i];
    }
}
