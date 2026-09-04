using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверки политики определения размеров SVG: приоритет источников,
/// поддерживаемые единицы и явная ошибка вместо тихого fallback.</summary>
public sealed class SvgSizingTests
{
    [Fact]
    public void Resolve_UsesViewBox_WhenWidthAndHeightAbsent()
    {
        var size = SvgSizing.Resolve("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 900 650\"></svg>");
        Assert.Equal(900, size.Width);
        Assert.Equal(650, size.Height);
    }

    [Fact]
    public void Resolve_AcceptsCommaSeparatedViewBox()
    {
        var size = SvgSizing.Resolve("<svg viewBox=\"0,0,400,300\"></svg>");
        Assert.Equal(400, size.Width);
        Assert.Equal(300, size.Height);
    }

    [Fact]
    public void Resolve_PrefersExplicitWidthHeight_OverViewBox()
    {
        var size = SvgSizing.Resolve("<svg width=\"120px\" height=\"60\" viewBox=\"0 0 900 650\"></svg>");
        Assert.Equal(120, size.Width);
        Assert.Equal(60, size.Height);
    }

    [Fact]
    public void Resolve_IgnoresStrokeWidthAttribute()
    {
        var size = SvgSizing.Resolve("<svg stroke-width=\"3\" viewBox=\"0 0 200 100\"></svg>");
        Assert.Equal(200, size.Width);
        Assert.Equal(100, size.Height);
    }

    [Theory]
    [InlineData("<svg width=\"10mm\" height=\"5mm\" viewBox=\"0 0 200 100\"></svg>")]
    [InlineData("<svg width=\"100%\" height=\"100%\" viewBox=\"0 0 200 100\"></svg>")]
    public void Resolve_FallsBackToViewBox_ForUnsupportedUnits(string svg)
    {
        var size = SvgSizing.Resolve(svg);
        Assert.Equal(200, size.Width);
        Assert.Equal(100, size.Height);
    }

    [Theory]
    [InlineData("<svg viewBox=\"0 0 -1 100\"></svg>")]
    [InlineData("<svg viewBox=\"0 0 0 100\"></svg>")]
    [InlineData("<svg viewBox=\"0 0 100\"></svg>")]
    [InlineData("<svg width=\"100%\" height=\"100%\"></svg>")]
    [InlineData("<svg></svg>")]
    public void Resolve_Throws_WhenNoValidSource(string svg)
        => Assert.Throws<FormatException>(() => SvgSizing.Resolve(svg));

    [Fact]
    public void EnsureExplicitDimensions_ReplacesPercentAttributes()
    {
        string result = SvgSizing.EnsureExplicitDimensions(
            "<svg width=\"100%\" height=\"100%\" viewBox=\"0 0 900 650\"><rect/></svg>");

        Assert.DoesNotContain("100%", result);
        Assert.Contains("width=\"900px\"", result);
        Assert.Contains("height=\"650px\"", result);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(result, "width="));
        Assert.Contains("<rect/>", result);
    }

    [Fact]
    public void EnsureExplicitDimensions_AddsAttributes_WhenAbsent()
    {
        string result = SvgSizing.EnsureExplicitDimensions("<svg viewBox=\"0 0 900 650\"></svg>");
        Assert.Contains("width=\"900px\"", result);
        Assert.Contains("height=\"650px\"", result);
        Assert.Contains("viewBox=\"0 0 900 650\"", result);
    }

    [Fact]
    public void EnsureExplicitDimensions_KeepsSelfClosingRoot()
    {
        string result = SvgSizing.EnsureExplicitDimensions("<svg viewBox=\"0 0 10 20\"/>");
        Assert.Contains("width=\"10px\"", result);
        Assert.EndsWith("/>", result);
    }
}
