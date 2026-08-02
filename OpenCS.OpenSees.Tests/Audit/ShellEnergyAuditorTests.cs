using OpenCS.OpenSees.Audit;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests.Audit;

public sealed class ShellEnergyAuditorTests
{
    [Fact]
    public void DetermineConfidence_NativeEnergyResponse_IsNativeResponse()
    {
        ShellEnergyConfidence confidence = ShellEnergyAuditor.DetermineConfidence(
            hasNativeEnergyResponse: true, hasStateIntegralData: true, hasLoadHistory: true);

        Assert.Equal(ShellEnergyConfidence.NativeResponse, confidence);
    }

    [Fact]
    public void DetermineConfidence_StateIntegralWithoutNative_IsStateIntegral()
    {
        ShellEnergyConfidence confidence = ShellEnergyAuditor.DetermineConfidence(
            hasNativeEnergyResponse: false, hasStateIntegralData: true, hasLoadHistory: true);

        Assert.Equal(ShellEnergyConfidence.StateIntegral, confidence);
    }

    [Fact]
    public void DetermineConfidence_LoadHistoryOnly_IsExternalWorkOnly()
    {
        ShellEnergyConfidence confidence = ShellEnergyAuditor.DetermineConfidence(
            hasNativeEnergyResponse: false, hasStateIntegralData: false, hasLoadHistory: true);

        Assert.Equal(ShellEnergyConfidence.ExternalWorkOnly, confidence);
    }

    [Fact]
    public void DetermineConfidence_NoSources_IsUnavailable()
    {
        ShellEnergyConfidence confidence = ShellEnergyAuditor.DetermineConfidence(
            hasNativeEnergyResponse: false, hasStateIntegralData: false, hasLoadHistory: false);

        Assert.Equal(ShellEnergyConfidence.Unavailable, confidence);
    }

    [Fact]
    public void ExternalWork_TrapezoidRule_IntegratesWorkDotOverLoadFactor()
    {
        var samples = new[]
        {
            new ShellEnergySample(LoadFactor: 0.0, WorkDot: 0.0),
            new ShellEnergySample(LoadFactor: 0.5, WorkDot: 100.0),
            new ShellEnergySample(LoadFactor: 1.0, WorkDot: 250.0)
        };

        double work = ShellEnergyAuditor.ExternalWork(samples);

        Assert.Equal(112.5, work, 12);
    }

    [Fact]
    public void ExternalWork_EmptySamples_IsZero()
    {
        Assert.Equal(0.0, ShellEnergyAuditor.ExternalWork([]), 12);
    }

    [Fact]
    public void StateIntegral_UsesExplicitConjugateComponentPairs()
    {
        var samples = new[]
        {
            new ShellMaterialEnergySample([0, 0, 0, 0, 0], [0, 0, 0, 0, 0], 2.0),
            new ShellMaterialEnergySample([100, 50, 0, 0, 0], [0.01, 0.02, 0, 0, 0], 2.0)
        };

        double work = ShellEnergyAuditor.StateIntegral(
            samples,
            [(StressIndex: 0, StrainIndex: 0), (StressIndex: 1, StrainIndex: 1)]);

        Assert.Equal(2.0, work, 12);
    }

    [Fact]
    public void KinematicReactionWork_SumsForceTimesDisplacementOverNodes()
    {
        var step = new RCShellStepResult(
            1, 0, 1.0, true,
            [new ShellNodeDisplacement(1, 0, 0, 0.001, 0, 0, 0)],
            [new ShellNodeReaction(1, 0, 0, 1000, 0, 0, 0)],
            [], [], []);

        double work = ShellEnergyAuditor.KinematicReactionWork([step]);

        Assert.Equal(1.0, work, 12);
    }
}
