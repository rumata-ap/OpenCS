using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

public sealed class CalcpadWorksheetBuilderTests
{
    [Fact]
    public void WorksheetBuilder_QuotesHtmlAndKeepsFormulaReference()
    {
        var document = new ReportDocument("Отчёт")
            .Add(new ReportFormula("(8.42)", "D11 = Σ(E·A·y²)", "D11 = 12.3", "12.3 кН·м²"));

        string cpd = new CalcpadWorksheetBuilder().Build(document);

        Assert.Contains("(8.42)", cpd);
        Assert.Contains("D11", cpd);
        Assert.DoesNotContain("\nD11 =", cpd);
        Assert.Contains("'<div class=\"formula\">'", cpd);
        Assert.Contains("'<p class=\"formula-ref\">(8.42)</p>'", cpd);
    }

    [Fact]
    public void WorksheetBuilder_UsesDocxCompatibleSvgMimeOnlyForDocx()
    {
        var document = new ReportDocument("Отчёт")
            .Add(new ReportImage("Карта", "<svg width=\"10\" height=\"10\"></svg>"));

        string docxCpd = new CalcpadWorksheetBuilder().Build(document, forDocx: true);
        string pdfCpd = new CalcpadWorksheetBuilder().Build(document, forDocx: false);

        Assert.Contains("data:image/svg;base64,", docxCpd);
        Assert.Contains("data:image/svg+xml;base64,", pdfCpd);
    }

    [Fact]
    public void WorksheetBuilder_ConstrainsTablesAndImagesForPageExport()
    {
        var document = new ReportDocument("Отчёт")
            .Add(new ReportTable(
                ["№", "Группа", "Материал", "x, мм", "y, мм", "d, мм"],
                [["1", "Нижняя", "A500", "120", "-180", "16"]]))
            .Add(new ReportImage("Карта", "<svg width=\"900\" height=\"650\"></svg>"));

        string cpd = new CalcpadWorksheetBuilder().Build(document, forDocx: true);

        Assert.Contains("table-layout:fixed", cpd);
        Assert.Contains("overflow-wrap:anywhere", cpd);
        Assert.Contains("width=\"620\"", cpd);
        Assert.Contains("height=\"448\"", cpd);
    }
}
