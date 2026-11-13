using System.Text.Json;
using CScore;
using CScore.Batch;

namespace OpenCS.ViewModels;

internal static class BatchResultRowHelper
{
    /// <summary>Номер строки набора усилий; для старых результатов — порядковый номер в таблице.</summary>
    public static int RowNum(JsonElement row, int fallbackIndex)
        => row.TryGetProperty("num", out var v) && v.ValueKind == JsonValueKind.Number
           ? v.GetInt32()
           : fallbackIndex;

    /// <summary>Причина строки пакета (сохранённая или восстановленная для старых результатов).</summary>
    public static BatchVerdictView Verdict(CalcResult result, JsonElement row)
        => new(BatchRowVerdictReader.Read(result.TaskKind, row));
}
