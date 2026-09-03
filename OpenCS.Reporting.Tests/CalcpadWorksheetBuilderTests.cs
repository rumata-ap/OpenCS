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
        Assert.Contains("'<div class=\"formula-ref\">(8.42)</div>'", cpd);
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
}
