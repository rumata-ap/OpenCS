namespace CScore.Fem;

/// <summary>Диагностическое сообщение проверки канонической FEM-модели.</summary>
public sealed record FemValidationDiagnostic(string Code, string Message);

/// <summary>Проверяет ссылки, идентификаторы и числовую корректность FEM-контрактов.</summary>
public static class FemCanonicalValidator
{
    static readonly HashSet<string> Sp20Types = new(StringComparer.OrdinalIgnoreCase)
    {
        "permanent", "long_term", "short_term", "accidental"
    };

    /// <summary>Возвращает полный детерминированный список ошибок канонической модели.</summary>
    public static IReadOnlyList<FemValidationDiagnostic> Validate(
        FemSchema schema,
        IReadOnlyList<FemLoadCase> loadCases,
        IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemNodeLoad> loads)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(loadCases);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(loads);

        var errors = new List<FemValidationDiagnostic>();
        var caseById = BuildUniqueIndex(loadCases, "load_case_id_duplicate", errors);
        var nodeById = BuildUniqueIndex(nodes, "node_id_duplicate", errors);
        BuildUniqueIndex(loads, "node_load_id_duplicate", errors, skipZero: true);

        foreach (var loadCase in loadCases)
        {
            if (loadCase.SchemaId != schema.Id)
                errors.Add(new("load_case_schema_mismatch",
                    $"Загружение {loadCase.Id} принадлежит схеме {loadCase.SchemaId}, ожидалась {schema.Id}."));
            if (string.IsNullOrWhiteSpace(loadCase.Tag))
                errors.Add(new("load_tag_empty", $"У загружения {loadCase.Id} отсутствует имя."));
            if (!Sp20Types.Contains(loadCase.Sp20Type))
                errors.Add(new("sp20_type_invalid",
                    $"У загружения {loadCase.Id} недопустимый Sp20Type '{loadCase.Sp20Type}'."));
        }

        foreach (var duplicateTag in loadCases
                     .Where(loadCase => !string.IsNullOrWhiteSpace(loadCase.Tag))
                     .GroupBy(loadCase => loadCase.Tag, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
            errors.Add(new("load_tag_duplicate",
                $"Имя загружения '{duplicateTag.Key}' используется несколько раз."));

        foreach (var node in nodes)
            if (node.SchemaId != schema.Id)
                errors.Add(new("node_schema_mismatch",
                    $"Узел {node.Id} принадлежит схеме {node.SchemaId}, ожидалась {schema.Id}."));

        foreach (var load in loads)
        {
            if (load.SchemaId != schema.Id)
                errors.Add(new("node_load_schema_mismatch",
                    $"Узловая нагрузка {load.Id} принадлежит схеме {load.SchemaId}, ожидалась {schema.Id}."));
            if (!caseById.ContainsKey(load.LoadCaseId))
                errors.Add(new("load_case_missing",
                    $"Узловая нагрузка {load.Id} ссылается на отсутствующее загружение {load.LoadCaseId}."));
            if (!nodeById.ContainsKey(load.NodeId))
                errors.Add(new("node_missing",
                    $"Узловая нагрузка {load.Id} ссылается на отсутствующий узел {load.NodeId}."));

            CheckFinite(load.Id, "Fx", load.Fx, errors);
            CheckFinite(load.Id, "Fy", load.Fy, errors);
            CheckFinite(load.Id, "Fz", load.Fz, errors);
            CheckFinite(load.Id, "Mx", load.Mx, errors);
            CheckFinite(load.Id, "My", load.My, errors);
            CheckFinite(load.Id, "Mz", load.Mz, errors);
        }

        return errors;
    }

    /// <summary>Проверяет также уникальность и существование порядка узлов сборки.</summary>
    public static IReadOnlyList<FemValidationDiagnostic> Validate(
        FemSchema schema,
        IReadOnlyList<FemLoadCase> loadCases,
        IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemNodeLoad> loads,
        IReadOnlyList<int> orderedNodeIds)
    {
        var errors = Validate(schema, loadCases, nodes, loads).ToList();
        var knownIds = nodes.Select(node => node.Id).ToHashSet();
        var seen = new HashSet<int>();
        foreach (int nodeId in orderedNodeIds)
        {
            if (!seen.Add(nodeId))
                errors.Add(new("node_order_duplicate", $"Узел {nodeId} повторяется в порядке сборки."));
            else if (!knownIds.Contains(nodeId))
                errors.Add(new("node_order_missing", $"Узел {nodeId} отсутствует в схеме."));
        }
        return errors;
    }

    static Dictionary<int, T> BuildUniqueIndex<T>(
        IEnumerable<T> items,
        string duplicateCode,
        ICollection<FemValidationDiagnostic> errors,
        bool skipZero = false)
        where T : class
    {
        var result = new Dictionary<int, T>();
        foreach (var item in items)
        {
            int id = item switch
            {
                FemLoadCase loadCase => loadCase.Id,
                FemNode node => node.Id,
                FemNodeLoad load => load.Id,
                _ => throw new ArgumentException("Неподдерживаемый тип FEM-объекта.", nameof(items))
            };
            if (skipZero && id == 0) continue;
            if (!result.TryAdd(id, item))
                errors.Add(new(duplicateCode, $"Идентификатор FEM-объекта {id} повторяется."));
        }
        return result;
    }

    static void CheckFinite(int loadId, string component, double value, ICollection<FemValidationDiagnostic> errors)
    {
        if (!double.IsFinite(value))
            errors.Add(new("load_component_not_finite",
                $"Компонента {component} узловой нагрузки {loadId} не является конечным числом."));
    }
}
