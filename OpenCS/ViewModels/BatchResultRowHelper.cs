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

    /// <summary>
    /// Счётчики сводки окна по причинам строк: прошло, не прошло, не сошлось (вместе с ошибками).
    /// «Не сошлось» не входит в «не прошло» — как в панели фильтра и отчёте пакета.
    /// </summary>
    public static (int Passed, int NotPassed, int NotConverged) Counts(IEnumerable<BatchVerdictView> reasons)
    {
        int ok = 0, fail = 0, conv = 0;
        foreach (var r in reasons)
            switch (r.Verdict.Status)
            {
                case BatchRowStatus.Ok: ok++; break;
                case BatchRowStatus.NotPassed: fail++; break;
                case BatchRowStatus.NotConverged or BatchRowStatus.Error: conv++; break;
            }
        return (ok, fail, conv);
    }
}
