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
        if (!WindowsShortPath.IsAsciiSafe(WorkingDirectory))
            throw new ArgumentException(
                "Каталог артефактов OpenSees содержит символы вне ASCII (например, кириллицу) — " +
                "OpenSees.exe не может открыть по нему файлы, а автоматический переход на короткое " +
                "8.3-имя пути не удался (вероятно, отключены на этом томе). Измените каталог артефактов " +
                "OpenSees в настройках на путь без кириллицы/юникода.", nameof(WorkingDirectory));
        if (ScriptPath is not null && !WindowsShortPath.IsAsciiSafe(ScriptPath))
            throw new ArgumentException(
                "Путь к Tcl-сценарию OpenSees содержит символы вне ASCII (например, кириллицу) — " +
                "OpenSees.exe не может его открыть. Измените каталог артефактов OpenSees в настройках " +
                "на путь без кириллицы/юникода.", nameof(ScriptPath));
        if (Timeout <= TimeSpan.Zero)
            throw new ArgumentException("Timeout должен быть положительным.", nameof(Timeout));
    }
}
