using System.Text.Json;

namespace CScore.Fem.Fragment;

/// <summary>Отчёт сборки фрагмента в БД: диагностика построителя ↔ JSON (<see cref="FemSchema.FragmentBuildReportJson"/>).</summary>
public static class StoreyFragmentBuildReport
{
    sealed record Row(string Code, string Message, bool IsError, IReadOnlyList<string>? Keys);

    public static string ToJson(IEnumerable<FemValidationDiagnostic> diagnostics) =>
        JsonSerializer.Serialize(diagnostics.Select(d => new Row(d.Code, d.Message, d.IsError, d.SourceKeys)).ToList());

    /// <summary>Диагностика из JSON; пусто или битый JSON — пустой список.</summary>
    public static List<FemValidationDiagnostic> FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return (JsonSerializer.Deserialize<List<Row>>(json) ?? [])
                .Select(r => new FemValidationDiagnostic(r.Code, r.Message, r.IsError, r.Keys)).ToList();
        }
        catch (JsonException) { return []; }
    }
}
