using System.Diagnostics;

namespace OpenCS.Reporting;

/// <summary>Запускает bundled CalcpadCE CLI для преобразования worksheet в DOCX или PDF.</summary>
public sealed class CalcpadCliRunner
{
    readonly string? _explicitExecutablePath;
    readonly TimeSpan _timeout;

    /// <summary>Создаёт runner с необязательным явным путём к Calcpad.Cli.</summary>
    public CalcpadCliRunner(string? executablePath = null, TimeSpan? timeout = null)
    {
        _explicitExecutablePath = executablePath;
        _timeout = timeout ?? TimeSpan.FromSeconds(120);
    }

    /// <summary>Возвращает первый существующий CLI из переданного и bundled путей.</summary>
    public string? ResolveExecutable()
    {
        var candidates = new[]
        {
            _explicitExecutablePath,
            Environment.GetEnvironmentVariable("OPENCS_CALCPAD_CLI"),
            Path.Combine(AppContext.BaseDirectory, "Reporting", "CalcpadCE", "Cli.exe"),
            Path.Combine(AppContext.BaseDirectory, "Reporting", "CalcpadCE", "Calcpad.Cli.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Calcpad", "Cli.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Calcpad", "Cli.exe")
        };
        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    /// <summary>Экспортирует отчёт в DOCX или PDF через CalcpadCE.</summary>
    public async Task ExportAsync(ReportDocument document, string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        string extension = Path.GetExtension(outputPath).ToLowerInvariant();
        if (extension is not ".docx" and not ".pdf")
            throw new ArgumentException("CalcpadCE экспортирует отчёт только в DOCX или PDF.", nameof(outputPath));

        string? executable = ResolveExecutable();
        if (executable == null)
            throw new FileNotFoundException("Bundled CalcpadCE CLI не найден.");

        string fullOutputPath = Path.GetFullPath(outputPath);
        string? outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        string workDirectory = Path.Combine(Path.GetTempPath(), "OpenCS-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);
        string inputPath = Path.Combine(workDirectory, "report.cpd");
        string generatedPath = Path.Combine(workDirectory, "report" + extension);
        await File.WriteAllTextAsync(inputPath, new CalcpadWorksheetBuilder().Build(document), cancellationToken);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add(inputPath);
            startInfo.ArgumentList.Add(generatedPath);
            startInfo.ArgumentList.Add("-s");

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
                throw new InvalidOperationException("Не удалось запустить CalcpadCE CLI.");

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_timeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException($"CalcpadCE не завершился за {_timeout.TotalSeconds:0} с.");
            }

            string stdout = await stdoutTask;
            string stderr = await stderrTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"CalcpadCE завершился с кодом {process.ExitCode}. {stderr} {stdout}".Trim());
            if (!File.Exists(generatedPath))
                throw new InvalidOperationException("CalcpadCE не создал файл отчёта.");

            File.Copy(generatedPath, fullOutputPath, overwrite: true);
        }
        finally
        {
            try { Directory.Delete(workDirectory, recursive: true); } catch { }
        }
    }
}
