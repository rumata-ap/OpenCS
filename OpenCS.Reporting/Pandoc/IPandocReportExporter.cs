namespace OpenCS.Reporting.Pandoc;

/// <summary>Экспортёр отчётного документа через комплект Pandoc/Typst.</summary>
public interface IPandocReportExporter
{
    /// <summary>Записывает DOCX или PDF по расширению целевого файла.</summary>
    Task ExportAsync(
        ReportDocument document,
        string outputPath,
        CancellationToken cancellationToken = default);
}
