namespace CScore.Fem.Editing;

/// <summary>Обратимая команда редактирования FEM-схемы.</summary>
public interface IFemEditCommand
{
    void Do(FemSchemaEditSession session);
    void Undo(FemSchemaEditSession session);
}

/// <summary>Несколько команд одной отменой: выполняются по порядку, отменяются в обратном.</summary>
public sealed class CompositeFemCommand(IEnumerable<IFemEditCommand> commands) : IFemEditCommand
{
    readonly IReadOnlyList<IFemEditCommand> _commands = commands.ToList();

    public void Do(FemSchemaEditSession session)
    {
        foreach (var command in _commands) command.Do(session);
    }

    public void Undo(FemSchemaEditSession session)
    {
        for (int i = _commands.Count - 1; i >= 0; i--) _commands[i].Undo(session);
    }
}
