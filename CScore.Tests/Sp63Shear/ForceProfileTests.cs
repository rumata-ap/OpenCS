using CScore.Sp63Shear;
using Xunit;

namespace CScore.Tests.Sp63Shear;

/// <summary>Профили усилий вдоль элемента.</summary>
public sealed class ForceProfileTests
{
    [Fact]
    public void ConstantProfile_ReturnsSameValuesAtAnyStation()
    {
        var profile = new ConstantProfile(q: 120.0, m: -85.0, n: -40.0, supportDistance: 0.0);

        Assert.Equal(120.0, profile.Q(0.0), 12);
        Assert.Equal(120.0, profile.Q(1.7), 12);
        Assert.Equal(-85.0, profile.M(1.7), 12);
        Assert.Equal(-40.0, profile.N(1.7), 12);
        Assert.Equal((0.0, 0.0), profile.StationRange);
    }

    [Fact]
    public void UniformLoadProfile_MatchesManualBeamCalculation()
    {
        // Q0 = 120 кН, M0 = 0, q = 30 кН/м, опора в 4,0 м
        var profile = new UniformLoadProfile(
            q0: 120.0, m0: 0.0, n0: 0.0, distributedLoad: 30.0, supportDistance: 4.0);

        Assert.Equal(120.0 - 30.0 * 2.0, profile.Q(2.0), 9);
        Assert.Equal(120.0 * 2.0 - 30.0 * 4.0 / 2.0, profile.M(2.0), 9);
        Assert.Equal(0.0, profile.N(2.0), 12);
    }

    [Fact]
    public void UniformLoadProfile_StationRangeSpansToSupport()
    {
        var profile = new UniformLoadProfile(
            q0: 120.0, m0: 0.0, n0: 0.0, distributedLoad: 30.0, supportDistance: 4.0);

        Assert.Equal(0.0, profile.StationRange.Min, 12);
        Assert.Equal(4.0, profile.StationRange.Max, 12);
        Assert.Equal(4.0, profile.Length, 12);
    }

    [Fact]
    public void SupportDistanceAt_CountsTowardsSupportOnly()
    {
        var profile = new UniformLoadProfile(
            q0: 120.0, m0: 0.0, n0: 0.0, distributedLoad: 30.0, supportDistance: 4.0);

        Assert.Equal(1.5, profile.SupportDistanceAt(2.5, direction: +1), 9);
        Assert.Equal(2.5, profile.SupportDistanceAt(2.5, direction: -1), 9);
    }

    [Fact]
    public void ConstantProfile_WithoutSupportDistance_ReportsZero()
    {
        var profile = new ConstantProfile(q: 120.0, m: 0.0, n: 0.0, supportDistance: 0.0);

        Assert.Equal(0.0, profile.SupportDistanceAt(0.0, direction: -1), 12);
        Assert.False(profile.HasSupport(-1));
    }

    [Fact]
    public void SupportDistanceAt_EndWithoutSupport_ReportsZero()
    {
        // Консоль: опора только в начале, s = 4 м — свободный конец
        var profile = new UniformLoadProfile(
            q0: 120.0, m0: 0.0, n0: 0.0, distributedLoad: 30.0, supportDistance: 4.0,
            supportAtStart: true, supportAtEnd: false);

        Assert.Equal(0.0, profile.SupportDistanceAt(2.5, direction: +1), 12);
        Assert.False(profile.HasSupport(+1));
        Assert.Equal(2.5, profile.SupportDistanceAt(2.5, direction: -1), 9);
        Assert.True(profile.HasSupport(-1));
    }

    [Fact]
    public void MaxAbsQ_ConstantProfile_IsAbsoluteValue()
    {
        var profile = new ConstantProfile(q: -120.0, m: 0.0, n: 0.0, supportDistance: 0.0);

        Assert.Equal(120.0, profile.MaxAbsQ(0.0, 1.5), 12);
    }

    [Fact]
    public void MaxAbsQ_UniformLoad_IsTakenAtIntervalEnds()
    {
        // Q(s) = 120 − 30·s линейна: максимум |Q| на [1; 2] равен 90 (в s = 1)
        var profile = new UniformLoadProfile(
            q0: 120.0, m0: 0.0, n0: 0.0, distributedLoad: 30.0, supportDistance: 4.0);

        Assert.Equal(90.0, profile.MaxAbsQ(1.0, 2.0), 9);
        Assert.Equal(120.0, profile.MaxAbsQ(4.0, 0.0), 9);   // порядок концов не важен
    }

    [Fact]
    public void MaxAbsQ_UniformLoad_SignChange_TakesLargestModulus()
    {
        // Q меняет знак в s = 4: на [0; 8] максимум модуля равен 120
        var profile = new UniformLoadProfile(
            q0: 120.0, m0: 0.0, n0: 0.0, distributedLoad: 30.0, supportDistance: 8.0);

        Assert.Equal(120.0, profile.MaxAbsQ(0.0, 8.0), 9);
    }
}
