namespace OpenCS.OpenSees.Structural;

/// <summary>Типизированная линейная 3D-стержневая модель для OpenSees (ndm 3, ndf 6).</summary>
public sealed class FemLinearModel
{
    public IReadOnlyList<FemLinearNode> Nodes { get; init; } = [];
    public IReadOnlyList<FemLinearElement> Elements { get; init; } = [];
    public IReadOnlyList<FemLinearNodalLoad> Loads { get; init; } = [];

    /// <summary>Проверяет целостность модели перед генерацией Tcl.</summary>
    public void Validate()
    {
        if (Nodes.Count == 0) throw new InvalidOperationException("Модель не содержит узлов.");
        if (Elements.Count == 0) throw new InvalidOperationException("Модель не содержит элементов.");

        var tags = new HashSet<int>();
        foreach (var n in Nodes)
        {
            if (n.Fixed is null || n.Fixed.Length != 6)
                throw new InvalidOperationException($"Узел {n.Tag}: маска закрепления должна иметь 6 флагов.");
            if (!double.IsFinite(n.X) || !double.IsFinite(n.Y) || !double.IsFinite(n.Z))
                throw new InvalidOperationException($"Узел {n.Tag}: неконечные координаты.");
            if (!tags.Add(n.Tag))
                throw new InvalidOperationException($"Дублирующийся тег узла {n.Tag}.");
        }

        var elemTags = new HashSet<int>();
        foreach (var e in Elements)
        {
            if (!elemTags.Add(e.Tag))
                throw new InvalidOperationException($"Дублирующийся тег элемента {e.Tag}.");
            if (!tags.Contains(e.NodeI) || !tags.Contains(e.NodeJ))
                throw new InvalidOperationException($"Элемент {e.Tag} ссылается на несуществующий узел.");
            if (e.A <= 0 || e.E <= 0)
                throw new InvalidOperationException($"Элемент {e.Tag}: A и E должны быть положительны.");
        }

        foreach (var l in Loads)
            if (!tags.Contains(l.NodeTag))
                throw new InvalidOperationException($"Нагрузка ссылается на несуществующий узел {l.NodeTag}.");
    }
}
