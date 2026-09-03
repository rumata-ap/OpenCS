using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

public sealed class ReportDocumentTests
{
    [Fact]
    public void HtmlRenderer_EmitsEscapedTextFormulaTableAndSvg()
    {
        var document = new ReportDocument("Тест <сечения>")
            .Add(new ReportHeading(1, "НДС"))
            .Add(new ReportFormula("(8.39)", "Mx = D11·ky + D13·ε0", "Mx = 12.5", "12.5 кН·м"))
            .Add(new ReportImage("section.svg", "<svg></svg>"));

        string html = new HtmlReportRenderer().Render(document);

        Assert.Contains("Тест &lt;сечения&gt;", html);
        Assert.Contains("(8.39)", html);
        Assert.Contains("<svg>", html);
    }
}
