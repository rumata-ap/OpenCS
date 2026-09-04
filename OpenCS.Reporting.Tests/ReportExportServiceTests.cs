using OpenCS.Reporting;
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

    sealed class ThrowingPdfConverter : IHtmlToPdfConverter
    {
        public Task ConvertAsync(string html, string outputPdfPath, CancellationToken ct = default)
        {
            File.WriteAllText(outputPdfPath, "частично записанный PDF");
            throw new InvalidOperationException("сбой печати");
        }
    }

    sealed class StubPdfConverter : IHtmlToPdfConverter
    {
        public Task ConvertAsync(string html, string outputPdfPath, CancellationToken ct = default)
        {
            File.WriteAllText(outputPdfPath, "%PDF-1.7 stub");
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
        await new ReportExportService().ExportAsync(Document(), path);

        byte[] bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal([0x50, 0x4B], bytes.Take(2).ToArray());       // ZIP-сигнатура
    }

    [Fact]
    public async Task Export_WritesPdf_ThroughConverter()
    {
        string path = Path.Combine(_dir, "report.pdf");
        await new ReportExportService(pdfConverter: new StubPdfConverter())
            .ExportAsync(Document(), path);

        Assert.StartsWith("%PDF-", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Export_Pdf_WithoutConverter_Throws()
        => await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ReportExportService().ExportAsync(Document(), Path.Combine(_dir, "r.pdf")));

    [Fact]
    public async Task Export_UnknownExtension_ThrowsAndCreatesNothing()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => new ReportExportService().ExportAsync(Document(), Path.Combine(_dir, "nope", "r.rtf")));

        Assert.Contains("Markdown", ex.Message);
        Assert.False(Directory.Exists(Path.Combine(_dir, "nope")));
    }

    [Fact]
    public async Task Export_CleansTempFile_AndKeepsExistingTarget_OnRendererFailure()
    {
        string path = Path.Combine(_dir, "report.pdf");
        await File.WriteAllTextAsync(path, "прежний файл");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ReportExportService(pdfConverter: new ThrowingPdfConverter())
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
            () => new ReportExportService(pdfConverter: new StubPdfConverter())
                .ExportAsync(Document(), Path.Combine(_dir, "report.pdf"), cts.Token));

        Assert.Empty(Directory.GetFiles(_dir));
    }
}
