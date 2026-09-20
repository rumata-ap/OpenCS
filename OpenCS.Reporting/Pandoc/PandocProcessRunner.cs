using System.Diagnostics;

namespace OpenCS.Reporting.Pandoc;

/// <summary>Результат завершения внешнего отчётного инструмента.</summary>
public sealed record PandocProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Контракт запуска внешнего процесса отчётности.</summary>
public interface IPandocProcessRunner
{
    /// <summary>Запускает процесс без shell с заданными аргументами и ограничением времени.</summary>
    Task<PandocProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

/// <summary>Запускает Pandoc или Typst без использования системного shell.</summary>
public sealed class PandocProcessRunner : IPandocProcessRunner
{
    /// <inheritdoc />
    public async Task<PandocProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Process.Start вернул false.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new PandocExportException(
                PandocExportFailureReason.ProcessStartFailed,
                $"Не удалось запустить отчётный инструмент: {executablePath}",
                tool: Path.GetFileName(executablePath),
                inner: ex);
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Kill(process);
            throw new PandocExportException(
                PandocExportFailureReason.TimedOut,
                $"Отчётный инструмент не завершился за {timeout.TotalSeconds:G17} с: {executablePath}",
                tool: Path.GetFileName(executablePath));
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }

        return new PandocProcessResult(
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false));
    }

    static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
