using System.Text.Json;

namespace OpenCS.ViewModels;

internal static class BatchResultRowHelper
{
    /// <summary>Номер строки набора усилий; для старых результатов — порядковый номер в таблице.</summary>
    public static int RowNum(JsonElement row, int fallbackIndex)
        => row.TryGetProperty("num", out var v) && v.ValueKind == JsonValueKind.Number
           ? v.GetInt32()
           : fallbackIndex;
}
