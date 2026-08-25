using CScore.Sp63Shear;
using Xunit;

namespace CScore.Tests.Sp63Shear;

/// <summary>Геометрическая модель наклонного сечения: интервал, точка 0, снятие усилий.</summary>
public sealed class InclinedSectionModelTests
{
    [Fact]
    public void Point0_BackwardDirection_IsStationMinusProjection()
    {
        var model = new InclinedSectionModel(Station: 3.0, Direction: -1, ProjectionC: 1.1);

        Assert.Equal(1.9, model.Point0, 9);
    }

    [Fact]
    public void Point0_ForwardDirection_IsStationPlusProjection()
    {
        var model = new InclinedSectionModel(Station: 3.0, Direction: +1, ProjectionC: 1.1);

        Assert.Equal(4.1, model.Point0, 9);
    }

    [Fact]
    public void AppliedMoment_IsTakenAtPoint0_NotAtStation()
    {
        // M(s) = 100·s: в точке 0 при s = 3, C = 1, dir = −1 момент равен M(2) = 200
        var profile = new UniformLoadProfile(
            q0: 100.0, m0: 0.0, n0: 0.0, distributedLoad: 0.0, supportDistance: 6.0);
        var model = new InclinedSectionModel(Station: 3.0, Direction: -1, ProjectionC: 1.0);

        Assert.Equal(200.0, model.AppliedMoment(profile), 9);
    }

    [Fact]
    public void AppliedShear_TakesMaximumAbsoluteValueInInterval()
    {
        // Q убывает от 90 (s = 1) до 60 (s = 2): максимум по интервалу равен 90
        var profile = new UniformLoadProfile(
            q0: 120.0, m0: 0.0, n0: 0.0, distributedLoad: 30.0, supportDistance: 4.0);
        var model = new InclinedSectionModel(Station: 1.0, Direction: +1, ProjectionC: 1.0);

        Assert.Equal(90.0, model.AppliedShear(profile), 9);
    }

    [Fact]
    public void AppliedShear_BackwardDirection_ScansTowardsPoint0()
    {
        // Направление −1 от s = 2: интервал [1; 2], максимум Q = 90 в s = 1
        var profile = new UniformLoadProfile(
            q0: 120.0, m0: 0.0, n0: 0.0, distributedLoad: 30.0, supportDistance: 4.0);
        var model = new InclinedSectionModel(Station: 2.0, Direction: -1, ProjectionC: 1.0);

        Assert.Equal(90.0, model.AppliedShear(profile), 9);
    }

    [Fact]
    public void AppliedShear_FindsPeakAtInteriorNode_NotOnProbeGrid()
    {
        // Пик 150 кН в узле s = 0,5 — сетка из 41 пробы его пропускала (H-02)
        var samples = new List<ForceSample>
        {
            new(0.0, 40.0, 0.0, 0.0),
            new(0.5, 150.0, 20.0, 0.0),
            new(1.0, 40.0, 40.0, 0.0)
        };
        var profile = new SampledProfile(samples, 0.0, 1.0);
        var model = new InclinedSectionModel(Station: 0.0, Direction: +1, ProjectionC: 1.0);

        Assert.Equal(150.0, model.AppliedShear(profile), 6);
    }

    [Fact]
    public void AppliedShear_ZeroProjection_TakesValueAtStation()
    {
        var profile = new UniformLoadProfile(
            q0: 120.0, m0: 0.0, n0: 0.0, distributedLoad: 30.0, supportDistance: 4.0);
        var model = new InclinedSectionModel(Station: 1.0, Direction: +1, ProjectionC: 0.0);

        Assert.Equal(90.0, model.AppliedShear(profile), 9);
    }

    [Fact]
    public void SupportDistance_UsesProfileAndDirection()
    {
        var profile = new UniformLoadProfile(
            q0: 120.0, m0: 0.0, n0: 0.0, distributedLoad: 30.0, supportDistance: 4.0);
        var model = new InclinedSectionModel(Station: 0.4, Direction: -1, ProjectionC: 1.0);

        Assert.Equal(0.4, model.SupportDistance(profile), 9);
    }
}
