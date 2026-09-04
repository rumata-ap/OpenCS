namespace OpenCS.Reporting;

/// <summary>Растеризатор SVG в PNG. Нужен только DOCX-рендереру и только для документов,
/// содержащих SVG-подобные иллюстрации.</summary>
public interface ISvgRasterizer
{
    /// <summary>Растеризует SVG в PNG-байты.</summary>
    Task<byte[]> RasterizeAsync(string svg, CancellationToken ct = default);
}
