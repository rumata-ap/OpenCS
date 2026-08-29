using CScore.Sp63Shear;
using Xunit;

namespace CScore.Tests.Sp63Shear;

/// <summary>Момент в наклонном сечении по п. 8.1.35 и зоны его проверки.</summary>
public sealed class MomentFormulasTests
{
    const double H0 = 0.55;

    [Fact]
    public void LongitudinalMoment_IsNsTimesLeverArm()
    {
        double ms = MomentFormulas.LongitudinalMoment(Input());

        Assert.Equal(1.0 * 435.0 * 0.9 * H0, ms, 9);
    }

    [Fact]
    public void LongitudinalMoment_AppliesAnchorageFactor()
    {
        double ms = MomentFormulas.LongitudinalMoment(Input(anchorage: 0.6));

        Assert.Equal(0.6 * 435.0 * 0.9 * H0, ms, 9);
    }

    [Fact]
    public void StirrupMoment_HasNoPhiSwFactor()
    {
        double c = 1.5 * H0;
        double msw = MomentFormulas.StirrupMoment(Input(), projectionC: c);

        Assert.Equal(0.5 * 200.0 * c * c, msw, 9);
        Assert.NotEqual(0.5 * ShearFormulas.PhiSw * 200.0 * c * c, msw, 9);
    }

    [Fact]
    public void SimplifiedStirrupMoment_UsesHalfQswH0Squared()
    {
        double msw = MomentFormulas.SimplifiedStirrupMoment(Input());

        Assert.Equal(0.5 * 200.0 * H0 * H0, msw, 9);
    }

    [Fact]
    public void StirrupMoment_DoesNotApplyNonNormativeHalfDepthCutoff()
    {
        var input = Input(sw: 0.4);
        double c = 1.5 * H0;

        double msw = MomentFormulas.StirrupMoment(input, projectionC: c);

        Assert.Equal(0.5 * 200.0 * c * c, msw, 9);
    }

    [Fact]
    public void IsInZone_NearSupport_IsTrue()
    {
        var profile = new UniformLoadProfile(120.0, 0.0, 0.0, 30.0, supportDistance: 6.0);

        Assert.True(MomentCheckZones.IsInZone(0.5, Input(), profile));
        Assert.True(MomentCheckZones.IsInZone(5.6, Input(), profile));
    }

    [Fact]
    public void IsInZone_MidSpanWithoutCutoffs_IsFalse()
    {
        var profile = new UniformLoadProfile(120.0, 0.0, 0.0, 30.0, supportDistance: 6.0);

        Assert.False(MomentCheckZones.IsInZone(3.0, Input(), profile));
    }

    [Fact]
    public void IsInZone_NearDeclaredCutoff_IsTrue()
    {
        var profile = new UniformLoadProfile(120.0, 0.0, 0.0, 30.0, supportDistance: 6.0);
        var input = Input(cutoffs: [3.0]);

        Assert.True(MomentCheckZones.IsInZone(3.4, input, profile));
        Assert.False(MomentCheckZones.IsInZone(4.5, input, profile));
    }

    [Fact]
    public void IsInZone_ConstantProfile_IsAlwaysTrue()
    {
        var profile = new ConstantProfile(120.0, 85.0, 0.0, supportDistance: 0.0);

        Assert.True(MomentCheckZones.IsInZone(0.0, Input(), profile));
    }

    static ShearInclinedInput Input(
        double anchorage = 1.0, double[]? cutoffs = null, double sw = 0.15) => new(
        B: 0.30, H0: H0, Rb: 14_500.0, Rbt: 1_050.0, Qsw: 200.0, Sw: sw, Ns: 435.0,
        Kind: ElementKind.BendingUnstressed, AnchorageFactor: anchorage,
        StationStep: 0.0, ProjectionStep: 0.0,
        MomentZoneLength: 0.0, BarCutoffs: cutoffs ?? [], CheckMoment: true, PhiNOverride: null);
}
