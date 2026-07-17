namespace CScore.Fem.Editing;

/// <summary>Сессия редактирования одной FEM-схемы в памяти: рабочие копии + история команд.</summary>
public sealed class FemSchemaEditSession
{
    public FemSchema Schema { get; }

    public List<FemNode>        Nodes        { get; } = [];
    public List<FemMember>      Members      { get; } = [];
    public List<FemMemberGroup> MemberGroups { get; } = [];
    public List<FemLoadCase>    LoadCases    { get; } = [];
    public List<FemNodeLoad>    NodeLoads    { get; } = [];

    readonly List<IFemEditCommand> _history = [];
    int _position; // индекс первой ненаправленной команды (== _history.Count при отсутствии redo)

    public FemSchemaEditSession(FemSchema schema) => Schema = schema;

    public bool CanUndo => _position > 0;
    public bool CanRedo => _position < _history.Count;
    public bool IsDirty => _position > 0;

    public void Execute(IFemEditCommand command)
    {
        if (_position < _history.Count)
            _history.RemoveRange(_position, _history.Count - _position);
        command.Do(this);
        _history.Add(command);
        _position++;
    }

    public void Undo()
    {
        if (!CanUndo) return;
        _position--;
        _history[_position].Undo(this);
    }

    public void Redo()
    {
        if (!CanRedo) return;
        _history[_position].Do(this);
        _position++;
    }

    /// <summary>Сбрасывает историю после успешного сохранения (данные в сессии остаются как «чистое» состояние).</summary>
    public void MarkSaved()
    {
        _history.Clear();
        _position = 0;
    }
}
