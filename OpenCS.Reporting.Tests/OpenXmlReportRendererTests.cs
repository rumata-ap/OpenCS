using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверки структуры DOCX: текст, надстрочные/подстрочные прогоны,
/// параметры страницы, таблицы и встроенные изображения.</summary>
public sealed class OpenXmlReportRendererTests
{
    // 1×1 прозрачный PNG — достаточен для проверки ImagePart и связи Drawing→ImagePart.
    static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    sealed class FakeRasterizer : ISvgRasterizer
    {
        public int Calls { get; private set; }
        public Task<byte[]> RasterizeAsync(string svg, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(OnePixelPng);
        }
    }

    static async Task<WordprocessingDocument> RenderAsync(ISvgRasterizer? rasterizer, params ReportBlock[] blocks)
    {
        var document = new ReportDocument("Отчёт НДС");
        foreach (var block in blocks) document.Add(block);
        byte[] bytes = await new OpenXmlReportRenderer().RenderAsync(document, rasterizer);
        return WordprocessingDocument.Open(new MemoryStream(bytes, writable: false), false);
    }

    [Fact]
    public async Task Render_WritesTitleAsHeadingAndCoreProperty()
    {
        using var docx = await RenderAsync(null);

        Assert.Contains("Отчёт НДС", docx.MainDocumentPart!.Document!.InnerText);
        Assert.Equal("Отчёт НДС", docx.PackageProperties.Title);
    }

    [Fact]
    public async Task Render_SetsA4PageSizeAndMargins()
    {
        using var docx = await RenderAsync(null);

        var section = docx.MainDocumentPart!.Document!.Body!.Elements<SectionProperties>().Single();
        var size = section.GetFirstChild<PageSize>()!;
        var margin = section.GetFirstChild<PageMargin>()!;

        Assert.Equal(11906u, size.Width!.Value);
        Assert.Equal(16838u, size.Height!.Value);
        // PageMargin.Top — Int32Value (может быть отрицательным), Left — UInt32Value.
        Assert.Equal(1134, margin.Top!.Value);
        Assert.Equal(1134u, margin.Left!.Value);
    }

    [Fact]
    public async Task Formula_UsesVerticalTextAlignmentRuns()
    {
        using var docx = await RenderAsync(null,
            new ReportFormula("СП 63 (8.1)", "σ<sub>b</sub>", "A<sup>2</sup>", "= 5"));

        var alignments = docx.MainDocumentPart!.Document!.Descendants<VerticalTextAlignment>()
            .Select(v => v.Val!.Value).ToList();

        Assert.Contains(VerticalPositionValues.Subscript, alignments);
        Assert.Contains(VerticalPositionValues.Superscript, alignments);
        Assert.Contains("СП 63 (8.1)", docx.MainDocumentPart.Document!.InnerText);
    }

    [Fact]
    public async Task Formula_ThrowsOnForeignTag()
        => await Assert.ThrowsAsync<FormatException>(
            () => new OpenXmlReportRenderer().RenderAsync(
                new ReportDocument("t").Add(new ReportFormula("r", "<b>x</b>", "", "")), null));

    [Fact]
    public async Task KeyValueTable_HasSynthesizedHeaderRow()
    {
        using var docx = await RenderAsync(null, new ReportKeyValueTable(
            [("Задача", "1")], "Параметр", "Значение"));

        var table = docx.MainDocumentPart!.Document!.Descendants<Table>().Single();
        var headerCells = table.Elements<TableRow>().First().Elements<TableCell>()
            .Select(c => c.InnerText).ToList();

        Assert.Equal(["Параметр", "Значение"], headerCells);
        Assert.NotNull(table.GetFirstChild<TableProperties>()!.GetFirstChild<TableBorders>());
    }

    [Fact]
    public async Task Table_UsesHeadersAsFirstRow()
    {
        using var docx = await RenderAsync(null, new ReportTable(["A", "B"], [["1", "2"]]));

        var rows = docx.MainDocumentPart!.Document!.Descendants<TableRow>().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("AB", rows[0].InnerText);
        Assert.Equal("12", rows[1].InnerText);
    }

    [Fact]
    public async Task Image_Svg_IsRasterizedAndReferencedByDrawing()
    {
        var rasterizer = new FakeRasterizer();
        using var docx = await RenderAsync(rasterizer,
            new ReportImage("Карта", "<svg viewBox=\"0 0 900 650\"></svg>"));

        Assert.Equal(1, rasterizer.Calls);
        var part = Assert.Single(docx.MainDocumentPart!.ImageParts);
        var blip = docx.MainDocumentPart.Document!.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().Single();
        Assert.Equal(docx.MainDocumentPart.GetIdOfPart(part), blip.Embed!.Value);
        Assert.Contains("Карта", docx.MainDocumentPart.Document!.InnerText);
    }

    [Fact]
    public async Task Image_NonSvg_UsesTextFallbackWithoutRasterizer()
    {
        var rasterizer = new FakeRasterizer();
        using var docx = await RenderAsync(rasterizer, new ReportImage("Текст", "просто текст"));

        Assert.Equal(0, rasterizer.Calls);
        Assert.Empty(docx.MainDocumentPart!.ImageParts);
        Assert.Contains("просто текст", docx.MainDocumentPart.Document!.InnerText);
    }

    [Fact]
    public async Task Image_SvgWithoutValidSize_Throws()
        => await Assert.ThrowsAsync<FormatException>(
            () => new OpenXmlReportRenderer().RenderAsync(
                new ReportDocument("t").Add(new ReportImage("x", "<svg></svg>")), new FakeRasterizer()));

    [Fact]
    public async Task Image_SvgWithoutRasterizer_Throws()
        => await Assert.ThrowsAsync<InvalidOperationException>(
            () => new OpenXmlReportRenderer().RenderAsync(
                new ReportDocument("t").Add(new ReportImage("x", "<svg viewBox=\"0 0 10 10\"></svg>")), null));

    [Fact]
    public async Task Heading_UsesExpectedFontSizes()
    {
        using var docx = await RenderAsync(null, new ReportHeading(2, "Раздел"));

        var run = docx.MainDocumentPart!.Document!.Descendants<Run>()
            .First(r => r.InnerText == "Раздел");
        Assert.Equal("30", run.RunProperties!.FontSize!.Val!.Value);
        Assert.NotNull(run.RunProperties.Bold);
    }

    [Fact]
    public async Task PageBreak_EmitsPageBreakRun()
    {
        using var docx = await RenderAsync(null, new ReportPageBreak());

        var breaks = docx.MainDocumentPart!.Document!.Descendants<Break>().ToList();
        Assert.Contains(breaks, b => b.Type!.Value == BreakValues.Page);
    }

    [Fact]
    public async Task Warning_IsShadedParagraph()
    {
        using var docx = await RenderAsync(null, new ReportWarning("Осторожно"));

        var paragraph = docx.MainDocumentPart!.Document!.Descendants<Paragraph>()
            .First(p => p.InnerText == "Осторожно");
        Assert.NotNull(paragraph.ParagraphProperties!.Shading);
        Assert.NotNull(paragraph.ParagraphProperties.ParagraphBorders!.LeftBorder);
    }
}
