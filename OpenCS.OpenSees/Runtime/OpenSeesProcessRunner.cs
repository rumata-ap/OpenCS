using System.Diagnostics;
using System.Text;
using OpenCS.ExternalTools;

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

        List<string> arguments = [.. request.Arguments];
        if (arguments.Count == 0 && !string.IsNullOrWhiteSpace(request.ScriptPath))
            arguments.Add(request.ScriptPath);

        // Режим Python-пакетов (консоль) строгий: путь к OpenSees.exe, пришедший в обход поиска (параметры задачи,
        // фрагменты), тоже исполняется через OpenSeesPy.
        string executable = request.ExecutablePath;
        if (!PythonToolCommand.TryParse(executable, out _) && ExternalToolPolicy.Current == ExternalToolSource.PythonPackages)
            executable = new OpenSeesExecutableResolver().Resolve(null).Path;

        ProcessStartInfo startInfo;
        if (PythonToolCommand.TryParse(executable, out PythonToolCommand? python))
        {
            // OpenSeesPy: тот же Tcl-сценарий через мост (OpenSeesPyBridge).
            string bridge = PythonRuntime.MaterializeScript(OpenSeesPyBridge.FileName, OpenSeesPyBridge.Script);
            startInfo = PythonRuntime.CreateStartInfo(python.PythonPath, bridge, arguments);
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }
        else
        {
            startInfo = new()
            {
                FileName = executable,
                WorkingDirectory = request.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (string argument in arguments)
                startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
        StringBuilder stdout = new();
        StringBuilder stderr = new();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            stdout.AppendLine(e.Data);
            request.OnOutputLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            stderr.AppendLine(e.Data);
            request.OnOutputLine?.Invoke(e.Data);
        };

        Stopwatch stopwatch = Stopwatch.StartNew();
        if (!process.Start())
            throw new InvalidOperationException("Не удалось запустить OpenSees process.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

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

        // Синхронный WaitForExit() (уже завершившийся процесс) гарантирует, что все буферизованные
        // асинхронные строки OutputDataReceived/ErrorDataReceived доставлены до возврата — в отличие
        // от WaitForExitAsync(), который такой гарантии не даёт.
        process.WaitForExit();
        stopwatch.Stop();

        return new OpenSeesRunResult
        {
            ExitCode = process.ExitCode,
            Stdout = stdout.ToString(),
            Stderr = stderr.ToString(),
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
