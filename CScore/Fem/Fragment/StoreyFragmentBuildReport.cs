using System.Text.Json;

namespace CScore.Fem.Fragment;

/// <summary>
/// Отчёт сборки фрагмента (и снятия масок) в БД: диагностика ↔ JSON (<see cref="FemSchema.FragmentBuildReportJson"/>).
/// Переводимый текст хранится ключом и аргументами (<see cref="DiagText"/>) — старая запись показывается на языке
/// интерфейса; записи без него (до перевода) — по русскому <c>Message</c>.
/// </summary>
public static class StoreyFragmentBuildReport
{
    sealed record Row(string Code, string Message, bool IsError, IReadOnlyList<string>? Keys, DiagText? Text = null);

    public static string ToJson(IEnumerable<FemValidationDiagnostic> diagnostics) =>
        JsonSerializer.Serialize(diagnostics.Select(d => new Row(d.Code, d.Message, d.IsError, d.SourceKeys, d.Text)).ToList());

    /// <summary>Диагностика из JSON; пусто или битый JSON — пустой список.</summary>
    public static List<FemValidationDiagnostic> FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return (JsonSerializer.Deserialize<List<Row>>(json) ?? [])
                .Select(r => new FemValidationDiagnostic(r.Code, r.Message, r.IsError, r.Keys, r.Text)).ToList();
        }
        catch (JsonException) { return []; }
    }
}
