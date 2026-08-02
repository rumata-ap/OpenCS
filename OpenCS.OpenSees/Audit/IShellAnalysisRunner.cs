using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Audit;

/// <summary>Исход audit-запуска shell-расчёта. Решение принимается по exit code,
/// таймауту, ошибке парсинга и сходимости шагов, а не только по Status.</summary>
public enum ShellAnalysisOutcome
{
    /// <summary>Расчёт завершился, парсинг успешен и есть сошедшийся шаг.</summary>
    Completed,

    /// <summary>Процесс завершился без ошибок, но ни один шаг не сошёлся.</summary>
    NotConverged,

    /// <summary>Файлы результата не удалось распарсить.</summary>
    ParseFailed,

    /// <summary>OpenSees завершился с ненулевым exit code.</summary>
    ExecutionFailed,

    /// <summary>Процесс завершился по таймауту.</summary>
    TimedOut
}

/// <summary>Результат audit-запуска: outcome, parsed result, каталог артефактов и ошибка.</summary>
public sealed record ShellAnalysisRunResult(
    ShellAnalysisOutcome Outcome,
    ShellResult? Result,
    string? ArtifactDirectory,
    string? ErrorMessage);

/// <summary>Запускает shell-расчёт через генерацию Tcl, OpenSees process runner и parser.</summary>
public interface IShellAnalysisRunner
{
    /// <summary>Выполняет расчёт модели и возвращает типизированный исход.</summary>
    Task<ShellAnalysisRunResult> RunAsync(
        ShellOpenSeesModel model,
        string executablePath,
        CancellationToken cancellationToken);
}
