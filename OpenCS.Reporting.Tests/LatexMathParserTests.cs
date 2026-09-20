using OpenCS.Reporting.Formula;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверяет общий промежуточный формат математических выражений.</summary>
public sealed class LatexMathParserTests
{
    [Fact]
    public void Parse_fraction_and_subscript_renders_to_self_contained_html()
    {
        var node = LatexMathParser.Parse(@"x = \frac{R_s A_s}{R_b b}");
        string html = MathHtmlRenderer.Render(node);

        Assert.Contains("math-fraction", html);
        Assert.Contains("<sub>", html);
        Assert.Contains("R", html);
    }

    [Fact]
    public void Unsupported_command_is_preserved_as_safe_fallback()
    {
        var node = LatexMathParser.Parse(@"x = \unknown{q}");
        string html = MathHtmlRenderer.Render(node);

        Assert.Contains(@"\unknown", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }
}
