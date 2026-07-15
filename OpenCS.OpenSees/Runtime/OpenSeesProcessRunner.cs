using System.Diagnostics;

namespace OpenCS.OpenSees.Runtime;

/// <summary>Запускает OpenSees без shell-интерполяции и сохраняет диагностику процесса.</summary>
public sealed class OpenSeesProcessRunner : IOpenSeesProcessRunner
{
    /// <inheritdoc />
    public async Task<OpenSeesRunResult> RunAsync(
        OpenSeesRunRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        ProcessStartInfo startInfo = new()
        {
            FileName = request.ExecutablePath,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (string argument in request.Arguments)
            startInfo.ArgumentList.Add(argument);
        if (request.Arguments.Count == 0 && !string.IsNullOrWhiteSpace(request.ScriptPath))
            startInfo.ArgumentList.Add(request.ScriptPath);

        using Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
        Stopwatch stopwatch = Stopwatch.StartNew();
        if (!process.Start())
            throw new InvalidOperationException("Не удалось запустить OpenSees process.");

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeoutCancellation = new(request.Timeout);
        using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);

        bool timedOut = false;
        bool cancelled = false;
        try
        {
            await process.WaitForExitAsync(linkedCancellation.Token);
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            timedOut = timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            cancelled = cancellationToken.IsCancellationRequested;
            KillProcessTree(process);
            await process.WaitForExitAsync();
        }

        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        stopwatch.Stop();

        return new OpenSeesRunResult
        {
            ExitCode = process.ExitCode,
            Stdout = stdout,
            Stderr = stderr,
            Duration = stopwatch.Elapsed,
            TimedOut = timedOut,
            Cancelled = cancelled
        };
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Процесс мог завершиться между проверкой и Kill().
        }
    }
}
