namespace OpenCS.OpenSees.Runtime;

/// <summary>Результат запуска внешнего OpenSees-процесса.</summary>
public sealed class OpenSeesRunResult
{
    /// <summary>Код завершения процесса.</summary>
    public int ExitCode { get; init; }

    /// <summary>Перехваченный stdout.</summary>
    public string Stdout { get; init; } = "";

    /// <summary>Перехваченный stderr.</summary>
    public string Stderr { get; init; } = "";

    /// <summary>Фактическая длительность процесса.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Процесс завершён по таймауту.</summary>
    public bool TimedOut { get; init; }

    /// <summary>Процесс завершён из-за внешней отмены.</summary>
    public bool Cancelled { get; init; }
}
