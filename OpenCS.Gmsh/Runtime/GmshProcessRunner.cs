using System.Diagnostics;
using System.Text.RegularExpressions;

namespace OpenCS.Gmsh.Runtime;

public sealed class GmshProcessTimeoutException : TimeoutException
{
    public GmshProcessTimeoutException(string output, string error) : base("Gmsh process timed out.")
    {
        Output = output;
        Error = error;
    }

    public string Output { get; }
    public string Error { get; }
}

/// <summary>Запуск внешнего gmsh.exe и чтение его версии — переиспользуется полной сборкой сетки
/// (GmshPlanarMesher) и дешёвой проверкой актуальности (staleness check в PlanarRegionMemberVM).</summary>
public static class GmshProcessRunner
{
    public static async Task<string> ReadVersionAsync(string executable, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var result = await RunAsync(executable, Environment.CurrentDirectory, ["-version"], timeout, cancellationToken);
        var output = result.Output + Environment.NewLine + result.Error;
        if (result.ExitCode != 0)
            throw new IOException($"Не удалось определить версию Gmsh: код {result.ExitCode}.");
        var match = Regex.Match(output, @"\b\d+\.\d+(?:\.\d+)?\b");
        if (!match.Success)
            throw new InvalidDataException("Gmsh не вернул распознаваемую версию.");
        return match.Value;
    }

    public static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("Не удалось запустить Gmsh.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { await process.WaitForExitAsync(); } catch { }
            throw new GmshProcessTimeoutException(await output, await error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { await process.WaitForExitAsync(); } catch { }
            throw;
        }
        return (process.ExitCode, await output, await error);
    }
}
