using CScore;
using CScore.Sp63.Deflection;
using CScore.Sp63.Normal;
using CScore.Tests.Sp63Fixtures;
using Xunit;

namespace CScore.Tests.Sp63Deflection;

/// <summary>
/// Тесты формульного расчёта прогиба тавра/двутавра: все три статические схемы, передача
/// длительной составляющей в кривизну и границы применимости.
/// </summary>
public sealed class Sp63TeeDeflectionTests
{
    [Theory]
    [InlineData(Sp63DeflectionStaticScheme.SimplySupportedUniform, 5.0 / 48.0)]
    [InlineData(Sp63DeflectionStaticScheme.SimplySupportedMidpoint, 1.0 / 12.0)]
    [InlineData(Sp63DeflectionStaticScheme.CantileverTip, 1.0 / 3.0)]
    public void Tee_Deflection_UsesCurvatureAndSchemeCoefficient(
        Sp63DeflectionStaticScheme scheme, double expectedCoefficient)
    {
        var result = Sp63DeflectionChecker.Check(
            Sp63SlsTestSections.TopTee(),
            new LoadItem { Mx = -80, N = 10 },
            new LoadItem { Mx = -20, N = 4 }, CalcType.N,
            Sp63SlsTestOptions.Deflection(Sp63NormalShapeKind.Tee, scheme));

        Assert.Equal(Sp63DeflectionStatus.Calculated, result.Status);
        Assert.Equal(1000 * expectedCoefficient * 36 * Math.Abs(result.Curvature!.Total),
            result.DeflectionMm, 9);
    }

    [Fact]
    public void Tee_LongTermLoads_EnterCurvature()
    {
        var result = Sp63DeflectionChecker.Check(
            Sp63SlsTestSections.TopTee(),
            new LoadItem { Mx = -80, N = 10 },
            new LoadItem { Mx = -20, N = 4 }, CalcType.N,
            Sp63SlsTestOptions.Deflection(Sp63NormalShapeKind.Tee));

        Assert.Equal(Sp63DeflectionStatus.Calculated, result.Status);
        Assert.Contains(result.Curvature!.Terms, term => term.M == 20.0 && term.N == 4.0);
    }

    [Fact]
    public void Tee_LongTermShare_ChangesResult()
    {
        var shareFull = Sp63DeflectionChecker.Check(
            Sp63SlsTestSections.TopTee(),
            new LoadItem { Mx = -80, N = 10 },
            new LoadItem { Mx = -80, N = 10 }, CalcType.N,
            Sp63SlsTestOptions.Deflection(Sp63NormalShapeKind.Tee,
                Sp63DeflectionStaticScheme.SimplySupportedUniform));
        var shareHalf = Sp63DeflectionChecker.Check(
            Sp63SlsTestSections.TopTee(),
            new LoadItem { Mx = -80, N = 10 },
            new LoadItem { Mx = -40, N = 5 }, CalcType.N,
            Sp63SlsTestOptions.Deflection(Sp63NormalShapeKind.Tee,
                Sp63DeflectionStaticScheme.SimplySupportedUniform));

        Assert.Equal(Sp63DeflectionStatus.Calculated, shareFull.Status);
        Assert.Equal(Sp63DeflectionStatus.Calculated, shareHalf.Status);
        Assert.NotEqual(shareFull.DeflectionMm, shareHalf.DeflectionMm);
    }

    [Fact]
    public void ISection_Deflection_Calculates()
    {
        var result = Sp63DeflectionChecker.Check(
            Sp63SlsTestSections.ISection(),
            new LoadItem { Mx = 80, N = 10 },
            new LoadItem { Mx = 20, N = 4 }, CalcType.N,
            Sp63SlsTestOptions.Deflection(Sp63NormalShapeKind.Tee));

        Assert.Equal(Sp63DeflectionStatus.Calculated, result.Status);
        Assert.True(result.DeflectionMm > 0);
    }

    [Fact]
    public void Rectangle_Deflection_MatchesCurvatureFormula()
    {
        var result = Sp63DeflectionChecker.Check(
            Sp63SlsTestSections.Rectangle(),
            new LoadItem { Mx = 80, N = 10 },
            new LoadItem { Mx = 20, N = 4 }, CalcType.N,
            Sp63SlsTestOptions.Deflection(Sp63NormalShapeKind.Rectangular));

        Assert.Equal(Sp63DeflectionStatus.Calculated, result.Status);
        Assert.Equal(1000 * (5.0 / 48.0) * 36 * Math.Abs(result.Curvature!.Total),
            result.DeflectionMm, 9);
    }

    [Fact]
    public void RectangleMy_Deflection_Calculates()
    {
        var result = Sp63DeflectionChecker.Check(
            Sp63SlsTestSections.RectangleMy(),
            new LoadItem { My = 80, N = 10 },
            new LoadItem { My = 20, N = 4 }, CalcType.N,
            Sp63SlsTestOptions.Deflection(Sp63NormalShapeKind.Rectangular,
                Sp63DeflectionStaticScheme.SimplySupportedUniform, Sp63NormalAxis.My));

        Assert.Equal(Sp63DeflectionStatus.Calculated, result.Status);
    }

    [Fact]
    public void Tee_ReversedLongMoment_NotApplicable()
    {
        var result = Sp63DeflectionChecker.Check(
            Sp63SlsTestSections.TopTee(),
            new LoadItem { Mx = -80, N = 10 },
            new LoadItem { Mx = 20, N = 4 }, CalcType.N,
            Sp63SlsTestOptions.Deflection(Sp63NormalShapeKind.Tee));

        Assert.Equal(Sp63DeflectionStatus.NotApplicable, result.Status);
        Assert.Contains(result.ApplicabilityMessages, m => m.Code == "long_moment_reversed");
    }

    [Fact]
    public void Tee_ThroughTension_NotApplicable()
    {
        var result = Sp63DeflectionChecker.Check(
            Sp63SlsTestSections.TopTee(),
            new LoadItem { Mx = -10, N = 2000 },
            new LoadItem { Mx = -5, N = 800 }, CalcType.N,
            Sp63SlsTestOptions.Deflection(Sp63NormalShapeKind.Tee));

        Assert.Equal(Sp63DeflectionStatus.NotApplicable, result.Status);
        Assert.Contains(result.ApplicabilityMessages, m => m.Code == "curvature_through_tension");
    }

    [Fact]
    public void Tee_BiaxialLoad_NotApplicable()
    {
        var result = Sp63DeflectionChecker.Check(
            Sp63SlsTestSections.TopTee(),
            new LoadItem { Mx = -80, My = 10, N = 10 },
            new LoadItem { Mx = -20, N = 4 }, CalcType.N,
            Sp63SlsTestOptions.Deflection(Sp63NormalShapeKind.Tee));

        Assert.Equal(Sp63DeflectionStatus.NotApplicable, result.Status);
        Assert.Contains(result.ApplicabilityMessages, m => m.Code == "biaxial_load");
    }
}
