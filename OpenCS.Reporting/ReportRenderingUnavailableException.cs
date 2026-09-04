namespace OpenCS.Reporting;

/// <summary>Причина отказа движка рендеринга отчёта.</summary>
public enum ReportRenderingFailureReason
{
    /// <summary>Отсутствует установленный runtime движка.</summary>
    RuntimeMissing,
    /// <summary>Навигация к содержимому не удалась.</summary>
    NavigationFailed,
    /// <summary>Печать или захват изображения завершились ошибкой.</summary>
    RenderFailed,
    /// <summary>Операция не уложилась во внутренний таймаут.</summary>
    TimedOut
}

/// <summary>Единый тип ошибки движка рендеринга независимо от места отказа.
/// <see cref="Exception.Message"/> — техническое диагностическое сообщение для логов,
/// а не локализованный текст для пользователя: portable-библиотека не хардкодит UI-строки,
/// локализованное сообщение собирает WPF-слой по значению <see cref="Reason"/>.</summary>
public sealed class ReportRenderingUnavailableException : Exception
{
    /// <summary>Причина отказа.</summary>
    public ReportRenderingFailureReason Reason { get; }

    /// <summary>URL установки runtime; задан только для <see cref="ReportRenderingFailureReason.RuntimeMissing"/>.</summary>
    public string? RuntimeDownloadUrl { get; }

    /// <summary>Создаёт исключение движка рендеринга.</summary>
    public ReportRenderingUnavailableException(
        ReportRenderingFailureReason reason,
        string debugMessage,
        string? runtimeDownloadUrl = null,
        Exception? inner = null)
        : base(debugMessage, inner)
    {
        Reason = reason;
        RuntimeDownloadUrl = runtimeDownloadUrl;
    }
}
