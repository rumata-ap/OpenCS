namespace CScore.Batch;

/// <summary>Сводка причин по пакету: счётчики статусов, определяющая строка, статус пакета.</summary>
public static class BatchVerdictSummary
{
    /// <summary>
    /// Статус пакета: ok — все строки ok/not_applicable; not_passed — все сошлись, есть not_passed;
    /// partial — есть not_converged или error.
    /// </summary>
    public static string PackageStatus(IEnumerable<BatchRowVerdict> verdicts)
    {
        bool fail = false;
        foreach (var v in verdicts)
        {
            if (v.Status is BatchRowStatus.NotConverged or BatchRowStatus.Error) return "partial";
            if (v.Status == BatchRowStatus.NotPassed) fail = true;
        }
        return fail ? BatchRowStatus.NotPassed : BatchRowStatus.Ok;
    }

    /// <summary>Индекс строки с наибольшим Кисп; null — Кисп нет ни у одной строки.</summary>
    public static int? GoverningIndex(IReadOnlyList<BatchRowVerdict> verdicts)
    {
        int? best = null;
        for (int i = 0; i < verdicts.Count; i++)
            if (verdicts[i].Util is { } u && !double.IsNaN(u)
                && (best is null || u > verdicts[best.Value].Util!.Value))
                best = i;
        return best;
    }

    /// <summary>Объект verdict_summary для корня DataJson; nums — номера строк набора в порядке verdicts.</summary>
    public static object ToJson(IReadOnlyList<BatchRowVerdict> verdicts, IReadOnlyList<int> nums)
    {
        int Count(string status) => verdicts.Count(v => v.Status == status);
        int? g = GoverningIndex(verdicts);
        return new
        {
            ok = Count(BatchRowStatus.Ok),
            not_passed = Count(BatchRowStatus.NotPassed),
            not_converged = Count(BatchRowStatus.NotConverged),
            not_applicable = Count(BatchRowStatus.NotApplicable),
            error = Count(BatchRowStatus.Error),
            governing_num = g is { } i ? nums[i] : (int?)null,
        };
    }
}
