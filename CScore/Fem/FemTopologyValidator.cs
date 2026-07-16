using System.Text.Json;

namespace CScore.Fem;

/// <summary>Проверяет топологическую и GJ-корректность канонической FEM-схемы.</summary>
public static class FemTopologyValidator
{
    static readonly HashSet<string> GjStrategies = new(StringComparer.OrdinalIgnoreCase)
    {
        "manual", "saint_venant"
    };

    public static IReadOnlyList<FemValidationDiagnostic> Validate(
        FemSchema schema,
        IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemElement> elements,
        IReadOnlyList<FemMember> members)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(members);

        var errors = new List<FemValidationDiagnostic>();
        var nodeById = new Dictionary<int, FemNode>();
        foreach (var node in nodes)
            if (!nodeById.TryAdd(node.Id, node))
                errors.Add(new("node_id_duplicate", $"Идентификатор узла {node.Id} повторяется."));

        foreach (var group in nodes.GroupBy(n => n.NodeTag, StringComparer.Ordinal).Where(g => g.Count() > 1))
            errors.Add(new("node_tag_duplicate", $"Тег узла '{group.Key}' используется несколько раз."));

        var elemById = new Dictionary<int, FemElement>();
        foreach (var element in elements)
        {
            if (!elemById.TryAdd(element.Id, element))
                errors.Add(new("element_id_duplicate", $"Идентификатор элемента {element.Id} повторяется."));

            var ids = JsonSerializer.Deserialize<int[]>(element.NodeIdsJson) ?? [];
            foreach (var nodeId in ids)
                if (!nodeById.ContainsKey(nodeId))
                    errors.Add(new("element_node_missing",
                        $"Элемент {element.ElemTag} ссылается на отсутствующий узел {nodeId}."));

            if (element.ElemType == "beam" && ids.Length == 2 && ids[0] == ids[1])
                errors.Add(new("element_zero_length", $"Стержень {element.ElemTag} имеет нулевую длину."));
        }

        foreach (var group in elements.GroupBy(e => e.ElemTag, StringComparer.Ordinal).Where(g => g.Count() > 1))
            errors.Add(new("element_tag_duplicate", $"Тег элемента '{group.Key}' используется несколько раз."));

        var elemTagSet = elements.Select(e => e.ElemTag).ToHashSet(StringComparer.Ordinal);
        foreach (var member in members)
        {
            var elemTags = (JsonSerializer.Deserialize<int[]>(member.ElemIdsJson) ?? [])
                .Select(id => id.ToString());
            foreach (var tag in elemTags)
                if (!elemTagSet.Contains(tag))
                    errors.Add(new("member_element_missing",
                        $"Конструктивный элемент '{member.Tag}' ссылается на отсутствующий КЭ {tag}."));

            if (member.CrossSectionId == null)
                errors.Add(new("member_section_missing",
                    $"Конструктивному элементу '{member.Tag}' не назначено сечение.", IsError: false));

            if (!GjStrategies.Contains(member.GjStrategy))
                errors.Add(new("gj_strategy_invalid",
                    $"У '{member.Tag}' недопустимая GJ-стратегия '{member.GjStrategy}'."));
            else if (member.GjStrategy == "manual" &&
                     (member.GjManualValue is null || member.GjManualValue <= 0 || !double.IsFinite(member.GjManualValue.Value)))
                errors.Add(new("gj_manual_value_missing",
                    $"У '{member.Tag}' не задано положительное ручное значение GJ."));
            else if (member.GjStrategy == "saint_venant" && member.GjTorsionTaskId is null)
                errors.Add(new("gj_torsion_task_missing",
                    $"У '{member.Tag}' не выбрана задача кручения для GJ."));
        }

        return errors;
    }

    /// <summary>Следующий свободный числовой тег узла в схеме, начиная с "1".</summary>
    public static string NextNodeTag(IReadOnlyList<FemNode> nodes) => NextTag(nodes.Select(n => n.NodeTag));

    /// <summary>Следующий свободный числовой тег элемента в схеме, начиная с "1".</summary>
    public static string NextElemTag(IReadOnlyList<FemElement> elements) => NextTag(elements.Select(e => e.ElemTag));

    static string NextTag(IEnumerable<string> tags)
    {
        int max = 0;
        foreach (var tag in tags)
            if (int.TryParse(tag, out var value) && value > max)
                max = value;
        return (max + 1).ToString();
    }
}
