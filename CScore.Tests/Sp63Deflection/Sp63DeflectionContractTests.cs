using CScore.Sp63.Deflection;
using Xunit;

namespace CScore.Tests.Sp63Deflection;

public sealed class Sp63DeflectionContractTests
{
    [Theory]
    [InlineData("simply_supported_uniform", 5.0 / 48.0)]
    [InlineData("simply_supported_midpoint", 1.0 / 12.0)]
    [InlineData("cantilever_tip", 1.0 / 3.0)]
    public void TryParseScheme_ReturnsCoefficient(string value, double expected)
    {
        Assert.True(Sp63DeflectionScheme.TryParse(value, out var scheme));
        Assert.Equal(expected, Sp63DeflectionScheme.Coefficient(scheme), 12);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void TryParseScheme_RejectsMissingOrUnknownValue(string? value)
    {
        Assert.False(Sp63DeflectionScheme.TryParse(value, out _));
    }

    [Fact]
    public void Params_RejectManualModeWithoutLongitudinalN()
    {
        var p = new Sp63DeflectionTaskParams
        {
            Scheme = "simply_supported_uniform", ForcesMode = "manual", SpanM = 6,
            DeflectionLimitMm = 20, MxLongManual = 12, MyLongManual = 0
        };

        Assert.False(p.TryToOptions(out _, out var error));
        Assert.Equal("missing_long_manual_load", error);
    }

    [Fact]
    public void Params_MissingSchemeInJsonDoesNotUseCreationDefault()
    {
        var p = Sp63DeflectionTaskParams.Parse("{\"spanM\":6,\"deflectionLimitMm\":20}");

        Assert.False(p.TryToOptions(out _, out var error));
        Assert.Equal("invalid_scheme", error);
    }
}
