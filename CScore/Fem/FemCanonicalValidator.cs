using System.Text.Json;

namespace CScore.Fem;

/// <summary>Диагностическое сообщение проверки канонической FEM-модели.</summary>
public sealed record FemValidationDiagnostic(string Code, string Message, bool IsError = true);

/// <summary>Проверяет ссылки, идентификаторы и числовую корректность FEM-контрактов.</summary>
public static class FemCanonicalValidator
{
    static readonly HashSet<string> Sp20Types = new(StringComparer.OrdinalIgnoreCase)
    {
        "permanent", "long_term", "short_term", "accidental"
    };
    static readonly HashSet<string> MemberLoadCoordinateSystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "local", "global"
    };
    static readonly HashSet<string> MemberLoadDistributionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "uniform", "trapezoidal", "point"
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

        // Id=0 обозначает ещё не сохранённый объект (см. FemSchemaEditSession): несколько таких
        // объектов одновременно — законное состояние сессии редактирования, а не дубликат.
        var errors = new List<FemValidationDiagnostic>();
        var caseById = BuildUniqueIndex(loadCases, "load_case_id_duplicate", errors, skipZero: true);
        var nodeById = BuildUniqueIndex(nodes, "node_id_duplicate", errors, skipZero: true);
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

    /// <summary>Проверяет канонические распределённые нагрузки конструктивных стержней.</summary>
    public static IReadOnlyList<FemValidationDiagnostic> Validate(
        FemSchema schema,
        IReadOnlyList<FemLoadCase> loadCases,
        IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemNodeLoad> nodeLoads,
        IReadOnlyList<FemMember> members,
        IReadOnlyList<FemMemberLoad> memberLoads,
        IReadOnlyList<FemKinematicLoad>? kinematicLoads = null)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(memberLoads);
        kinematicLoads ??= [];

        var errors = Validate(schema, loadCases, nodes, nodeLoads).ToList();
        var caseById = loadCases.Where(loadCase => loadCase.Id != 0)
            .GroupBy(loadCase => loadCase.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var nodeById = nodes.Where(node => node.Id != 0)
            .GroupBy(node => node.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var memberById = BuildUniqueIndex(
            members, "member_id_duplicate", errors, skipZero: true);
        var nodeByTag = nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.NodeTag))
            .GroupBy(node => node.NodeTag, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        BuildUniqueIndex(memberLoads, "member_load_id_duplicate", errors, skipZero: true);
        BuildUniqueIndex(kinematicLoads, "kinematic_load_id_duplicate", errors, skipZero: true);

        foreach (var member in members)
        {
            if (member.SchemaId != schema.Id)
                errors.Add(new("member_schema_mismatch",
                    $"Стержень {member.Id} принадлежит схеме {member.SchemaId}, ожидалась {schema.Id}."));
        }

        foreach (var load in memberLoads)
        {
            if (load.SchemaId != schema.Id)
                errors.Add(new("member_load_schema_mismatch",
                    $"Распределённая нагрузка {load.Id} принадлежит схеме {load.SchemaId}, ожидалась {schema.Id}."));
            if (!caseById.ContainsKey(load.LoadCaseId))
                errors.Add(new("load_case_missing",
                    $"Распределённая нагрузка {load.Id} ссылается на отсутствующее загружение {load.LoadCaseId}."));
            if (!memberById.ContainsKey(load.MemberId))
                errors.Add(new("member_missing",
                    $"Распределённая нагрузка {load.Id} ссылается на отсутствующий стержень {load.MemberId}."));
            if (!MemberLoadCoordinateSystems.Contains(load.CoordinateSystem))
                errors.Add(new("member_load_coordinate_system_invalid",
                    $"Распределённая нагрузка {load.Id}: неизвестная система координат '{load.CoordinateSystem}'."));
            if (!MemberLoadDistributionTypes.Contains(load.DistributionType))
                errors.Add(new("member_load_distribution_invalid",
                    $"Распределённая нагрузка {load.Id}: неизвестный тип '{load.DistributionType}'."));

            if (!double.IsFinite(load.StartOffsetM) || !double.IsFinite(load.EndOffsetM) ||
                load.StartOffsetM < 0 || load.EndOffsetM < 0)
                errors.Add(new("member_load_offset_invalid",
                    $"Распределённая нагрузка {load.Id}: отступы должны быть конечными и неотрицательными."));

            bool isPoint = load.DistributionType.Equals("point", StringComparison.OrdinalIgnoreCase);
            if (isPoint && (!double.IsFinite(load.EndOffsetM) || load.EndOffsetM != 0))
                errors.Add(new("member_load_point_end_offset_invalid",
                    $"Сосредоточенная нагрузка {load.Id}: отступ от конца не используется и должен быть 0."));

            CheckMemberLoadFinite(load.Id, "QxStart", load.QxStart, errors);
            CheckMemberLoadFinite(load.Id, "QyStart", load.QyStart, errors);
            CheckMemberLoadFinite(load.Id, "QzStart", load.QzStart, errors);
            CheckMemberLoadFinite(load.Id, "QxEnd", load.QxEnd, errors);
            CheckMemberLoadFinite(load.Id, "QyEnd", load.QyEnd, errors);
            CheckMemberLoadFinite(load.Id, "QzEnd", load.QzEnd, errors);
            CheckMemberLoadFinite(load.Id, "Mx", load.Mx, errors);
            CheckMemberLoadFinite(load.Id, "My", load.My, errors);
            CheckMemberLoadFinite(load.Id, "Mz", load.Mz, errors);

            if (load.DistributionType.Equals("uniform", StringComparison.OrdinalIgnoreCase) &&
                (load.QxStart != load.QxEnd || load.QyStart != load.QyEnd || load.QzStart != load.QzEnd))
                errors.Add(new("member_load_uniform_end_mismatch",
                    $"Распределённая нагрузка {load.Id} типа uniform имеет разные интенсивности на концах."));

            if (memberById.TryGetValue(load.MemberId, out var member))
            {
                var nodeIds = JsonSerializer.Deserialize<int[]>(member.NodeIdsJson) ?? [];
                if (nodeIds.Length != 2 ||
                    !nodeByTag.TryGetValue(nodeIds[0].ToString(), out var nodeI) ||
                    !nodeByTag.TryGetValue(nodeIds[1].ToString(), out var nodeJ))
                {
                    errors.Add(new("member_load_member_topology_invalid",
                        $"Распределённая нагрузка {load.Id}: стержень {member.ElemTag} должен иметь два существующих узла."));
                }
                else
                {
                    double dx = nodeJ.X - nodeI.X, dy = nodeJ.Y - nodeI.Y, dz = nodeJ.Z - nodeI.Z;
                    double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    bool invalid = !double.IsFinite(length) ||
                        (isPoint ? load.StartOffsetM > length : load.StartOffsetM + load.EndOffsetM >= length);
                    if (invalid)
                        errors.Add(new("member_load_interval_invalid", isPoint
                            ? $"Сосредоточенная нагрузка {load.Id}: точка приложения должна быть в пределах длины стержня."
                            : $"Распределённая нагрузка {load.Id}: участок приложения должен быть меньше длины стержня."));
                }
            }
        }

        var kinematicDofs = new HashSet<(int LoadCaseId, int NodeId, int Dof)>();
        foreach (var load in kinematicLoads)
        {
            if (load.SchemaId != schema.Id)
                errors.Add(new("kinematic_load_schema_mismatch",
                    $"Кинематическая нагрузка {load.Id} принадлежит схеме {load.SchemaId}, ожидалась {schema.Id}."));
            if (!caseById.ContainsKey(load.LoadCaseId))
                errors.Add(new("load_case_missing",
                    $"Кинематическая нагрузка {load.Id} ссылается на отсутствующее загружение {load.LoadCaseId}."));
            if (!nodeById.ContainsKey(load.NodeId))
                errors.Add(new("node_missing",
                    $"Кинематическая нагрузка {load.Id} ссылается на отсутствующий узел {load.NodeId}."));
            if (load.Dof is < 1 or > 6)
                errors.Add(new("kinematic_dof_invalid",
                    $"Кинематическая нагрузка {load.Id}: DOF должен быть от 1 до 6."));
            if (!double.IsFinite(load.Value))
                errors.Add(new("kinematic_value_not_finite",
                    $"Значение кинематической нагрузки {load.Id} не является конечным числом."));
            if (!kinematicDofs.Add((load.LoadCaseId, load.NodeId, load.Dof)))
                errors.Add(new("kinematic_dof_duplicate",
                    $"Кинематическая нагрузка для загружения {load.LoadCaseId}, узла {load.NodeId}, DOF {load.Dof} задана повторно."));
            if (nodeById.TryGetValue(load.NodeId, out var node) && load.Dof is >= 1 and <= 6 &&
                (node.DofMask & (1 << (load.Dof - 1))) != 0 && load.Value != 0)
                errors.Add(new("kinematic_fixed_dof_conflict",
                    $"Кинематическая нагрузка {load.Id} конфликтует с закреплением узла {load.NodeId}, DOF {load.Dof}."));
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
                FemMember member => member.Id,
                FemMemberLoad load => load.Id,
                FemKinematicLoad load => load.Id,
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

    static void CheckMemberLoadFinite(int loadId, string component, double value,
        ICollection<FemValidationDiagnostic> errors)
    {
        if (!double.IsFinite(value))
            errors.Add(new("member_load_component_not_finite",
                $"Компонента {component} распределённой нагрузки {loadId} не является конечным числом."));
    }
}
