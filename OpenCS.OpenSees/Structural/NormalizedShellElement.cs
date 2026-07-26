namespace OpenCS.OpenSees.Structural;

/// <summary>Нормализованный shell-элемент с ordered connectivity и локальным frame.</summary>
public sealed record NormalizedShellElement(
    int Tag,
    ShellElementKind Kind,
    IReadOnlyList<int> NodeTags,
    int SectionTag,
    string SectionFingerprint,
    ShellFrame Frame,
    ShellIntegrationPolicy IntegrationPolicy,
    string? SourceId)
{
    /// <summary>Количество точек интегрирования согласно типу и политике элемента.</summary>
    public int IntegrationPointCount => Kind switch
    {
        ShellElementKind.ASDShellQ4 => 4,
        ShellElementKind.ASDShellT3 when IntegrationPolicy == ShellIntegrationPolicy.Reduced => 1,
        ShellElementKind.ASDShellT3 => 3,
        _ => throw new InvalidOperationException($"Неизвестный тип shell-элемента {Kind}.")
    };

    /// <summary>Проверяет topology, frame и ссылки на узлы/секцию.</summary>
    public void Validate(
        IReadOnlyDictionary<int, NormalizedShellNode> nodes,
        IReadOnlySet<int> sectionTags)
    {
        if (Tag <= 0)
            throw new InvalidOperationException("Tag shell-элемента должен быть положительным.");
        if (SectionTag <= 0 || !sectionTags.Contains(SectionTag))
            throw new InvalidOperationException($"Элемент {Tag} ссылается на неизвестную shell-секцию {SectionTag}.");
        if (string.IsNullOrWhiteSpace(SectionFingerprint))
            throw new InvalidOperationException($"Элемент {Tag}: отсутствует fingerprint секции.");
        if (NodeTags is null)
            throw new InvalidOperationException($"Элемент {Tag}: отсутствует connectivity.");

        int expectedNodes = Kind == ShellElementKind.ASDShellQ4 ? 4 : 3;
        if (NodeTags.Count != expectedNodes)
            throw new InvalidOperationException(
                $"Элемент {Tag} типа {Kind} должен иметь {expectedNodes} узла.");

        if (NodeTags.Distinct().Count() != NodeTags.Count)
            throw new InvalidOperationException($"Элемент {Tag}: connectivity содержит повторяющийся узел.");

        foreach (int nodeTag in NodeTags)
        {
            if (!nodes.TryGetValue(nodeTag, out NormalizedShellNode? node))
                throw new InvalidOperationException($"Элемент {Tag} ссылается на неизвестный узел {nodeTag}.");
            node.Validate();
        }

        if (Kind == ShellElementKind.ASDShellQ4 && IntegrationPolicy != ShellIntegrationPolicy.Full)
            throw new InvalidOperationException("ASDShellQ4 поддерживает только полную интеграцию.");

        Frame.Validate();
    }
}
