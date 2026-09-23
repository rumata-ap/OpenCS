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
        // Gmsh при старте читает личные настройки пользователя (gmsh-options/gmshrc из GMSH_HOME,
        // иначе из APPDATA/HOME), и они перекрывают умолчания, на которые рассчитан .geo OpenCS
        // (например, Mesh.RecombineAll = 1 превращает треугольную сетку в смешанную). Пустой
        // GMSH_HOME на каждый запуск делает сетку независимой от машины.
        var isolatedHome = Path.Combine(Path.GetTempPath(), "opencs-gmsh-home", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedHome);
        info.Environment["GMSH_HOME"] = isolatedHome;
        try
        {
            return await RunProcessAsync(info, timeout, cancellationToken);
        }
        finally
        {
            try { Directory.Delete(isolatedHome, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    static async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(
        ProcessStartInfo info,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
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
