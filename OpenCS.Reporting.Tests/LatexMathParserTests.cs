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

    [Fact]
    public void Spacing_text_min_and_int_render_without_raw_commands()
    {
        var node = LatexMathParser.Parse(@"F_{ult} = \min(a;\ 2b) = 5\ \text{кН};\quad I = \int_u y^2\,dl \pm 1");
        string html = MathHtmlRenderer.Render(node);

        Assert.DoesNotContain(@"\", html);
        Assert.Contains("min", html);
        Assert.Contains("кН", html);
        Assert.Contains("∫", html);
        Assert.Contains("&#177;", html);   // ±
    }
}
