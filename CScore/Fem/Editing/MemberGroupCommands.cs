namespace CScore.Fem.Editing;

public sealed class CreateMemberGroupCommand(FemMemberGroup group) : IFemEditCommand
{
    public void Do(FemSchemaEditSession session) => session.MemberGroups.Add(group);
    public void Undo(FemSchemaEditSession session) => session.MemberGroups.Remove(group);
}
