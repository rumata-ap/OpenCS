namespace CScore.Fem;

/// <summary>Проверяет ссылочную и числовую корректность определений нагрузок FEM-схемы.</summary>
public static class FemLoadDefinitionValidator
{
    /// <summary>Возвращает все ошибки определений в пределах одной схемы.</summary>
    public static IReadOnlyList<FemValidationDiagnostic> Validate(
        FemSchema schema,
        IReadOnlyList<FemLoadDefinition> definitions,
        IReadOnlyList<FemLoadCase> loadCases)
    {
        var diagnostics = new List<FemValidationDiagnostic>();
        var tags = new HashSet<string>(StringComparer.Ordinal);
        var knownLoadCases = loadCases
            .Where(loadCase => loadCase.SchemaId == schema.Id)
            .Select(loadCase => loadCase.Id)
            .ToHashSet();

        foreach (var definition in definitions)
        {
            if (definition.SchemaId != schema.Id)
                diagnostics.Add(new("load_definition_schema_mismatch",
                    $"Определение нагрузки {definition.Id} принадлежит схеме {definition.SchemaId}, ожидалась {schema.Id}."));

            if (!tags.Add(definition.Tag.Trim()))
                diagnostics.Add(new("load_definition_tag_duplicate",
                    $"Имя определения нагрузки '{definition.Tag}' повторяется в схеме."));

            foreach (var term in definition.GetExpression().Terms)
            {
                if (!double.IsFinite(term.Coefficient))
                    diagnostics.Add(new("load_definition_coefficient_not_finite",
                        $"Коэффициент загружения {term.LoadCaseId} в определении '{definition.Tag}' не является конечным числом."));

                if (!knownLoadCases.Contains(term.LoadCaseId))
                    diagnostics.Add(new("load_definition_load_case_missing",
                        $"Определение нагрузки '{definition.Tag}' ссылается на отсутствующее загружение {term.LoadCaseId}."));
            }
        }

        return diagnostics;
    }
}
