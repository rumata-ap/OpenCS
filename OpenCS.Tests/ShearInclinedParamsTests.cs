using CScore.Sp63Shear;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Сериализация параметров задачи расчёта наклонных сечений.</summary>
public sealed class ShearInclinedParamsTests
{
    [Fact]
    public void Parse_EmptyJson_ReturnsDefaults()
    {
        var parameters = ShearInclinedParams.Parse("{}");

        Assert.Equal("constant", parameters.ForceSource);
        Assert.Equal("bending_unstressed", parameters.ElementKind);
        Assert.Equal("both", parameters.Planes);
        Assert.Equal("auto", parameters.SupportDirection);
        Assert.True(parameters.CheckMoment);
        Assert.True(parameters.SupportAtStart);
        Assert.True(parameters.SupportAtEnd);
        Assert.False(parameters.ConstructiveRequirements103Confirmed);
        Assert.Equal(1.0, parameters.AnchorageFactor, 12);
        Assert.Empty(parameters.BarCutoffs);
    }

    [Fact]
    public void Parse_NullOrWhitespace_ReturnsDefaults()
    {
        Assert.Equal("constant", ShearInclinedParams.Parse("").ForceSource);
        Assert.Equal("constant", ShearInclinedParams.Parse("   ").ForceSource);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = new ShearInclinedParams
        {
            ForceSource = "uniform_load",
            ElementKind = "other",
            DistributedLoad = 42.5,
            DistanceToSupport = 3.5,
            SupportDirection = "forward",
            StationStep = 0.12,
            ProjectionStep = 0.004,
            FemStepIndex = 7,
            Planes = "vy",
            CheckMoment = false,
            MomentZoneLength = 1.4,
            BarCutoffs = [1.5, 3.0],
            AnchorageFactor = 0.65,
            SupportAtEnd = false,
            ConstructiveRequirements103Confirmed = true,
            OverridesVy = new ShearInclinedOverrides { B = 0.25, H0 = 0.5, PhiN = 1.2 }
        };

        var restored = ShearInclinedParams.Parse(original.ToJson());

        Assert.Equal("uniform_load", restored.ForceSource);
        Assert.Equal("other", restored.ElementKind);
        Assert.Equal(42.5, restored.DistributedLoad, 12);
        Assert.Equal(7, restored.FemStepIndex);
        Assert.Equal([1.5, 3.0], restored.BarCutoffs);
        Assert.Equal(0.65, restored.AnchorageFactor, 12);
        Assert.True(restored.ConstructiveRequirements103Confirmed);
        Assert.True(restored.SupportAtStart);
        Assert.False(restored.SupportAtEnd);
        Assert.Equal(0.25, restored.OverridesVy!.B!.Value, 12);
        Assert.Null(restored.OverridesVx);
    }

    [Theory]
    [InlineData("auto", 0)]
    [InlineData("forward", 1)]
    [InlineData("backward", -1)]
    [InlineData("нечто", 0)]
    public void DirectionSign_MapsStringToSign(string direction, int expected)
    {
        var parameters = new ShearInclinedParams { SupportDirection = direction };

        Assert.Equal(expected, parameters.DirectionSign());
    }

    [Theory]
    [InlineData("other", ElementKind.Other)]
    [InlineData("bending_unstressed", ElementKind.BendingUnstressed)]
    [InlineData("нечто", ElementKind.BendingUnstressed)]
    public void ResolveElementKind_MapsStringToEnum(string value, ElementKind expected)
    {
        var parameters = new ShearInclinedParams { ElementKind = value };

        Assert.Equal(expected, parameters.ResolveElementKind());
    }
}
