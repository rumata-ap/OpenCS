using System.Text;
using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверки markdown-экранирования: произвольный текст отчёта не должен
/// превращаться в разметку и не должен нести видимых обратных слешей.</summary>
public sealed class MarkdownReportRendererTests
{
    static string Render(params ReportBlock[] blocks)
    {
        var document = new ReportDocument("Отчёт");
        foreach (var block in blocks) document.Add(block);
        return new MarkdownReportRenderer().Render(document);
    }

    [Fact]
    public void Render_StartsWithTitleHeading()
        => Assert.StartsWith("# Отчёт", Render());

    [Fact]
    public void Paragraph_EscapesMarkdownAndHtmlSyntax()
    {
        string md = Render(new ReportParagraph("[x] `code` _tag_ * & <script>"));

        Assert.Contains("\\[x\\]", md);
        Assert.Contains("\\`code\\`", md);
        Assert.Contains("\\_tag\\_", md);
        Assert.Contains("\\*", md);
        Assert.Contains("&amp;", md);
        Assert.Contains("&lt;script&gt;", md);
    }

    [Theory]
    [InlineData("# not heading", "\\# not heading")]
    [InlineData("> not quote", "\\> not quote")]
    [InlineData("- not list", "\\- not list")]
    [InlineData("+ not list", "\\+ not list")]
    [InlineData("1. not a list", "1\\. not a list")]
    [InlineData("2) not a list", "2\\) not a list")]
    public void Paragraph_EscapesLineMarkers(string input, string expected)
        => Assert.Contains(expected, Render(new ReportParagraph(input)));

    [Fact]
    public void Paragraph_NumericMarker_KeepsDigitUnescaped()
    {
        string md = Render(new ReportParagraph("1. not a list"));
        Assert.DoesNotContain("\\1", md);
    }

    [Fact]
    public void Paragraph_FourLeadingSpaces_DoNotBecomeCodeBlock()
    {
        string md = Render(new ReportParagraph("    текст"));
        Assert.Contains("&#32;&#32;&#32;&#32;текст", md);
    }

    [Fact]
    public void Paragraph_EscapesMarkerOnEveryLine()
    {
        string md = Render(new ReportParagraph("первая\n# вторая"));

        Assert.Contains("первая  \n\\# вторая", md);
    }

    [Fact]
    public void Table_EscapesPipesAndUsesBrForNewlines()
    {
        string md = Render(new ReportTable(["Тег"], [["A|B\nC"]]));

        Assert.Contains("A\\|B<br>C", md);
        Assert.Contains("| Тег |", md);
        Assert.Contains("| --- |", md);
    }

    [Fact]
    public void KeyValueTable_HasParameterValueHeader()
    {
        string md = Render(new ReportKeyValueTable([("Задача", "1")], "Параметр", "Значение"));

        Assert.Contains("| Параметр | Значение |", md);
        Assert.Contains("| Задача | 1 |", md);
    }

    [Fact]
    public void Formula_UsesBlockquoteAndInlineTags()
    {
        string md = Render(new ReportFormula("СП 63 (8.1)", "σ<sub>b</sub>", "A<sup>2</sup>", "= 5"));

        Assert.Contains("> **СП 63 (8.1)**", md);
        Assert.Contains("σ<sub>b</sub>", md);
        Assert.Contains("A<sup>2</sup>", md);
        Assert.Contains("= 5", md);
    }

    [Fact]
    public void Formula_ThrowsOnForeignTag()
        => Assert.Throws<FormatException>(() => Render(new ReportFormula("r", "<b>x</b>", "", "")));

    [Fact]
    public void Image_Svg_IsEmbeddedAsBase64DataUri()
    {
        string svg = "<svg viewBox=\"0 0 10 20\"></svg>";
        string md = Render(new ReportImage("Карта", svg));

        string expected = Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
        Assert.Contains($"![Карта](data:image/svg+xml;base64,{expected})", md);
        Assert.Contains("*Карта*", md);
    }

    [Fact]
    public void Image_NonSvg_UsesFenceLongerThanContent()
    {
        string md = Render(new ReportImage("Текст", "a ```` b"));

        Assert.Contains("`````\na ```` b\n`````", md);
    }

    [Fact]
    public void Warning_AndPageBreak_AreRendered()
    {
        string md = Render(new ReportWarning("Осторожно"), new ReportPageBreak());

        Assert.Contains("> ⚠️ Осторожно", md);
        Assert.Contains("\n---\n", md);
    }

    [Fact]
    public void Heading_UsesLevelHashes()
        => Assert.Contains("### Раздел", Render(new ReportHeading(3, "Раздел")));
}
