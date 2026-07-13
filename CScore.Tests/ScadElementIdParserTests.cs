using CScore.Import;
using Xunit;

namespace CScore.Tests;

public class ScadElementIdParserTests
{
    [Theory]
    [InlineData("12", new[] { 12 })]
    [InlineData("12, 15-17", new[] { 12, 15, 16, 17 })]
    [InlineData(" 3-3 ,5 ", new[] { 3, 5 })]
    public void TryParse_ValidRanges_ReturnsIds(string text, int[] expected)
    {
        Assert.True(ScadElementIdParser.TryParse(text, out var ids, out var error));
        Assert.Null(error);
        Assert.Equal(expected.OrderBy(x => x), ids.OrderBy(x => x));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12-")]
    [InlineData("a")]
    [InlineData("5-3")]
    public void TryParse_Invalid_ReturnsFalse(string text)
    {
        Assert.False(ScadElementIdParser.TryParse(text, out var ids, out var error));
        Assert.NotNull(error);
        Assert.Empty(ids);
    }
}
