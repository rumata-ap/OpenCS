using System.Security.Cryptography;
using System.Text.Json;
using OpenCS.Reporting.Pandoc;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверяет выбор прямых Pandoc-форматов без запуска реального процесса.</summary>
public sealed class PandocReportExporterTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("opencs-pandoc-unit-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Theory]
    [InlineData(".odt", "odt")]
    [InlineData(".rtf", "rtf")]
    public async Task Export_UsesDirectWriterWithoutTypstOrReferenceDoc(
        string extension, string format)
    {
        string runtimeBase = CreateRuntime(includePdfAssets: false);
        var runner = new RecordingProcessRunner();
        string outputPath = Path.Combine(_root, "report" + extension);

        await new PandocReportExporter(
            processRunner: runner,
            runtimeBaseDirectory: runtimeBase)
            .ExportAsync(Document(), outputPath);

        Assert.Contains("--to=" + format, runner.Arguments);
        Assert.DoesNotContain(runner.Arguments, argument =>
            argument.StartsWith("--reference-doc=", StringComparison.Ordinal));
        Assert.DoesNotContain(runner.Arguments, argument =>
            argument.StartsWith("--pdf-engine", StringComparison.Ordinal));
        Assert.Equal("generated", await File.ReadAllTextAsync(outputPath));
    }

    sealed class RecordingProcessRunner : IPandocProcessRunner
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public Task<PandocProcessResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Arguments = arguments.ToArray();
            int outputIndex = Array.IndexOf(arguments.ToArray(), "-o");
            File.WriteAllText(arguments[outputIndex + 1], "generated");
            return Task.FromResult(new PandocProcessResult(0, "", ""));
        }
    }

    string CreateRuntime(bool includePdfAssets)
    {
        string root = Path.Combine(_root, "Tools", "Pandoc");
        Directory.CreateDirectory(Path.Combine(root, "fonts"));
        var files = new Dictionary<string, string>
        {
            ["pandoc.exe"] = "pandoc",
            ["report-filter.lua"] = "filter"
        };
        if (includePdfAssets)
        {
            files["typst.exe"] = "typst";
            files["reference.docx"] = "reference";
            files["report.typ"] = "template";
        }

        foreach ((string relative, string content) in files)
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllText(path, content);
        }

        var hashes = files.ToDictionary(
            pair => pair.Key,
            pair => Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(Path.Combine(root,
                    pair.Key.Replace('/', Path.DirectorySeparatorChar))))));
        File.WriteAllText(Path.Combine(root, "manifest.json"),
            JsonSerializer.Serialize(new { files = hashes }));
        return _root;
    }

    static ReportDocument Document() => new ReportDocument("Отчёт")
        .Add(new ReportParagraph("Проверка формата."));
}
