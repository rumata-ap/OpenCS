using System.Text;
using OpenCS.Reporting.Pandoc;

namespace OpenCS.Reporting;

/// <summary>Общий сервис экспорта нейтрального отчёта в HTML, Markdown, DOCX, ODT, RTF или PDF.
/// Запись всегда идёт во временный файл рядом с целью и завершается одной атомарной
/// операцией переноса — прерванный экспорт не портит существующий файл.</summary>
public sealed class ReportExportService
{
    readonly HtmlReportRenderer _html;
    readonly MarkdownReportRenderer _markdown;
    readonly IPandocReportExporter _pandoc;

    /// <summary>Создаёт сервис экспорта.</summary>
    public ReportExportService(
        HtmlReportRenderer? html = null,
        MarkdownReportRenderer? markdown = null,
        IPandocReportExporter? pandocExporter = null)
    {
        _html = html ?? new HtmlReportRenderer();
        _markdown = markdown ?? new MarkdownReportRenderer();
        _pandoc = pandocExporter ?? new PandocReportExporter();
    }

    /// <summary>Экспортирует документ по расширению целевого файла.</summary>
    /// <exception cref="ArgumentException">Расширение не поддерживается.</exception>
    public async Task ExportAsync(ReportDocument document, string outputPath,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        string fullPath = Path.GetFullPath(outputPath);
        string extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (extension is not (".html" or ".htm" or ".md" or ".docx" or ".odt" or ".rtf" or ".pdf"))
            throw new ArgumentException(
                "Формат отчёта должен быть HTML, Markdown, DOCX, ODT, RTF или PDF.", nameof(outputPath));
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("Не удалось определить каталог отчёта.", nameof(outputPath));
        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(directory,
            $".{Path.GetFileNameWithoutExtension(fullPath)}-{Guid.NewGuid():N}{extension}");
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
                case ".docx" or ".odt" or ".rtf" or ".pdf":
                    await _pandoc.ExportAsync(document, tempPath, ct).ConfigureAwait(false);
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
