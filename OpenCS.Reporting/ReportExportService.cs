using System.Text;

namespace OpenCS.Reporting;

/// <summary>Общий сервис экспорта нейтрального отчёта в HTML, Markdown, DOCX или PDF.
/// Запись всегда идёт во временный файл рядом с целью и завершается одной атомарной
/// операцией переноса — прерванный экспорт не портит существующий файл.</summary>
public sealed class ReportExportService
{
    readonly HtmlReportRenderer _html;
    readonly MarkdownReportRenderer _markdown;
    readonly OpenXmlReportRenderer _docx;
    readonly IHtmlToPdfConverter? _pdfConverter;
    readonly ISvgRasterizer? _svgRasterizer;

    /// <summary>Создаёт сервис экспорта.</summary>
    public ReportExportService(
        HtmlReportRenderer? html = null,
        MarkdownReportRenderer? markdown = null,
        OpenXmlReportRenderer? docx = null,
        IHtmlToPdfConverter? pdfConverter = null,
        ISvgRasterizer? svgRasterizer = null)
    {
        _html = html ?? new HtmlReportRenderer();
        _markdown = markdown ?? new MarkdownReportRenderer();
        _docx = docx ?? new OpenXmlReportRenderer();
        _pdfConverter = pdfConverter;
        _svgRasterizer = svgRasterizer;
    }

    /// <summary>Экспортирует документ по расширению целевого файла.</summary>
    /// <exception cref="ArgumentException">Расширение не поддерживается.</exception>
    /// <exception cref="InvalidOperationException">Для PDF не передан преобразователь.</exception>
    public async Task ExportAsync(ReportDocument document, string outputPath,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        string fullPath = Path.GetFullPath(outputPath);
        string extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (extension is not (".html" or ".htm" or ".md" or ".docx" or ".pdf"))
            throw new ArgumentException(
                "Формат отчёта должен быть HTML, Markdown, DOCX или PDF.", nameof(outputPath));
        if (extension == ".pdf" && _pdfConverter == null)
            throw new InvalidOperationException(
                "Экспорт в PDF требует преобразователя IHtmlToPdfConverter.");

        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("Не удалось определить каталог отчёта.", nameof(outputPath));
        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(directory,
            $".{Path.GetFileNameWithoutExtension(fullPath)}-{Guid.NewGuid():N}.tmp");
        bool moved = false;
        try
        {
            switch (extension)
            {
                case ".html" or ".htm":
                    await File.WriteAllTextAsync(tempPath, _html.Render(document), Encoding.UTF8, ct)
                        .ConfigureAwait(false);
                    break;
                case ".md":
                    await File.WriteAllTextAsync(tempPath, _markdown.Render(document), Encoding.UTF8, ct)
                        .ConfigureAwait(false);
                    break;
                case ".docx":
                    byte[] bytes = await _docx.RenderAsync(document, _svgRasterizer, ct).ConfigureAwait(false);
                    await File.WriteAllBytesAsync(tempPath, bytes, ct).ConfigureAwait(false);
                    break;
                case ".pdf":
                    await _pdfConverter!.ConvertAsync(_html.Render(document), tempPath, ct).ConfigureAwait(false);
                    break;
            }

            ct.ThrowIfCancellationRequested();
            File.Move(tempPath, fullPath, overwrite: true);
            moved = true;
        }
        finally
        {
            // После успешного переноса источника уже нет — повторное удаление безопасно.
            if (!moved)
                try { File.Delete(tempPath); } catch { /* временный файл, ошибку удаления игнорируем */ }
        }
    }
}
