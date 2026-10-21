using CScore;
using CScore.Combinations;
using Xunit;

namespace CScore.Tests;

/// <summary>Признак второго порядка комбинированного набора СП 20: только если он у всех исходных наборов.</summary>
public class SP20SecondOrderTests
{
    static ForceSet Set(bool secondOrder) => new() { Tag = "x", SecondOrder = secondOrder };

    [Fact]
    public void AllSecondOrder_Marked()
    {
        Assert.True(SP20Combinations.SecondOrderOf([Set(true), Set(true)], out bool mixed));
        Assert.False(mixed);
    }

    [Fact]
    public void Mixed_NotMarked_ReportsMixed()
    {
        Assert.False(SP20Combinations.SecondOrderOf([Set(true), Set(false)], out bool mixed));
        Assert.True(mixed);
    }

    [Fact]
    public void NoneOrEmpty_NotMarked()
    {
        Assert.False(SP20Combinations.SecondOrderOf([Set(false)], out bool mixed));
        Assert.False(mixed);
        Assert.False(SP20Combinations.SecondOrderOf([], out mixed));
        Assert.False(mixed);
    }
}
