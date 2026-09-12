using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>Проверки сериализации параметров задачи нормального сечения.</summary>
public sealed class Sp63NormalTaskParamsTests
{
    [Fact]
    public void TaskParams_RoundTrip_PreservesNormalOptions()
    {
        var source = new Sp63NormalTaskParams
        {
            ShapeKind = "rectangular",
            Axis = "Mx",
            StructuralScheme = "statically_determinate",
            ElementLengthOrRestraintDistance = 6.0,
            EffectiveLengthL0 = 4.2,
            StabilityMode = "member",
            Psi = 1.0,
            UseManualForces = true,
            N = -800.0,
            Mx = -30.0
        };

        var parsed = Sp63NormalTaskParams.Parse(source.ToJson());

        Assert.Equal(6.0, parsed.ElementLengthOrRestraintDistance);
        Assert.True(parsed.TryToOptions(out var options, out var errorCode), errorCode);
        Assert.Equal(Sp63NormalAxis.Mx, options.Axis);
        Assert.Equal(Sp63StructuralScheme.StaticallyDeterminate,
            options.MemberContext.StructuralScheme);
        Assert.Equal(-800.0, parsed.ToLoadItem().N);
    }

    [Fact]
    public void UnknownAxis_IsInvalidInput()
    {
        var parameters = new Sp63NormalTaskParams { Axis = "Mxy" };

        Assert.False(parameters.TryToOptions(out _, out var errorCode));
        Assert.Equal("invalid_axis", errorCode);
    }

    [Fact]
    public void EmptyJson_UsesSafeDefaults()
    {
        var parameters = Sp63NormalTaskParams.Parse(null);

        Assert.True(parameters.TryToOptions(out var options, out var errorCode), errorCode);
        Assert.Equal(Sp63NormalShapeKind.Rectangular, options.ShapeKind);
        Assert.Equal(Sp63NormalAxis.Mx, options.Axis);
    }

    [Fact]
    public void Result_StoresMessagesOutsideStrengthDetails()
    {
        var result = new Sp63NormalResult
        {
            Status = Sp63NormalStatus.NotApplicable,
            StrengthPassed = null,
            ApplicabilityMessages =
            [new("missing_length", Sp63NormalMessageKind.Applicability,
                "8.1.7", "Sp63Normal_MissingLength")]
        };

        Assert.Null(result.StrengthPassed);
        Assert.Empty(result.StrengthDetails);
        Assert.Single(result.ApplicabilityMessages);
    }
}
