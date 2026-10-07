using CScore.Fem;
using Xunit;

namespace CScore.Tests;

public sealed class FemTagListTests
{
    [Fact]
    public void Parse_RangesSeparatorsAndDuplicates()
    {
        var tags = FemTagList.Parse("1-3, 7;2 10–11 П1", out var error);
        Assert.Null(error);
        Assert.Equal(["1", "2", "3", "7", "10", "11", "П1"], tags);
    }

    [Fact]
    public void Parse_ReversedRangeIsError()
    {
        FemTagList.Parse("5-2", out var error);
        Assert.NotNull(error);
    }
}
