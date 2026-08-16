using CScore;
using Xunit;

namespace CScore.Tests;

public class MomentCurvatureBiaxialTaskParamsTests
{
    [Fact]
    public void Parse_EmptyJson_ReturnsDefaults()
    {
        var p = MomentCurvatureBiaxialTaskParams.Parse(null);

        Assert.Equal("constant", p.NMode);
        Assert.True(p.UsePsi);
        Assert.Null(p.N);
        Assert.Null(p.Mx);
        Assert.Null(p.My);
    }

    [Fact]
    public void Parse_EmptyObjectJson_ReturnsDefaults()
    {
        var p = MomentCurvatureBiaxialTaskParams.Parse("{}");

        Assert.Equal("constant", p.NMode);
        Assert.True(p.UsePsi);
    }

    [Fact]
    public void RoundTrip_SerializeThenParse_PreservesValues()
    {
        var original = new MomentCurvatureBiaxialTaskParams
        {
            N = -50.0, Mx = -60.0, My = -12.0, NMode = "proportional", UsePsi = false
        };

        var parsed = MomentCurvatureBiaxialTaskParams.Parse(original.ToJson());

        Assert.Equal(original.N, parsed.N);
        Assert.Equal(original.Mx, parsed.Mx);
        Assert.Equal(original.My, parsed.My);
        Assert.Equal(original.NMode, parsed.NMode);
        Assert.Equal(original.UsePsi, parsed.UsePsi);
    }

    [Fact]
    public void ToLoadItem_MapsNMxMyWithZeroDefaults()
    {
        var p = new MomentCurvatureBiaxialTaskParams { Mx = -10.0 };

        var item = p.ToLoadItem();

        Assert.Equal(0.0, item.N);
        Assert.Equal(-10.0, item.Mx);
        Assert.Equal(0.0, item.My);
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsDefaultsWithoutThrowing()
    {
        var p = MomentCurvatureBiaxialTaskParams.Parse("{not valid json");

        Assert.Equal("constant", p.NMode);
        Assert.True(p.UsePsi);
    }
}
