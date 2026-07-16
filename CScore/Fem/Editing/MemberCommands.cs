namespace CScore.Fem.Editing;

public sealed class CreateMemberCommand(FemMember member) : IFemEditCommand
{
    public void Do(FemSchemaEditSession session) => session.Members.Add(member);
    public void Undo(FemSchemaEditSession session) => session.Members.Remove(member);
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
