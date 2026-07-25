namespace OpenCS.OpenSees.Runtime;

/// <summary>Параметры одного внешнего запуска OpenSees.</summary>
public sealed class OpenSeesRunRequest
{
    /// <summary>Полный путь к исполняемому файлу.</summary>
    public string ExecutablePath { get; init; } = "";

    /// <summary>Аргументы процесса, передаваемые без shell-интерполяции.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Рабочий каталог процесса.</summary>
    public string WorkingDirectory { get; init; } = "";

    /// <summary>Путь к Tcl-файлу для стандартного OpenSees-запуска.</summary>
    public string? ScriptPath { get; init; }

    /// <summary>Максимальная длительность процесса.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Необязательный колбэк, вызываемый на каждую строку stdout/stderr процесса по мере
    /// её поступления (а не после завершения) — для живого лога хода расчёта в UI.</summary>
    public Action<string>? OnOutputLine { get; init; }

    /// <summary>Проверяет обязательные параметры запуска.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ExecutablePath))
            throw new ArgumentException("Не задан путь к OpenSees executable.", nameof(ExecutablePath));
        if (string.IsNullOrWhiteSpace(WorkingDirectory) || !Directory.Exists(WorkingDirectory))
            throw new ArgumentException("Рабочий каталог процесса не существует.", nameof(WorkingDirectory));
        if (Timeout <= TimeSpan.Zero)
            throw new ArgumentException("Timeout должен быть положительным.", nameof(Timeout));
    }
}
