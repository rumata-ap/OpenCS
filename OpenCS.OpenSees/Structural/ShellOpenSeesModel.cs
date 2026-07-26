using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Structural;

/// <summary>Нормализованная shell-модель OpenSees без UI и persistence-зависимостей.</summary>
public sealed record ShellOpenSeesModel
{
    /// <summary>Узлы модели.</summary>
    public IReadOnlyList<NormalizedShellNode> Nodes { get; init; } = [];

    /// <summary>Native shell-материалы модели.</summary>
    public IReadOnlyList<NativeShellMaterialDefinition> Materials { get; init; } = [];

    /// <summary>Слоистые shell-секции модели.</summary>
    public IReadOnlyList<RCShellLayeredSection> Sections { get; init; } = [];

    /// <summary>Оболочечные элементы модели.</summary>
    public IReadOnlyList<NormalizedShellElement> Elements { get; init; } = [];

    /// <summary>Узловые нагрузки модели.</summary>
    public IReadOnlyList<ShellNodalLoad> Loads { get; init; } = [];

    /// <summary>Проверяет topology, geometry, materials и section mappings.</summary>
    public void Validate()
    {
        if (Nodes.Count == 0)
            throw new InvalidOperationException("Shell-модель не содержит узлов.");
        if (Sections.Count == 0)
            throw new InvalidOperationException("Shell-модель не содержит секций.");
        if (Elements.Count == 0)
            throw new InvalidOperationException("Shell-модель не содержит элементов.");

        var nodes = new Dictionary<int, NormalizedShellNode>();
        foreach (NormalizedShellNode node in Nodes)
        {
            node.Validate();
            if (!nodes.TryAdd(node.Tag, node))
                throw new InvalidOperationException($"Дублирующийся tag shell-узла {node.Tag}.");
        }

        var materials = new Dictionary<int, NativeShellMaterialDefinition>();
        foreach (NativeShellMaterialDefinition material in Materials)
        {
            material.Validate();
            if (!materials.TryAdd(material.Tag, material))
                throw new InvalidOperationException($"Дублирующийся tag shell-материала {material.Tag}.");
        }

        var sections = new Dictionary<int, RCShellLayeredSection>();
        foreach (RCShellLayeredSection section in Sections)
        {
            section.Validate();
            if (!sections.TryAdd(section.Tag, section))
                throw new InvalidOperationException($"Дублирующийся tag shell-секции {section.Tag}.");
            foreach (RCShellLayer layer in section.Layers)
                if (!materials.ContainsKey(layer.MaterialTag))
                    throw new InvalidOperationException(
                        $"Секция {section.Tag}, слой {layer.Index} ссылается на неизвестный material tag {layer.MaterialTag}.");
        }

        var elements = new HashSet<int>();
        foreach (NormalizedShellElement element in Elements)
        {
            if (!elements.Add(element.Tag))
                throw new InvalidOperationException($"Дублирующийся tag shell-элемента {element.Tag}.");
            element.Validate(nodes, sections.Keys.ToHashSet());
            if (!HasPositiveArea(element, nodes))
                throw new InvalidOperationException($"Элемент {element.Tag}: площадь geometry должна быть положительной.");
            if (!string.Equals(element.SectionFingerprint, sections[element.SectionTag].Fingerprint,
                               StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Элемент {element.Tag}: fingerprint секции не совпадает с секцией {element.SectionTag}.");
        }

        foreach (ShellNodalLoad load in Loads)
        {
            if (!nodes.ContainsKey(load.NodeTag))
                throw new InvalidOperationException($"Нагрузка ссылается на неизвестный узел {load.NodeTag}.");
            if (!double.IsFinite(load.Fx) || !double.IsFinite(load.Fy) || !double.IsFinite(load.Fz) ||
                !double.IsFinite(load.Mx) || !double.IsFinite(load.My) || !double.IsFinite(load.Mz))
                throw new InvalidOperationException($"Нагрузка узла {load.NodeTag}: компоненты должны быть конечны.");
        }
    }

    private static bool HasPositiveArea(
        NormalizedShellElement element,
        IReadOnlyDictionary<int, NormalizedShellNode> nodes)
    {
        ShellVector3 areaVector = ShellVector3.Zero;
        for (int i = 0; i < element.NodeTags.Count; i++)
        {
            NormalizedShellNode current = nodes[element.NodeTags[i]];
            NormalizedShellNode next = nodes[element.NodeTags[(i + 1) % element.NodeTags.Count]];
            var currentPoint = new ShellVector3(current.X, current.Y, current.Z);
            var nextPoint = new ShellVector3(next.X, next.Y, next.Z);
            areaVector += currentPoint.Cross(nextPoint);
        }

        return 0.5 * areaVector.Length > 1e-12;
    }
}
