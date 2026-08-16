using CScore;
using Xunit;

namespace CScore.Tests;

public class TotalCurvatureTaskParamsTests
{
    [Fact]
    public void Parse_EmptyJson_DefaultsToTotalOnly()
    {
        var p = TotalCurvatureTaskParams.Parse("{}");

        Assert.Equal("total_only", p.ForcesMode);
        Assert.Equal(0.7, p.LongShare);
    }

    [Fact]
    public void ToJson_ThenParse_RoundTripsManualFields()
    {
        var p = new TotalCurvatureTaskParams
        {
            N = 10.0,
            Mx = -60.0,
            My = 2.0,
            ForcesMode = "manual",
            MxLongManual = -50.0,
            MyLongManual = 0.0
        };

        var parsed = TotalCurvatureTaskParams.Parse(p.ToJson());

        Assert.Equal("manual", parsed.ForcesMode);
        Assert.Equal(-50.0, parsed.MxLongManual);
        Assert.Equal(0.0, parsed.MyLongManual);

        var loadItem = parsed.ToLoadItem();
        Assert.Equal(10.0, loadItem.N);
        Assert.Equal(-60.0, loadItem.Mx);
        Assert.Equal(2.0, loadItem.My);
    }
}
