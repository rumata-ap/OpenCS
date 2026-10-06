namespace CScore.Fem.Editing;

public sealed class CreateMemberGroupCommand(FemMemberGroup group) : IFemEditCommand
{
    public void Do(FemSchemaEditSession session) => session.MemberGroups.Add(group);
    public void Undo(FemSchemaEditSession session) => session.MemberGroups.Remove(group);
}

/// <summary>Добавляет теги в состав группы или убирает их (по виду группы — номера КЭ или теги КонЭ). Обратимо.</summary>
public sealed class EditMemberGroupTagsCommand(FemMemberGroup group, IReadOnlyCollection<string> tags, bool remove)
    : IFemEditCommand
{
    string _oldJson = "[]";

    /// <summary>Сколько тегов добавлено или убрано последним <see cref="Do"/>.</summary>
    public int Changed { get; private set; }

    public void Do(FemSchemaEditSession session)
    {
        _oldJson = group.MemberTagsJson;
        Changed = remove ? FemGroupComposition.RemoveTags(group, tags) : FemGroupComposition.AddTags(group, tags);
    }

    public void Undo(FemSchemaEditSession session) => group.MemberTagsJson = _oldJson;
}
