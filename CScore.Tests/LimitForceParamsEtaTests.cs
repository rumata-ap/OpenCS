using Xunit;

namespace CScore.Tests;

public class LimitForceParamsEtaTests
{
    [Fact]
    public void Parse_DefaultsEtaDisabled_WhenAbsent()
    {
        var p = LimitForceParams.Parse("{}");
        Assert.False(p.EtaEnabled);
        Assert.False(p.EtaIterative);
        Assert.Null(p.EtaL0x);
        Assert.Null(p.EtaL0y);
    }

    [Fact]
    public void RoundTrip_PreservesEtaFields()
    {
        var original = new LimitForceParams
        {
            EtaEnabled = true,
            EtaIterative = true,
            EtaL0x = 6.0,
            EtaL0y = 4.5,
            EtaM1lx = 30.0,
            EtaM1ly = 15.0,
        };

        var parsed = LimitForceParams.Parse(original.ToJson());

        Assert.True(parsed.EtaEnabled);
        Assert.True(parsed.EtaIterative);
        Assert.Equal(6.0, parsed.EtaL0x);
        Assert.Equal(4.5, parsed.EtaL0y);
        Assert.Equal(30.0, parsed.EtaM1lx);
        Assert.Equal(15.0, parsed.EtaM1ly);
    }

    [Fact]
    public void RoundTrip_EtaDisabled_DoesNotSerializeOptionalFields()
    {
        var original = new LimitForceParams { EtaEnabled = false };
        var json = original.ToJson();

        Assert.DoesNotContain("etaL0x", json);
    }

    [Fact]
    public void Parse_KeepsExistingManualForces_AlongsideEtaFields()
    {
        var original = new LimitForceParams
        {
            N = 100, Mx = 20, My = 10,
            EtaEnabled = true, EtaL0x = 3.0, EtaL0y = 3.0,
        };

        var parsed = LimitForceParams.Parse(original.ToJson());

        Assert.Equal(100, parsed.N);
        Assert.Equal(20, parsed.Mx);
        Assert.Equal(10, parsed.My);
        Assert.True(parsed.EtaEnabled);
    }
}
