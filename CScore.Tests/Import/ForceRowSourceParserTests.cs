using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>Разбор метки строки набора усилий в номер КЭ и сечения внешней схемы.</summary>
public class ForceRowSourceParserTests
{
    [Theory]
    [InlineData("э.12 с1", 12, 1)]
    [InlineData("э.12 с1 к3", 12, 1)]
    [InlineData("э.12 с1 к3 A2", 12, 1)]
    [InlineData("э.5251 с3 к10 Б2", 5251, 3)]
    [InlineData("  э.7 с2  ", 7, 2)]
    public void LiraApiLabel(string label, int elem, int section)
    {
        Assert.True(ForceRowSourceParser.TryParse(label, out int e, out int? s));
        Assert.Equal(elem, e);
        Assert.Equal(section, s);
    }

    [Theory]
    [InlineData("1_С1", 1, 1)]
    [InlineData("1_С2 LS+SD", 1, 2)]
    [InlineData("10_С1_К2", 10, 1)]
    [InlineData("10_С12_К2 LS+SD", 10, 12)]
    public void ScadBarLabel(string label, int elem, int section)
    {
        Assert.True(ForceRowSourceParser.TryParse(label, out int e, out int? s));
        Assert.Equal(elem, e);
        Assert.Equal(section, s);
    }

    [Theory]
    [InlineData("15_Центр", 15)]
    [InlineData("20_Центр_К2", 20)]
    public void ScadShellLabel_TextPoint_SectionUnknown(string label, int elem)
    {
        Assert.True(ForceRowSourceParser.TryParse(label, out int e, out int? s));
        Assert.Equal(elem, e);
        Assert.Null(s);
    }

    [Theory]
    [InlineData("127-1", 127, 1)]
    [InlineData("127", 127, null)]
    [InlineData("10825-C", 10825, null)]
    public void LiraHtmlLabel_OnlyWhenRequested(string label, int elem, int? section)
    {
        Assert.True(ForceRowSourceParser.TryParse(label, out int e, out int? s, liraHtml: true));
        Assert.Equal(elem, e);
        Assert.Equal(section, s);

        // Без признака HTML-отчёта так же выглядят метки РСУ2 SCAD («1-3»), где числа — не номер КЭ.
        Assert.False(ForceRowSourceParser.TryParse(label, out _, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1")]
    [InlineData("node 10")]
    [InlineData("Cm max N")]
    [InlineData("э.0 с1")]
    [InlineData("э.x с1")]
    [InlineData("в плоскости")]
    public void NotAnElementLabel(string? label)
    {
        Assert.False(ForceRowSourceParser.TryParse(label, out int e, out int? s));
        Assert.Equal(0, e);
        Assert.Null(s);
    }
}
