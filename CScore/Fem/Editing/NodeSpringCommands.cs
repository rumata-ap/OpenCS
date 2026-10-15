namespace CScore.Fem.Editing;

/// <summary>
/// Узловая связь узлов КонЭ одной отменой: маска закреплений <see cref="FemNode.DofMask"/> и пружины узла
/// (<see cref="FemSchemaEditSession.NodeSprings"/>; нулевые жёсткости — пружины нет). Прежние пружины этих узлов
/// заменяются.
/// </summary>
public sealed class SetNodeBoundaryCommand : IFemEditCommand
{
    readonly IReadOnlyList<FemNode> _nodes;
    readonly int _mask;
    readonly double[] _stiffnesses;
    int[] _oldMasks = [];
    List<(int Index, FemSpring Spring)> _removed = [];
    List<FemSpring> _added = [];

    /// <param name="stiffnesses">Жёсткости X Y Z UX UY UZ, Н/м и Н·м/рад.</param>
    public SetNodeBoundaryCommand(IReadOnlyList<FemNode> nodes, int mask, IReadOnlyList<double> stiffnesses)
    {
        if (stiffnesses.Count != 6) throw new ArgumentException("Нужно 6 жёсткостей: X Y Z UX UY UZ.", nameof(stiffnesses));
        _nodes = nodes;
        _mask = mask & FemBoundaryDofs.All;
        _stiffnesses = stiffnesses.ToArray();
    }

    public void Do(FemSchemaEditSession session)
    {
        _oldMasks = _nodes.Select(n => n.DofMask).ToArray();
        foreach (var node in _nodes) node.DofMask = _mask;

        var tags = _nodes.Select(n => n.NodeTag).ToHashSet(StringComparer.Ordinal);
        _removed = session.NodeSprings.Select((s, i) => (Index: i, Spring: s)).Where(x => tags.Contains(x.Spring.NodeTag)).ToList();
        for (int i = _removed.Count - 1; i >= 0; i--) session.NodeSprings.RemoveAt(_removed[i].Index);

        _added = [];
        if (_stiffnesses.Any(k => k != 0))
            foreach (var node in _nodes)
            {
                var spring = new FemSpring
                {
                    SchemaId = session.Schema.Id, TargetKind = FemSpringTargetKinds.Node, NodeTag = node.NodeTag,
                    Origin = FemLoadOrigin.Manual,
                };
                spring.SetStiffnesses(_stiffnesses);
                _added.Add(spring);
            }
        session.NodeSprings.AddRange(_added);
    }

    public void Undo(FemSchemaEditSession session)
    {
        foreach (var spring in _added) session.NodeSprings.Remove(spring);
        foreach (var (index, spring) in _removed) session.NodeSprings.Insert(index, spring);
        for (int i = 0; i < _nodes.Count; i++) _nodes[i].DofMask = _oldMasks[i];
    }
}
