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
        Assert.Null(p.EtaL);
        Assert.Equal(0, p.EtaL0x);
        Assert.Equal(0, p.EtaL0y);
    }

    [Fact]
    public void RoundTrip_PreservesEtaFields()
    {
        var original = new LimitForceParams
        {
            EtaEnabled = true,
            EtaIterative = true,
            EtaL = 6.0,
            EtaMuX = 1.0,
            EtaMuY = 0.75,
            EtaPsiX = 0.6,
            EtaPsiY = 0.3,
        };

        var parsed = LimitForceParams.Parse(original.ToJson());

        Assert.True(parsed.EtaEnabled);
        Assert.True(parsed.EtaIterative);
        Assert.Equal(6.0, parsed.EtaL);
        Assert.Equal(1.0, parsed.EtaMuX);
        Assert.Equal(0.75, parsed.EtaMuY);
        Assert.Equal(0.6, parsed.EtaPsiX);
        Assert.Equal(0.3, parsed.EtaPsiY);
    }

    [Fact]
    public void EtaL0xy_ComputedAsLengthTimesCoefficient()
    {
        var p = new LimitForceParams { EtaL = 6.0, EtaMuX = 0.7, EtaMuY = 1.5 };

        Assert.Equal(4.2, p.EtaL0x, precision: 6);
        Assert.Equal(9.0, p.EtaL0y, precision: 6);
    }

    [Fact]
    public void EtaL0xy_DefaultsMuTo1_WhenNotSet()
    {
        var p = new LimitForceParams { EtaL = 6.0 };

        Assert.Equal(6.0, p.EtaL0x);
        Assert.Equal(6.0, p.EtaL0y);
    }

    [Fact]
    public void RoundTrip_EtaDisabled_DoesNotSerializeOptionalFields()
    {
        var original = new LimitForceParams { EtaEnabled = false };
        var json = original.ToJson();

        Assert.DoesNotContain("etaL", json);
    }

    [Fact]
    public void RoundTrip_PreservesCustomSlendernessThreshold()
    {
        var original = new LimitForceParams
        {
            EtaEnabled = true,
            EtaL = 6.0,
            EtaSlendernessThreshold = 20.0,
        };

        var parsed = LimitForceParams.Parse(original.ToJson());

        Assert.Equal(20.0, parsed.EtaSlendernessThreshold);
    }

    [Fact]
    public void RoundTrip_IterativeModeWithoutPsi_DoesNotSwallowLaterFields()
    {
        // Режим B (итерационный) не запрашивает ψx/ψy у пользователя →
        // ToJson сериализует их как null. GetDouble() на null-элементе раньше
        // бросал исключение, которое проглатывал catch в Parse(), обрывая разбор
        // ДО чтения etaSlendernessThreshold (и любых полей после etaPsiY).
        var original = new LimitForceParams
        {
            EtaEnabled = true,
            EtaIterative = true,
            EtaL = 6.0,
            EtaMuX = 1.0,
            EtaMuY = 0.75,
            EtaSlendernessThreshold = 20.0,
        };

        var parsed = LimitForceParams.Parse(original.ToJson());

        Assert.True(parsed.EtaEnabled);
        Assert.True(parsed.EtaIterative);
        Assert.Null(parsed.EtaPsiX);
        Assert.Null(parsed.EtaPsiY);
        Assert.Equal(20.0, parsed.EtaSlendernessThreshold);
    }

    [Fact]
    public void ToJson_DefaultsSlendernessThresholdTo14_WhenNotSet()
    {
        var original = new LimitForceParams
        {
            EtaEnabled = true,
            EtaL = 6.0,
        };

        var parsed = LimitForceParams.Parse(original.ToJson());

        Assert.Equal(14.0, parsed.EtaSlendernessThreshold);
    }

    [Fact]
    public void Parse_KeepsExistingManualForces_AlongsideEtaFields()
    {
        var original = new LimitForceParams
        {
            N = 100, Mx = 20, My = 10,
            EtaEnabled = true, EtaL = 3.0,
        };

        var parsed = LimitForceParams.Parse(original.ToJson());

        Assert.Equal(100, parsed.N);
        Assert.Equal(20, parsed.Mx);
        Assert.Equal(10, parsed.My);
        Assert.True(parsed.EtaEnabled);
    }
}
