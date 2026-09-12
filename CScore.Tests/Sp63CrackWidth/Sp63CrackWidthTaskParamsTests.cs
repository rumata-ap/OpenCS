using CScore.Sp63.CrackWidth;
using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63CrackWidth;

/// <summary>Проверки сериализации параметров задачи ширины раскрытия трещин.</summary>
public sealed class Sp63CrackWidthTaskParamsTests
{
    [Fact]
    public void TaskParams_RoundTrip_PreservesOptions()
    {
        var source = new Sp63CrackWidthTaskParams
        {
            ShapeKind = "rectangular",
            Axis = "My",
            Phi1 = 1.4,
            Phi2 = 0.5,
            AcrcLimMm = 0.4,
            UseManualForces = true,
            N = 120.0,
            Mx = 0.0,
            My = 90.0
        };

        var parsed = Sp63CrackWidthTaskParams.Parse(source.ToJson());

        Assert.Equal(1.4, parsed.Phi1);
        Assert.True(parsed.TryToOptions(out var options, out var errorCode), errorCode);
        Assert.Equal(Sp63NormalAxis.My, options.Axis);
        Assert.Equal(0.4, options.AcrcLimMm);
        Assert.Equal(120.0, parsed.ToLoadItem().N);
        Assert.Equal(90.0, parsed.ToLoadItem().My);
    }

    [Fact]
    public void UnknownAxis_IsInvalidInput()
    {
        var parameters = new Sp63CrackWidthTaskParams { Axis = "Mxy" };

        Assert.False(parameters.TryToOptions(out _, out var errorCode));
        Assert.Equal("invalid_axis", errorCode);
    }

    [Fact]
    public void NonPositivePhi_IsInvalidInput()
    {
        var parameters = new Sp63CrackWidthTaskParams { Phi1 = 0.0 };

        Assert.False(parameters.TryToOptions(out _, out var errorCode));
        Assert.Equal("invalid_phi", errorCode);
    }

    [Fact]
    public void NonPositiveAcrcLimit_IsInvalidInput()
    {
        var parameters = new Sp63CrackWidthTaskParams { AcrcLimMm = -0.1 };

        Assert.False(parameters.TryToOptions(out _, out var errorCode));
        Assert.Equal("invalid_acrc_limit", errorCode);
    }

    [Fact]
    public void EmptyJson_UsesSafeDefaults()
    {
        var parameters = Sp63CrackWidthTaskParams.Parse(null);

        Assert.True(parameters.TryToOptions(out var options, out var errorCode), errorCode);
        Assert.Equal(Sp63NormalShapeKind.Rectangular, options.ShapeKind);
        Assert.Equal(Sp63NormalAxis.Mx, options.Axis);
        Assert.Equal(1.0, options.Phi1);
        Assert.Equal(0.5, options.Phi2);
        Assert.Equal(0.3, options.AcrcLimMm);
    }
}
