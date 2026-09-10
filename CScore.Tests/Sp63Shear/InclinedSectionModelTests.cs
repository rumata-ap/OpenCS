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
    public void AppliedShear_TakesValueAtStation_ForwardDirection()
    {
        // По п. 8.1.33 Q определяется статически по одну сторону от сечения — т.е. в самой
        // стоянке (Station), а не как максимум по отрезку [Station; Point0]: Q(1)=90.
        var profile = new UniformLoadProfile(
            q0: 120.0, m0: 0.0, n0: 0.0, distributedLoad: 30.0, supportDistance: 4.0);
        var model = new InclinedSectionModel(Station: 1.0, Direction: +1, ProjectionC: 1.0);

        Assert.Equal(90.0, model.AppliedShear(profile), 9);
    }

    [Fact]
    public void AppliedShear_BackwardDirection_UsesStationNotPoint0()
    {
        // Station=2 (Q=60), Point0=1 (Q=90, ближе к опоре). До исправления H-03 бралось
        // MaxAbsQ по всему интервалу — 90, что при переборе C вырождало приложенную силу в
        // константу ≈ опорной реакции независимо от C (см. заметку памяти о находке по
        // Пособию Краковского). По п. 8.1.33 Q берётся в самой проверяемой стоянке — 60.
        var profile = new UniformLoadProfile(
            q0: 120.0, m0: 0.0, n0: 0.0, distributedLoad: 30.0, supportDistance: 4.0);
        var model = new InclinedSectionModel(Station: 2.0, Direction: -1, ProjectionC: 1.0);

        Assert.Equal(60.0, model.AppliedShear(profile), 9);
    }

    [Fact]
    public void AppliedShear_IgnoresInteriorPeakBetweenStationAndPoint0()
    {
        // Пик 150 кН в узле s=0,5 (между Station=0 и Point0=1) не должен влиять на
        // приложенную силу — она берётся статически в Station=0, т.е. Q(0)=40. Способность
        // самого профиля точно находить внутренний пик отдельно проверяется в
        // SampledProfileTests.MaxAbsQ_TakesInteriorNodeExactly — здесь важно, что
        // AppliedShear теперь его сознательно игнорирует (см. п. 8.1.33: «наиболее опасное
        // загружение» — это про выбор варианта загружения, а не про максимум по точкам
        // одного и того же загружения внутри проекции C).
        var samples = new List<ForceSample>
        {
            new(0.0, 40.0, 0.0, 0.0),
            new(0.5, 150.0, 20.0, 0.0),
            new(1.0, 40.0, 40.0, 0.0)
        };
        var profile = new SampledProfile(samples, 0.0, 1.0);
        var model = new InclinedSectionModel(Station: 0.0, Direction: +1, ProjectionC: 1.0);

        Assert.Equal(40.0, model.AppliedShear(profile), 6);
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
