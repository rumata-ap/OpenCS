using System.Globalization;
using CScore.Batch;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Тон строки пакета для подсветки.</summary>
public enum BatchVerdictTone { Ok, NearLimit, Fail, NotConverged, NotApplicable, Error }

/// <summary>Фильтр строк окна пакета.</summary>
public enum BatchVerdictFilter { All, Failed, NotConverged }

/// <summary>Часть строки окна пакета, общая для всех видов: причина и её отображение.</summary>
public sealed class BatchVerdictView
{
    /// <summary>Порог подсветки «близко к пределу» (как у пакета предельных усилий).</summary>
    public const double NearLimitUtilization = 0.99;

    /// <summary>Пустая причина — для строк, у которых её нет.</summary>
    public static BatchVerdictView Empty { get; } = new(BatchRowVerdict.Converged());

    /// <summary>Создаёт отображение причины.</summary>
    public BatchVerdictView(BatchRowVerdict verdict) => Verdict = verdict;

    /// <summary>Причина строки.</summary>
    public BatchRowVerdict Verdict { get; }

    /// <summary>Ключ сортировки по Кисп: ∞ — выше всех, нет значения — ниже всех.</summary>
    public double SortUtil => Verdict.Util is not { } u || double.IsNaN(u) ? -1.0
        : double.IsPositiveInfinity(u) ? double.MaxValue : u;

    /// <summary>Кисп для таблицы: три знака, «∞» или «—».</summary>
    public string UtilText => Verdict.Util is not { } u || double.IsNaN(u) ? "—"
        : double.IsPositiveInfinity(u) ? "∞" : u.ToString("F3", CultureInfo.CurrentCulture);

    /// <summary>Подстановка; пусто — локализованный статус.</summary>
    public string ReasonText => Verdict.Text.Length > 0 ? Verdict.Text : StatusText;

    /// <summary>Пункт нормы (подсказка к причине).</summary>
    public string RefText => Verdict.Ref;

    /// <summary>Локализованный статус строки.</summary>
    public string StatusText => Loc.S("BatchVerdictStatus_" + Verdict.Status, Verdict.Status);

    /// <summary>Тон подсветки строки.</summary>
    public BatchVerdictTone Tone => Verdict.Status switch
    {
        BatchRowStatus.NotPassed => BatchVerdictTone.Fail,
        BatchRowStatus.NotConverged => BatchVerdictTone.NotConverged,
        BatchRowStatus.NotApplicable => BatchVerdictTone.NotApplicable,
        BatchRowStatus.Error => BatchVerdictTone.Error,
        _ => Verdict.Util is { } u && u >= NearLimitUtilization ? BatchVerdictTone.NearLimit : BatchVerdictTone.Ok,
    };
}

/// <summary>Отбор строк окна пакета по причине.</summary>
public static class BatchVerdictFilters
{
    /// <summary>Строка проходит фильтр: «Не проходят» — not_passed, «Не сошлись» — not_converged и error.</summary>
    public static bool Match(BatchVerdictView v, BatchVerdictFilter filter) => filter switch
    {
        BatchVerdictFilter.Failed => v.Verdict.Status == BatchRowStatus.NotPassed,
        BatchVerdictFilter.NotConverged => v.Verdict.Status is BatchRowStatus.NotConverged or BatchRowStatus.Error,
        _ => true,
    };
}
