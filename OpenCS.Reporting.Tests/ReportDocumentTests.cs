using OpenCS.Reporting;
using CScore;
using CSmath;
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
        Assert.Contains("width=\"620\"", html);
        Assert.Contains("height=\"448\"", html);
        Assert.Contains("table-layout:fixed", html);
        Assert.Contains("overflow-wrap:anywhere", html);
    }

    [Fact]
    public void MaterialDiagramRenderer_EmitsAxisTitlesAndNumericTicks()
    {
        var diagram = new Diagramm(
            new LSpline([-0.003, 0], [-30, 0]),
            new LSpline([0, 0.003], [0, 30]),
            DiagrammType.L2, MatType.Concrete, "B25");

        string svg = new MaterialDiagramSvgRenderer().Render(diagram, "B25 — рабочая диаграмма");

        Assert.Contains("data-axis=\"x\"", svg);
        Assert.Contains("data-axis=\"y\"", svg);
        Assert.Contains("ε, безразмерная", svg);
        Assert.Contains("σ, МПа", svg);
        Assert.Contains("data-tick-label", svg);
        Assert.Contains(">0</text>", svg);
    }

    [Fact]
    public void CrossSectionRenderer_EmitsCoordinateAxesAndNumericTicks()
    {
        string svg = new CrossSectionReportSvgRenderer().Render(new CrossSection(), "Сечение");

        Assert.Contains("data-axis=\"x\"", svg);
        Assert.Contains("data-axis=\"y\"", svg);
        Assert.Contains("x, м", svg);
        Assert.Contains("y, м", svg);
        Assert.Contains("data-tick-label", svg);
        Assert.Contains(">0</text>", svg);
    }

    [Fact]
    public void Html_KeyValueTable_HasSynthesizedHeaderRow()
    {
        var document = new ReportDocument("Отчёт")
            .Add(new ReportKeyValueTable([("Задача", "1")], "Параметр", "Значение"));

        string html = new HtmlReportRenderer().Render(document);

        Assert.Contains("<thead><tr><th>Параметр</th><th>Значение</th></tr></thead>", html);
        Assert.Contains("<tbody>", html);
    }

    [Fact]
    public void Html_Formula_RebuildsMarkupAndEscapesPlainSegments()
    {
        var document = new ReportDocument("Отчёт")
            .Add(new ReportFormula("СП 63 (8.1)", "A & B<sub>b</sub>", "1 < 2", "ok"));

        string html = new HtmlReportRenderer().Render(document);

        Assert.Contains("A &amp; B<sub>b</sub>", html);
        Assert.Contains("1 &lt; 2", html);
    }

    [Fact]
    public void Html_Formula_ThrowsOnForeignTag()
    {
        var document = new ReportDocument("Отчёт")
            .Add(new ReportFormula("ref", "<b>x</b>", "", ""));

        Assert.Throws<FormatException>(() => new HtmlReportRenderer().Render(document));
    }

    [Fact]
    public void Html_PrintCss_DeclaresA4Page()
    {
        string html = new HtmlReportRenderer().Render(new ReportDocument("Отчёт"));
        Assert.Contains("@page { size:A4; margin:0; }", html);
    }

    [Fact]
    public void Html_Header_HasNoStaticBrandingLine()
    {
        string html = new HtmlReportRenderer().Render(new ReportDocument("Отчёт"));
        Assert.DoesNotContain("расчётное обоснование", html);
    }
}
