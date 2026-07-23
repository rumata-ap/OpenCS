using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Structural;

/// <summary>Типизированная нелинейная 3D-стержневая модель для OpenSees (ndm 3, ndf 6):
/// fiber-сечения + forceBeamColumn + шаги нагрузки с критерием сходимости.</summary>
public sealed class FemNonlinearModel
{
    public IReadOnlyList<FemLinearNode> Nodes { get; init; } = [];
    public IReadOnlyDictionary<int, OpenSeesSectionModel> Sections { get; init; } =
        new Dictionary<int, OpenSeesSectionModel>();
    public IReadOnlyList<FemNonlinearElement> Elements { get; init; } = [];
    public IReadOnlyList<FemLinearNodalLoad> Loads { get; init; } = [];
    public IReadOnlyList<FemLinearDistributedLoad> DistributedLoads { get; init; } = [];

    public double LoadFactorStep { get; init; } = 0.1;
    public double MaxLoadFactor { get; init; } = 10.0;
    public int RefinementDivisions { get; init; } = 10;
    public double Tolerance { get; init; } = 1e-6;
    public int MaxIterations { get; init; } = 50;
    public string GeomTransfKind { get; init; } = "Linear";
    /// <summary>Критерий сходимости Ньютона: "EnergyIncr" (по умолчанию, самый устойчивый —
    /// учитывает и невязку силы, и приращение перемещения) | "NormUnbalance" | "NormDispIncr".</summary>
    public string ConvergenceTest { get; init; } = "EnergyIncr";
    /// <summary>Имя набора диаграмм CScore, использованного при построении fiber-сечения.</summary>
    public string CalcTypeName { get; init; } = "C";

    /// <summary>Проверяет целостность модели перед генерацией Tcl.</summary>
    public void Validate()
    {
        if (Nodes.Count == 0) throw new InvalidOperationException("Модель не содержит узлов.");
        if (Elements.Count == 0) throw new InvalidOperationException("Модель не содержит элементов.");
        if (Sections.Count == 0) throw new InvalidOperationException("Модель не содержит fiber-сечений.");
        if (!double.IsFinite(LoadFactorStep) || LoadFactorStep <= 0)
            throw new InvalidOperationException("Шаг коэффициента нагрузки должен быть конечным и положительным.");
        if (!double.IsFinite(MaxLoadFactor) || MaxLoadFactor <= 0)
            throw new InvalidOperationException("Максимальный коэффициент нагрузки должен быть конечным и положительным.");
        if (MaxLoadFactor < LoadFactorStep)
            throw new InvalidOperationException("Максимальный коэффициент нагрузки не может быть меньше шага.");
        if (RefinementDivisions <= 0)
            throw new InvalidOperationException("Количество делений шага должно быть положительным.");
        if (Tolerance <= 0) throw new InvalidOperationException("Допуск невязки должен быть положительным.");
        if (MaxIterations <= 0) throw new InvalidOperationException("Максимальное число итераций должно быть положительным.");
        if (GeomTransfKind is not ("Linear" or "PDelta" or "Corotational"))
            throw new InvalidOperationException($"Неизвестная формулировка geomTransf «{GeomTransfKind}».");
        if (ConvergenceTest is not ("EnergyIncr" or "NormUnbalance" or "NormDispIncr"))
            throw new InvalidOperationException($"Неизвестный критерий сходимости «{ConvergenceTest}».");

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
            if (!Sections.ContainsKey(e.SectionTag))
                throw new InvalidOperationException($"Элемент {e.Tag} ссылается на несуществующее сечение {e.SectionTag}.");
            if (e.NumIntegrationPoints <= 0)
                throw new InvalidOperationException($"Элемент {e.Tag}: число точек интегрирования должно быть положительным.");
        }

        foreach (var l in Loads)
            if (!tags.Contains(l.NodeTag))
                throw new InvalidOperationException($"Нагрузка ссылается на несуществующий узел {l.NodeTag}.");

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
        if (GeomTransfKind == "Corotational" && DistributedLoads.Count > 0)
            throw new InvalidOperationException("Распределённые нагрузки не поддерживаются для 3D forceBeamColumn с geomTransf Corotational.");
    }
}
