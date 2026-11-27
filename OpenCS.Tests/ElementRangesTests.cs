using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Единый разбор списков номеров КЭ (окна групп и КонЭ, импорт SCAD, консоль).</summary>
[Collection("Loc language")]
public sealed class ElementRangesTests : IDisposable
{
    public ElementRangesTests() => Loc.SetLanguage(Loc.Russian);
    public void Dispose() => Loc.SetLanguage(null);

    [Theory]
    [InlineData("12", new[] { 12 })]
    [InlineData("12, 15-17", new[] { 12, 15, 16, 17 })]
    [InlineData(" 3-3 ,5 ", new[] { 3, 5 })]
    [InlineData("101-103 106\t116;118", new[] { 101, 102, 103, 106, 116, 118 })]
    [InlineData("1 - 3", new[] { 1, 2, 3 })]
    [InlineData("1–3", new[] { 1, 2, 3 })]
    [InlineData("5 5 4", new[] { 4, 5 })]
    public void TryParse_Valid(string text, int[] expected)
    {
        Assert.True(ElementRanges.TryParse(text, out var ids, out var error));
        Assert.Null(error);
        Assert.Equal(expected, ids);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12-")]
    [InlineData("a")]
    [InlineData("1-5 abc 7")]
    [InlineData("5-3")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("1-2000000")]
    public void TryParse_Invalid_GivesErrorAndNoIds(string text)
    {
        Assert.False(ElementRanges.TryParse(text, out var ids, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Empty(ids);
    }

    [Fact]
    public void TryParse_ErrorNamesTheBadToken()
    {
        Assert.False(ElementRanges.TryParse("1-5 abc 7", out _, out var error));
        Assert.Contains("abc", error);
        Assert.False(ElementRanges.TryParse("10-5", out _, out error));
        Assert.Contains("10-5", error);
    }

    [Fact]
    public void TryParse_EmptyAllowed()
    {
        Assert.True(ElementRanges.TryParse("  ", out var ids, out var error, allowEmpty: true));
        Assert.Empty(ids);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(new[] { "101", "102", "103", "106", "116", "117", "118" }, "101-103 106 116-118")]
    [InlineData(new[] { "7", "x1", "5", "6" }, "5-7 x1")]
    [InlineData(new string[0], "")]
    public void FormatTags_LiraStyle(string[] tags, string expected) =>
        Assert.Equal(expected, ElementRanges.FormatTags(tags));

    [Fact]
    public void Selection_CollapsesToRanges_AndParsesBack()
    {
        string[] tags = ["106", "101", "102", "103", "143", "144", "116", "117", "118"];
        string range = ElementRanges.FormatTags(tags);
        Assert.Equal("101-103 106 116-118 143-144", range);
        Assert.True(ElementRanges.TryParse(range, out var ids, out _));
        Assert.Equal(tags.Select(int.Parse).Order(), ids);
        Assert.Equal("1 3 Колонна · 1", ElementRanges.FormatTags(["3", "Колонна · 1", "1", "1"]));
    }

    [Fact]
    public void Format_RoundTrips()
    {
        int[] ids = [1, 2, 3, 9, 11, 12];
        Assert.True(ElementRanges.TryParse(ElementRanges.Format(ids), out var back, out _));
        Assert.Equal(ids, back);
    }
}
