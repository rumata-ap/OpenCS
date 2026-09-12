using CScore;
using CScore.Sp63;
using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>Проверки случайного эксцентриситета и делегации коэффициента η.</summary>
public sealed class Sp63NormalEtaTests
{
    [Fact]
    public void Ea_UsesLOver600_WhenItIsMaximum()
    {
        Assert.Equal(0.020,
            Sp63MemberContext.AccidentalEccentricity(12.0, 0.30), precision: 12);
    }

    [Fact]
    public void Ea_UsesHOver30_WhenItIsMaximum()
    {
        Assert.Equal(0.020,
            Sp63MemberContext.AccidentalEccentricity(3.0, 0.60), precision: 12);
    }

    [Fact]
    public void Ea_IsAtLeastTenMillimetres()
    {
        Assert.Equal(0.010,
            Sp63MemberContext.AccidentalEccentricity(1.0, 0.10), precision: 12);
    }

    [Fact]
    public void DeterminateElement_AddsEaToStaticEccentricity()
    {
        Assert.Equal(0.025, Sp63MemberContext.EffectiveEccentricity(0.005, 0.020,
            Sp63StructuralScheme.StaticallyDeterminate), precision: 12);
    }

    [Fact]
    public void IndeterminateElement_TakesMaximumOfStaticAndEa()
    {
        Assert.Equal(0.020, Sp63MemberContext.EffectiveEccentricity(0.005, 0.020,
            Sp63StructuralScheme.StaticallyIndeterminate), precision: 12);
    }

    [Fact]
    public void NominalCentralCompression_StillUsesEa()
    {
        Assert.Equal(0.020, Sp63MemberContext.EffectiveEccentricity(0.0, 0.020,
            Sp63StructuralScheme.StaticallyIndeterminate), precision: 12);
    }

    [Fact]
    public void Compression_DelegatesEtaToExistingAmplifier_WithEaIncludedInM0()
    {
        var section = Sp63NormalFixtures.TwoLayerRectangle(0.30, 0.60,
            tensionArea: 0.0010, compressionArea: 0.0010);
        var options = Sp63NormalFixtures.MemberOptions();
        var result = Sp63NormalChecker.Check(section,
            new LoadItem { N = -800.0, Mx = -24.0 }, CalcType.C, options);

        Assert.NotNull(result.Eta);
        Assert.Equal(result.Variables["eta"], result.Eta!.Value.Eta, precision: 12);
        Assert.Equal(result.Variables["etaMEff"], result.Eta.Value.MEff, precision: 12);

        double ea = Sp63MemberContext.AccidentalEccentricity(6.0, 0.60);
        double e0 = Sp63MemberContext.EffectiveEccentricity(24.0 / 800.0, ea,
            Sp63StructuralScheme.StaticallyIndeterminate);
        var split = section.SplitStiffnessByMaterial();
        var expected = EccentricityAmplifier.AmplifyFormula(
            n: -800.0,
            m0: -800.0 * e0,
            l0: 4.2,
            h: 0.60,
            eiConcrete: split.EIxConcrete,
            eiRebar: split.EIxRebar,
            psi: 1.0);
        Assert.Equal(expected.Eta, result.Eta.Value.Eta, precision: 12);
        Assert.Equal(expected.Ncr, result.Eta.Value.Ncr, precision: 12);
        Assert.Equal(expected.D, result.Eta.Value.D, precision: 12);
        Assert.Equal(expected.MEff, result.Eta.Value.MEff, precision: 12);
    }

    [Fact]
    public void MissingL0_InMemberMode_IsNotApplicable_AndDoesNotCreateEta()
    {
        var context = Sp63NormalFixtures.MemberOptions().MemberContext with
        {
            EffectiveLengthL0 = null
        };
        var options = Sp63NormalFixtures.MemberOptions() with
        {
            MemberContext = context
        };
        var result = Sp63NormalChecker.Check(
            Sp63NormalFixtures.TwoLayerRectangle(0.30, 0.60, 0.0010, 0.0010),
            new LoadItem { N = -800.0, Mx = -24.0 }, CalcType.C, options);

        Assert.Equal(Sp63NormalStatus.NotApplicable, result.Status);
        Assert.Null(result.Eta);
    }

    [Fact]
    public void SectionOnlyExplicit_UsesNoEta_AndAddsInformationMessage()
    {
        var context = Sp63NormalFixtures.MemberOptions().MemberContext with
        {
            StabilityMode = Sp63NormalStabilityMode.SectionOnlyExplicit,
            EffectiveLengthL0 = null
        };
        var options = Sp63NormalFixtures.MemberOptions() with
        {
            MemberContext = context
        };
        var result = Sp63NormalChecker.Check(
            Sp63NormalFixtures.TwoLayerRectangle(0.30, 0.60, 0.0010, 0.0010),
            new LoadItem { N = -800.0, Mx = -24.0 }, CalcType.C, options);

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Null(result.Eta);
        Assert.Contains(result.InformationalMessages,
            message => message.Code == "stability_excluded_explicitly");
    }

    [Fact]
    public void UnstableEta_ReturnsStrengthFailure_WithNcrDetail()
    {
        var context = Sp63NormalFixtures.MemberOptions().MemberContext with
        {
            EffectiveLengthL0 = 100.0
        };
        var options = Sp63NormalFixtures.MemberOptions() with
        {
            MemberContext = context
        };
        var result = Sp63NormalChecker.Check(
            Sp63NormalFixtures.TwoLayerRectangle(0.30, 0.60, 0.0010, 0.0010),
            new LoadItem { N = -800.0, Mx = -24.0 }, CalcType.C, options);

        Assert.NotNull(result.Eta);
        Assert.False(result.Eta!.Value.Stable);
        Assert.False(result.StrengthPassed);
        Assert.Contains(result.StrengthDetails,
            detail => detail.NormReference == "8.1.15");
    }
}
