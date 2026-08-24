using CScore.Sp63Shear;
using Xunit;

namespace CScore.Tests.Sp63Shear;

/// <summary>Табличный профиль усилий из результатов МКЭ.</summary>
public sealed class SampledProfileTests
{
    [Fact]
    public void Q_IsInterpolatedLinearly()
    {
        var profile = Beam(segments: 2);

        Assert.Equal(120.0 - 30.0 * 1.0, profile.Q(1.0), 6);
        Assert.Equal(120.0 - 30.0 * 3.0, profile.Q(3.0), 6);
    }

    [Fact]
    public void M_HermiteInterpolation_ReproducesParabolaExactly()
    {
        // Балка 4 м, q = 30 кН/м, Q0 = 120 кН; всего 2 КЭ по 2 м — узлы в 0, 2, 4 м.
        var profile = Beam(segments: 2);

        // Точное значение в середине первого КЭ: M(1) = 120·1 − 30·1²/2 = 105
        Assert.Equal(105.0, profile.M(1.0), 6);
        // И в середине второго: M(3) = 120·3 − 30·9/2 = 225
        Assert.Equal(225.0, profile.M(3.0), 6);
    }

    [Fact]
    public void M_LinearInterpolationWouldUnderestimate_HermiteDoesNot()
    {
        var profile = Beam(segments: 2);

        double linear = 0.5 * (Exact(0.0).M + Exact(2.0).M);   // 0 и 180 → 90
        Assert.True(profile.M(1.0) > linear);
    }

    [Fact]
    public void N_IsInterpolatedLinearly()
    {
        var samples = new List<ForceSample>
        {
            new(0.0, 100.0, 0.0, -50.0),
            new(2.0, 100.0, 200.0, -90.0)
        };
        var profile = new SampledProfile(samples, 0.0, 2.0);

        Assert.Equal(-70.0, profile.N(1.0), 9);
    }

    [Fact]
    public void StationRange_SpansSamples()
    {
        var profile = Beam(segments: 2);

        Assert.Equal(0.0, profile.StationRange.Min, 12);
        Assert.Equal(4.0, profile.StationRange.Max, 12);
        Assert.Equal(4.0, profile.Length, 12);
    }

    [Fact]
    public void SupportDistanceAt_UsesEndsOfElement()
    {
        var profile = Beam(segments: 2);

        Assert.Equal(1.0, profile.SupportDistanceAt(3.0, direction: +1), 9);
        Assert.Equal(3.0, profile.SupportDistanceAt(3.0, direction: -1), 9);
    }

    [Fact]
    public void Constructor_UnsortedSamples_AreOrderedByS()
    {
        var samples = new List<ForceSample>
        {
            new(2.0, 60.0, 180.0, 0.0),
            new(0.0, 120.0, 0.0, 0.0)
        };
        var profile = new SampledProfile(samples, 0.0, 2.0);

        Assert.Equal(0.0, profile.Samples[0].S, 12);
        Assert.Equal(2.0, profile.Samples[1].S, 12);
    }

    [Fact]
    public void Constructor_SingleSample_Throws()
    {
        var samples = new List<ForceSample> { new(0.0, 1.0, 2.0, 3.0) };

        Assert.Throws<ArgumentException>(() => new SampledProfile(samples, 0.0, 0.0));
    }

    [Fact]
    public void Constructor_NearlyEqualCoordinates_Throws()
    {
        var samples = new List<ForceSample>
        {
            new(0.0, 120.0, 0.0, 0.0),
            new(1e-12, 119.0, 0.0, 0.0),
            new(2.0, 60.0, 180.0, 0.0)
        };

        Assert.Throws<ArgumentException>(() => new SampledProfile(samples, 0.0, 2.0));
    }

    [Fact]
    public void Constructor_NonFiniteValue_Throws()
    {
        var samples = new List<ForceSample>
        {
            new(0.0, double.NaN, 0.0, 0.0),
            new(2.0, 60.0, 180.0, 0.0)
        };

        Assert.Throws<ArgumentException>(() => new SampledProfile(samples, 0.0, 2.0));
    }

    [Fact]
    public void Constructor_DuplicateCoordinate_IsTreatedAsJump()
    {
        // Сосредоточенная сила в s = 2: Q скачком меняется со 120 на −80
        var samples = new List<ForceSample>
        {
            new(0.0, 120.0, 0.0, 0.0),
            new(2.0, 120.0, 240.0, 0.0),
            new(2.0, -80.0, 240.0, 0.0),
            new(4.0, -80.0, 80.0, 0.0)
        };
        var profile = new SampledProfile(samples, 0.0, 4.0);

        Assert.Equal(120.0, Math.Abs(profile.Q(2.0)), 6);   // берётся большее по модулю
        Assert.Equal(120.0, profile.MaxAbsQ(1.0, 3.0), 6);
    }

    [Fact]
    public void MaxAbsQ_TakesInteriorNodeExactly()
    {
        // Пик 150 кН в узле s = 0,5 — равномерная сетка проб его пропускает
        var samples = new List<ForceSample>
        {
            new(0.0, 40.0, 0.0, 0.0),
            new(0.5, 150.0, 20.0, 0.0),
            new(1.0, 40.0, 40.0, 0.0)
        };
        var profile = new SampledProfile(samples, 0.0, 1.0);

        Assert.Equal(150.0, profile.MaxAbsQ(0.0, 1.0), 6);
        Assert.Equal(40.0, profile.MaxAbsQ(0.0, 0.0), 6);
    }

    [Fact]
    public void M_InconsistentWithQ_FallsBackToLinearAndWarns()
    {
        // Знак Q противоположен производной момента: M растёт, Q отрицательна
        var samples = new List<ForceSample>
        {
            new(0.0, -120.0, 0.0, 0.0),
            new(2.0, -60.0, 180.0, 0.0)
        };
        var profile = new SampledProfile(samples, 0.0, 2.0);

        Assert.Equal(90.0, profile.M(1.0), 6);              // линейная интерполяция
        Assert.NotEmpty(profile.Warnings);
    }

    [Fact]
    public void Warnings_ConsistentProfile_IsEmpty()
    {
        Assert.Empty(Beam(segments: 2).Warnings);
    }

    [Fact]
    public void SupportDistanceAt_EndWithoutSupport_ReportsZero()
    {
        var profile = new SampledProfile(
            [new(0.0, 120.0, 0.0, 0.0), new(4.0, 0.0, 240.0, 0.0)],
            0.0, 4.0, supportAtStart: true, supportAtEnd: false);

        Assert.Equal(0.0, profile.SupportDistanceAt(3.0, direction: +1), 12);
        Assert.False(profile.HasSupport(+1));
        Assert.Equal(3.0, profile.SupportDistanceAt(3.0, direction: -1), 9);
    }

    static SampledProfile Beam(int segments)
    {
        var samples = new List<ForceSample>();
        double step = 4.0 / segments;
        for (int i = 0; i <= segments; i++)
        {
            double s = i * step;
            var exact = Exact(s);
            samples.Add(new ForceSample(s, exact.Q, exact.M, 0.0));
        }
        return new SampledProfile(samples, 0.0, 4.0);
    }

    static (double Q, double M) Exact(double s) => (120.0 - 30.0 * s, 120.0 * s - 15.0 * s * s);
}
