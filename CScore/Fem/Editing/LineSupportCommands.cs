namespace CScore.Fem.Editing;

/// <summary>
/// Назначает линейную связь граням пластин одной отменой: связь грани заменяется новой (одна связь на грань), грань с
/// пустыми параметрами (ни закреплений, ни пружин) связь теряет.
/// </summary>
public sealed class SetLineSupportsCommand : IFemEditCommand
{
    readonly IReadOnlyList<FemPlateEdge> _edges;
    readonly FemLineSupport _template;
    List<(int Index, FemLineSupport Support)> _removed = [];
    List<FemLineSupport> _added = [];

    /// <param name="template">Параметры связи (маска, поворот кромки, погонные жёсткости, происхождение).</param>
    public SetLineSupportsCommand(IEnumerable<FemPlateEdge> edges, FemLineSupport template)
    {
        _edges = edges.DistinctBy(e => e.Key).ToList();
        _template = template;
    }

    public void Do(FemSchemaEditSession session)
    {
        var keys = _edges.Select(e => e.Key).ToHashSet();
        _removed = session.LineSupports.Select((s, i) => (i, s)).Where(x => keys.Contains(x.s.Key)).ToList();
        for (int i = _removed.Count - 1; i >= 0; i--) session.LineSupports.RemoveAt(_removed[i].Index);
        _added = [];
        if (!_template.IsActive) return;
        foreach (var edge in _edges)
        {
            var s = _template.Clone();
            s.Id = 0;
            s.SchemaId = session.Schema.Id;
            s.AssignEdge(edge);
            _added.Add(s);
        }
        session.LineSupports.AddRange(_added);
    }

    public void Undo(FemSchemaEditSession session)
    {
        foreach (var s in _added) session.LineSupports.Remove(s);
        foreach (var (index, s) in _removed) session.LineSupports.Insert(Math.Min(index, session.LineSupports.Count), s);
    }
}

/// <summary>Снимает линейные связи (по объектам) одной отменой.</summary>
public sealed class RemoveLineSupportsCommand(IEnumerable<FemLineSupport> supports) : IFemEditCommand
{
    readonly HashSet<FemLineSupport> _targets = new(supports, ReferenceEqualityComparer.Instance);
    List<(int Index, FemLineSupport Support)> _removed = [];

    public void Do(FemSchemaEditSession session) => _removed = LineSupportCascade.Remove(session, _targets.Contains);

    public void Undo(FemSchemaEditSession session) => LineSupportCascade.Restore(session, _removed);
}

/// <summary>Каскад линейных связей при удалении КонЭ: снятие с запоминанием позиций и возврат при отмене.</summary>
public static class LineSupportCascade
{
    /// <summary>Убирает связи по условию; возвращает их с исходными индексами (по возрастанию).</summary>
    public static List<(int Index, FemLineSupport Support)> Remove(FemSchemaEditSession session, Func<FemLineSupport, bool> match)
    {
        var removed = session.LineSupports.Select((s, i) => (i, s)).Where(x => match(x.s)).ToList();
        for (int i = removed.Count - 1; i >= 0; i--) session.LineSupports.RemoveAt(removed[i].Item1);
        return removed;
    }

    /// <summary>Связи КонЭ с тегами <paramref name="memberTags"/>.</summary>
    public static List<(int Index, FemLineSupport Support)> RemoveForMembers(FemSchemaEditSession session, IEnumerable<string> memberTags)
    {
        var tags = memberTags.ToHashSet(StringComparer.Ordinal);
        return tags.Count == 0 ? [] : Remove(session, s => tags.Contains(s.MemberTag));
    }

    /// <summary>Возвращает связи на исходные места.</summary>
    public static void Restore(FemSchemaEditSession session, List<(int Index, FemLineSupport Support)> removed)
    {
        foreach (var (index, s) in removed) session.LineSupports.Insert(Math.Min(index, session.LineSupports.Count), s);
    }
}
