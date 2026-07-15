using Xunit;
using CScore;

namespace CScore.Tests;

public class CrackWidthTaskParamsTests
{
    [Fact]
    public void Parse_EmptyJson_LongPartUseNLDefaultsFalse()
    {
        var p = CrackWidthTaskParams.Parse("{}");
        Assert.False(p.LongPartUseNL);
    }

    [Fact]
    public void ToJson_ThenParse_RoundTripsLongPartUseNL()
    {
        var p = new CrackWidthTaskParams { LongPartUseNL = true };
        var json = p.ToJson();
        var parsed = CrackWidthTaskParams.Parse(json);
        Assert.True(parsed.LongPartUseNL);
    }
}
