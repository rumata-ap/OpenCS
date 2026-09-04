namespace OpenCS.Reporting;

/// <summary>Преобразователь HTML в PDF-файл. Реализация живёт в UI-слое (WebView2),
/// чтобы библиотека отчётов оставалась portable.</summary>
public interface IHtmlToPdfConverter
{
    /// <summary>Печатает HTML в PDF по указанному пути. Путь принадлежит вызывающему:
    /// реализация только пишет в него, не удаляет и не переименовывает.</summary>
    Task ConvertAsync(string html, string outputPdfPath, CancellationToken ct = default);
}
