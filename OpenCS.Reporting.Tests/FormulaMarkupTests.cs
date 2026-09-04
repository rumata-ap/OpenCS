using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверки закрытого inline-контракта формул: только &lt;sub&gt; и &lt;sup&gt;,
/// без вложенности, обязательно закрытые.</summary>
public sealed class FormulaMarkupTests
{
    [Fact]
    public void Parse_PlainText_ReturnsSingleSegment()
    {
        var segments = FormulaMarkup.Parse("N = 100");
        Assert.Single(segments);
        Assert.Equal(FormulaSegmentKind.Plain, segments[0].Kind);
        Assert.Equal("N = 100", segments[0].Text);
    }

    [Fact]
    public void Parse_SplitsSubscriptAndSuperscript()
    {
        var segments = FormulaMarkup.Parse("σ<sub>b</sub> · A<sup>2</sup> ");

        Assert.Collection(segments,
            s => { Assert.Equal(FormulaSegmentKind.Plain, s.Kind); Assert.Equal("σ", s.Text); },
            s => { Assert.Equal(FormulaSegmentKind.Subscript, s.Kind); Assert.Equal("b", s.Text); },
            s => { Assert.Equal(FormulaSegmentKind.Plain, s.Kind); Assert.Equal(" · A", s.Text); },
            s => { Assert.Equal(FormulaSegmentKind.Superscript, s.Kind); Assert.Equal("2", s.Text); },
            s => { Assert.Equal(FormulaSegmentKind.Plain, s.Kind); Assert.Equal(" ", s.Text); });
    }

    [Fact]
    public void Parse_AdjacentTags_ProduceNoEmptyPlainSegment()
    {
        var segments = FormulaMarkup.Parse("<sub>a</sub><sup>b</sup>");
        Assert.Equal(2, segments.Count);
        Assert.Equal(FormulaSegmentKind.Subscript, segments[0].Kind);
        Assert.Equal(FormulaSegmentKind.Superscript, segments[1].Kind);
    }

    [Fact]
    public void Parse_EmptyTagContent_IsSkipped()
    {
        var segments = FormulaMarkup.Parse("x<sub></sub>y");
        Assert.Collection(segments,
            s => Assert.Equal("x", s.Text),
            s => Assert.Equal("y", s.Text));
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsNoSegments()
        => Assert.Empty(FormulaMarkup.Parse(""));

    [Theory]
    [InlineData("1 < 2")]
    [InlineData("ε < 0,0035")]
    [InlineData("x <= y")]
    public void Parse_ComparisonSign_IsPlainText(string text)
    {
        var segments = FormulaMarkup.Parse(text);
        Assert.Single(segments);
        Assert.Equal(FormulaSegmentKind.Plain, segments[0].Kind);
        Assert.Equal(text, segments[0].Text);
    }

    [Theory]
    [InlineData("<b>жирный</b>")]
    [InlineData("<span style=\"vertical-align:sub\">b</span>")]
    [InlineData("<script>alert(1)</script>")]
    public void Parse_UnknownTag_Throws(string text)
        => Assert.Throws<FormatException>(() => FormulaMarkup.Parse(text));

    [Fact]
    public void Parse_UnclosedTag_Throws()
        => Assert.Throws<FormatException>(() => FormulaMarkup.Parse("σ<sub>b"));

    [Fact]
    public void Parse_NestedTags_Throw()
        => Assert.Throws<FormatException>(() => FormulaMarkup.Parse("σ<sub>b<sub>1</sub></sub>"));

    [Fact]
    public void Parse_StrayClosingTag_Throws()
        => Assert.Throws<FormatException>(() => FormulaMarkup.Parse("σ</sub>"));
}
