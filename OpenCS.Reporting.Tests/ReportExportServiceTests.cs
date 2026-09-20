using OpenCS.Reporting;
using OpenCS.Reporting.Pandoc;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверки маршрутизации форматов, атомарной записи и очистки временных файлов.</summary>
public sealed class ReportExportServiceTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("opencs-export-tests-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    sealed class ThrowingPandocExporter : IPandocReportExporter
    {
        public Task ExportAsync(ReportDocument document, string outputPath, CancellationToken ct = default)
        {
            File.WriteAllText(outputPath, "частично записанный файл");
            throw new InvalidOperationException("сбой печати");
        }
    }

    sealed class StubPandocExporter : IPandocReportExporter
    {
        readonly string _payload;

        public StubPandocExporter(string payload = "stub") => _payload = payload;

        public Task ExportAsync(ReportDocument document, string outputPath, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            File.WriteAllText(outputPath, _payload);
            return Task.CompletedTask;
        }
    }

    sealed class RecordingPandocExporter : IPandocReportExporter
    {
        public string? LastOutputPath { get; private set; }

        public Task ExportAsync(ReportDocument document, string outputPath,
            CancellationToken ct = default)
        {
            LastOutputPath = outputPath;
            File.WriteAllText(outputPath, "stub");
            return Task.CompletedTask;
        }
    }

    static ReportDocument Document() => new ReportDocument("Отчёт")
        .Add(new ReportParagraph("Текст"));

    [Theory]
    [InlineData(".html")]
    [InlineData(".htm")]
    public async Task Export_WritesHtml(string extension)
    {
        string path = Path.Combine(_dir, "sub", "report" + extension);
        await new ReportExportService().ExportAsync(Document(), path);

        Assert.StartsWith("<!doctype html>", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public async Task Export_WritesMarkdown()
    {
        string path = Path.Combine(_dir, "report.md");
        await new ReportExportService().ExportAsync(Document(), path);

        Assert.StartsWith("# Отчёт", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Export_WritesDocx_WithoutRasterizer_WhenNoSvgImages()
    {
        string path = Path.Combine(_dir, "report.docx");
        await new ReportExportService(pandocExporter: new StubPandocExporter("PK stub"))
            .ExportAsync(Document(), path);

        byte[] bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal("PK stub", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Theory]
    [InlineData(".odt")]
    [InlineData(".rtf")]
    public async Task Export_WritesAdditionalPandocFormat(string extension)
    {
        string path = Path.Combine(_dir, "report" + extension);
        await new ReportExportService(pandocExporter: new StubPandocExporter("format stub"))
            .ExportAsync(Document(), path);

        Assert.Equal("format stub", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Export_WritesPdf_ThroughConverter()
    {
        string path = Path.Combine(_dir, "report.pdf");
        await new ReportExportService(pandocExporter: new StubPandocExporter("%PDF-1.7 stub"))
            .ExportAsync(Document(), path);

        Assert.StartsWith("%PDF-", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Export_PandocReceivesTemporaryPathWithTargetExtension()
    {
        string path = Path.Combine(_dir, "report.pdf");
        var exporter = new RecordingPandocExporter();

        await new ReportExportService(pandocExporter: exporter).ExportAsync(Document(), path);

        Assert.Equal(".pdf", Path.GetExtension(exporter.LastOutputPath));
    }

    [Fact]
    public async Task Export_UnknownExtension_ThrowsAndCreatesNothing()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => new ReportExportService().ExportAsync(Document(), Path.Combine(_dir, "nope", "r.xyz")));

        Assert.Contains("ODT", ex.Message);
        Assert.False(Directory.Exists(Path.Combine(_dir, "nope")));
    }

    [Fact]
    public async Task Export_CleansTempFile_AndKeepsExistingTarget_OnRendererFailure()
    {
        string path = Path.Combine(_dir, "report.pdf");
        await File.WriteAllTextAsync(path, "прежний файл");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ReportExportService(pandocExporter: new ThrowingPandocExporter())
                .ExportAsync(Document(), path));

        Assert.Equal("прежний файл", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task Export_CleansTempFile_OnCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ReportExportService(pandocExporter: new StubPandocExporter())
                .ExportAsync(Document(), Path.Combine(_dir, "report.pdf"), cts.Token));

        Assert.Empty(Directory.GetFiles(_dir));
    }
}
