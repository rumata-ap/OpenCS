namespace OpenCS.Reporting.Pandoc;

/// <summary>Экспортирует нейтральный отчёт в DOCX, ODT, RTF или PDF через локальный Pandoc.</summary>
public sealed class PandocReportExporter : IPandocReportExporter
{
    readonly PandocRuntimeLocator _runtimeLocator;
    readonly PandocReportSourceRenderer _sourceRenderer;
    readonly IPandocProcessRunner _processRunner;
    readonly string? _runtimeBaseDirectory;
    readonly TimeSpan _timeout;

    /// <summary>Создаёт экспортёр Pandoc.</summary>
    public PandocReportExporter(
        PandocRuntimeLocator? runtimeLocator = null,
        PandocReportSourceRenderer? sourceRenderer = null,
        IPandocProcessRunner? processRunner = null,
        string? runtimeBaseDirectory = null,
        TimeSpan? timeout = null)
    {
        _runtimeLocator = runtimeLocator ?? new PandocRuntimeLocator();
        _sourceRenderer = sourceRenderer ?? new PandocReportSourceRenderer();
        _processRunner = processRunner ?? new PandocProcessRunner();
        _runtimeBaseDirectory = runtimeBaseDirectory;
        _timeout = timeout ?? TimeSpan.FromMinutes(2);
    }

    /// <summary>Экспортирует отчёт в DOCX, ODT, RTF или PDF.</summary>
    public async Task ExportAsync(
        ReportDocument document,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        string fullPath = Path.GetFullPath(outputPath);
        string extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (extension is not (".docx" or ".odt" or ".rtf" or ".pdf"))
            throw new ArgumentException("Pandoc-экспорт поддерживает только DOCX, ODT, RTF и PDF.", nameof(outputPath));

        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("Не удалось определить каталог отчёта.", nameof(outputPath));
        Directory.CreateDirectory(directory);
        PandocRuntimePaths runtime = _runtimeLocator.Locate(_runtimeBaseDirectory);
        bool forDocx = extension == ".docx";
        bool forPdf = extension == ".pdf";
        _runtimeLocator.Validate(runtime, requireReferenceDoc: forDocx, requirePdfTools: forPdf);

        string sourceDirectory = Path.Combine(directory, $".opencs-report-{Guid.NewGuid():N}");
        string temporaryOutput = Path.Combine(directory,
            $".{Path.GetFileNameWithoutExtension(fullPath)}-{Guid.NewGuid():N}{extension}");
        bool keepSource = false;
        try
        {
            Directory.CreateDirectory(sourceDirectory);
            PandocReportSource source = await _sourceRenderer.RenderAsync(
                document, sourceDirectory, cancellationToken).ConfigureAwait(false);
            List<string> arguments = BuildArguments(runtime, source, temporaryOutput, extension);
            PandocProcessResult result = await _processRunner.RunAsync(
                runtime.PandocExe, arguments, source.Directory, _timeout, cancellationToken)
                .ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                keepSource = true;
                string details = string.IsNullOrWhiteSpace(result.StandardError)
                    ? string.Empty
                    : $" Подробности: {result.StandardError.Trim()}";
                throw new PandocExportException(
                    PandocExportFailureReason.ProcessFailed,
                    $"Pandoc завершился с кодом {result.ExitCode}.{details}",
                    tool: "pandoc.exe",
                    exitCode: result.ExitCode,
                    standardError: result.StandardError);
            }

            if (!File.Exists(temporaryOutput) || new FileInfo(temporaryOutput).Length == 0)
            {
                keepSource = true;
                throw new PandocExportException(
                    PandocExportFailureReason.InvalidOutput,
                    $"Pandoc не создал непустой файл результата: {temporaryOutput}",
                    tool: "pandoc.exe",
                    standardError: result.StandardError);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryOutput, fullPath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporaryOutput);
            if (!keepSource)
                TryDeleteDirectory(sourceDirectory);
        }
    }

    static List<string> BuildArguments(
        PandocRuntimePaths runtime,
        PandocReportSource source,
        string outputPath,
        string extension)
    {
        var arguments = new List<string>
        {
            "--standalone",
            "--from=markdown+tex_math_dollars+subscript+superscript+fenced_divs",
            Path.GetFileName(source.MarkdownPath),
            "--resource-path=.",
            "--lua-filter=" + runtime.LuaFilter,
            "-o",
            outputPath
        };
        switch (extension)
        {
            case ".docx":
                arguments.Add("--to=docx");
                arguments.Add("--reference-doc=" + runtime.ReferenceDocx);
                break;
            case ".odt":
                arguments.Add("--to=odt");
                break;
            case ".rtf":
                arguments.Add("--to=rtf");
                break;
            case ".pdf":
                arguments.Add("--to=pdf");
                arguments.Add("--template=" + runtime.TypstTemplate);
                arguments.Add("--pdf-engine=" + runtime.TypstExe);
                arguments.Add("--pdf-engine-opt=--font-path=" + runtime.FontDirectory);
                string pathRoot = Path.GetPathRoot(source.Directory)
                    ?? throw new InvalidOperationException("Не удалось определить корень диска для Typst.");
                arguments.Add("--pdf-engine-opt=--root=" + pathRoot.Replace('\\', '/'));
                break;
            default:
                throw new ArgumentException("Неподдерживаемое расширение Pandoc-экспорта.", nameof(extension));
        }
        return arguments;
    }

    static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
