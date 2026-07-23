namespace OpenCS.OpenSees.Structural;

/// <summary>Типизированная линейная 3D-стержневая модель для OpenSees (ndm 3, ndf 6).</summary>
public sealed class FemLinearModel
{
    public IReadOnlyList<FemLinearNode> Nodes { get; init; } = [];
    public IReadOnlyList<FemLinearElement> Elements { get; init; } = [];
    public IReadOnlyList<FemLinearNodalLoad> Loads { get; init; } = [];
    public IReadOnlyList<FemLinearDistributedLoad> DistributedLoads { get; init; } = [];
    public IReadOnlyList<FemLinearPointLoad> PointLoads { get; init; } = [];
    public IReadOnlyList<FemLinearKinematicLoad> KinematicLoads { get; init; } = [];

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

        var kinematicDofs = new HashSet<(int NodeTag, int Dof)>();
        foreach (var l in KinematicLoads)
        {
            if (!tags.Contains(l.NodeTag))
                throw new InvalidOperationException($"Кинематическая нагрузка ссылается на несуществующий узел {l.NodeTag}.");
            if (l.Dof is < 1 or > 6)
                throw new InvalidOperationException($"Кинематическая нагрузка узла {l.NodeTag}: DOF должен быть от 1 до 6.");
            if (!double.IsFinite(l.Value))
                throw new InvalidOperationException($"Кинематическая нагрузка узла {l.NodeTag}: значение должно быть конечным.");
            if (!kinematicDofs.Add((l.NodeTag, l.Dof)))
                throw new InvalidOperationException($"Дублирующееся кинематическое воздействие узла {l.NodeTag}, DOF {l.Dof}.");
            var node = Nodes.First(n => n.Tag == l.NodeTag);
            if (node.Fixed[l.Dof - 1] && l.Value != 0)
                throw new InvalidOperationException($"Кинематическая нагрузка узла {l.NodeTag}, DOF {l.Dof} конфликтует с закреплением.");
        }

        foreach (var l in DistributedLoads)
        {
            if (!elemTags.Contains(l.ElementTag))
                throw new InvalidOperationException($"Распределённая нагрузка ссылается на несуществующий элемент {l.ElementTag}.");
            if (!double.IsFinite(l.WyStart) || !double.IsFinite(l.WzStart) || !double.IsFinite(l.WxStart) ||
                !double.IsFinite(l.WyEnd) || !double.IsFinite(l.WzEnd) || !double.IsFinite(l.WxEnd))
                throw new InvalidOperationException($"Распределённая нагрузка элемента {l.ElementTag}: интенсивности должны быть конечными.");
            if (!double.IsFinite(l.AOverL) || !double.IsFinite(l.BOverL) ||
                l.AOverL < 0 || l.BOverL > 1 || l.BOverL <= l.AOverL)
                throw new InvalidOperationException($"Распределённая нагрузка элемента {l.ElementTag}: некорректный интервал приложения.");
        }

        foreach (var l in PointLoads)
        {
            if (!elemTags.Contains(l.ElementTag))
                throw new InvalidOperationException($"Сосредоточенная нагрузка ссылается на несуществующий элемент {l.ElementTag}.");
            if (!double.IsFinite(l.Py) || !double.IsFinite(l.Pz) || !double.IsFinite(l.Px))
                throw new InvalidOperationException($"Сосредоточенная нагрузка элемента {l.ElementTag}: компоненты должны быть конечными.");
            if (!double.IsFinite(l.XOverL) || l.XOverL <= 0 || l.XOverL >= 1)
                throw new InvalidOperationException($"Сосредоточенная нагрузка элемента {l.ElementTag}: xL должен быть строго между 0 и 1.");
        }
    }
}
