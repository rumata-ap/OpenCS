using System.Text;

namespace OpenCS.Reporting;

/// <summary>Общий сервис экспорта нейтрального отчёта в HTML, DOCX или PDF.</summary>
public sealed class ReportExportService
{
    readonly HtmlReportRenderer _htmlRenderer;
    readonly CalcpadCliRunner _calcpad;

    /// <summary>Создаёт сервис экспорта.</summary>
    public ReportExportService(HtmlReportRenderer? htmlRenderer = null,
        CalcpadCliRunner? calcpad = null)
    {
        _htmlRenderer = htmlRenderer ?? new HtmlReportRenderer();
        _calcpad = calcpad ?? new CalcpadCliRunner();
    }

    /// <summary>Экспортирует документ по расширению целевого файла.</summary>
    public async Task ExportAsync(ReportDocument document, string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        string extension = Path.GetExtension(outputPath).ToLowerInvariant();

        if (extension is ".html" or ".htm")
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(outputPath, _htmlRenderer.Render(document), Encoding.UTF8, cancellationToken);
            return;
        }

        if (extension is ".docx" or ".pdf")
        {
            await _calcpad.ExportAsync(document, outputPath, cancellationToken);
            return;
        }

        throw new ArgumentException("Формат отчёта должен быть HTML, DOCX или PDF.", nameof(outputPath));
    }
}
