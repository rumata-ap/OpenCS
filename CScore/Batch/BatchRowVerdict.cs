using System.Text.Json;

namespace CScore.Batch;

/// <summary>Статусы строки пакета (поле verdict.status).</summary>
public static class BatchRowStatus
{
    /// <summary>Проверка выполнена (или расчёт без нормативного критерия сошёлся).</summary>
    public const string Ok = "ok";
    /// <summary>Решатель сошёлся, но критерий не выполнен (Кисп &gt; 1).</summary>
    public const string NotPassed = "not_passed";
    /// <summary>Решатель не нашёл состояние — проверка не выполнялась.</summary>
    public const string NotConverged = "not_converged";
    /// <summary>Строка вне области применимости метода.</summary>
    public const string NotApplicable = "not_applicable";
    /// <summary>Исключение при расчёте строки.</summary>
    public const string Error = "error";
}

/// <summary>
/// Один критерий строки: стабильный код, Кисп (null — значения нет, +∞ — нулевая несущая
/// способность), пункт нормы и подстановка (русский литерал).
/// </summary>
public readonly record struct BatchCriterion(string Code, double? Util, string Ref, string Text);

/// <summary>
/// «Причина» строки пакета: статус, Кисп, код, пункт нормы и подстановка определяющего критерия.
/// Пишется в DataJson строки как объект <c>verdict</c>.
/// </summary>
public sealed record BatchRowVerdict(string Status, double? Util, string Code, string Ref, string Text)
{
    /// <summary>Порог непрохождения: Кисп строго больше.</summary>
    public const double Threshold = 1.0;

    /// <summary>Строка проверку выполнила.</summary>
    public bool IsPassed => Status == BatchRowStatus.Ok;

    /// <summary>Определяющий критерий — наибольший Кисп (+∞ выше любого, null ниже любого); статус по порогу.</summary>
    public static BatchRowVerdict Worst(params BatchCriterion[] criteria)
    {
        if (criteria.Length == 0) return Converged();
        var worst = criteria[0];
        foreach (var c in criteria)
            if ((c.Util ?? double.NegativeInfinity) > (worst.Util ?? double.NegativeInfinity)) worst = c;
        bool failed = worst.Util is { } u && (double.IsNaN(u) || u > Threshold);
        return new(failed ? BatchRowStatus.NotPassed : BatchRowStatus.Ok, worst.Util, worst.Code, worst.Ref, worst.Text);
    }

    /// <summary>Решатель не сошёлся.</summary>
    public static BatchRowVerdict NotConverged(string text) => new(BatchRowStatus.NotConverged, null, "", "", text);

    /// <summary>Строка вне области применимости.</summary>
    public static BatchRowVerdict NotApplicable(string code, string text) => new(BatchRowStatus.NotApplicable, null, code, "", text);

    /// <summary>Ошибка расчёта строки.</summary>
    public static BatchRowVerdict Error(string text) => new(BatchRowStatus.Error, null, "", "", text);

    /// <summary>Расчёт без нормативного критерия сошёлся.</summary>
    public static BatchRowVerdict Converged() => new(BatchRowStatus.Ok, null, "", "", "");

    /// <summary>Объект для System.Text.Json; +∞ пишется как util = null и util_inf = true (JSON без Infinity).</summary>
    public object ToJson()
    {
        bool inf = Util is { } u && double.IsPositiveInfinity(u);
        double? util = Util is { } x && double.IsFinite(x) ? Math.Round(x, 4) : null;
        return new { status = Status, util, util_inf = inf, code = Code, @ref = Ref, text = Text };
    }

    /// <summary>Читает объект row.verdict; null — поля нет (результат до появления причины).</summary>
    public static BatchRowVerdict? TryRead(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object
            || !row.TryGetProperty("verdict", out var v) || v.ValueKind != JsonValueKind.Object)
            return null;
        double? util = v.TryGetProperty("util", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetDouble() : null;
        if (v.TryGetProperty("util_inf", out var inf) && inf.ValueKind == JsonValueKind.True)
            util = double.PositiveInfinity;
        return new(Str(v, "status"), util, Str(v, "code"), Str(v, "ref"), Str(v, "text"));
    }

    static string Str(JsonElement e, string key)
        => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
