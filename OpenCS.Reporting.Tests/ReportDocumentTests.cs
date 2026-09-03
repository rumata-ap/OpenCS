using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

public sealed class ReportDocumentTests
{
    [Fact]
    public void HtmlRenderer_EmitsEscapedTextFormulaTableAndSizedSvgImage()
    {
        var document = new ReportDocument("Тест <сечения>")
            .Add(new ReportHeading(1, "НДС"))
            .Add(new ReportFormula("(8.39)", "Mx = D11·ky + D13·ε0", "Mx = 12.5", "12.5 кН·м"))
            .Add(new ReportImage("section", "<svg width=\"900\" height=\"650\"></svg>"));

        string html = new HtmlReportRenderer().Render(document);

        Assert.Contains("Тест &lt;сечения&gt;", html);
        Assert.Contains("(8.39)", html);
        Assert.Contains("data:image/svg+xml;base64,", html);
        Assert.Contains("alt=\"section.svg\"", html);
        Assert.Contains("width=\"900\"", html);
        Assert.Contains("height=\"650\"", html);
    }
}
