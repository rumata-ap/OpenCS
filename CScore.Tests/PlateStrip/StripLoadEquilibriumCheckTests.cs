using CScore.Planar;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

/// <summary>Срез 7, Task 7: полнота переноса нагрузки на полосу.</summary>
public sealed class StripLoadEquilibriumCheckTests
{
    const double Length = 6.0;

    [Fact]
    public void UniformLoad_ResultantIsIntensityTimesLength()
    {
        var loads = new StripLoadSet([Uniform(-10.0)]);

        var (force, moment) = StripLoadEquilibriumCheck.Resultant(loads, Length);

        Assert.Equal(-60.0, force.Z, 9);
        Assert.Equal(-60.0 * 3.0, moment, 9); // равнодействующая в середине пролёта
    }

    [Fact]
    public void PartialUniformLoad_IntegratesOnlyItsSpan()
    {
        var loads = new StripLoadSet([Uniform(-10.0, a: 0.25, b: 0.75)]);

        var (force, moment) = StripLoadEquilibriumCheck.Resultant(loads, Length);

        Assert.Equal(-30.0, force.Z, 9);
        Assert.Equal(-30.0 * 3.0, moment, 9);
    }

    [Fact]
    public void LinearLoad_IntegratesTrapezoidally()
    {
        var loads = new StripLoadSet(
        [
            new StripLoad
            {
                SourceTag = "lin", Kind = StripLoadKind.DistributedLinear,
                StationStartFraction = 0.0, StationEndFraction = 1.0,
                QzKnM = 0.0, QzEndKnM = -20.0
            }
        ]);

        var (force, moment) = StripLoadEquilibriumCheck.Resultant(loads, Length);

        Assert.Equal(-60.0, force.Z, 9);            // ½·20·6
        Assert.Equal(-60.0 * 4.0, moment, 9);        // центр тяжести треугольника: 2/3 пролёта
    }

    [Fact]
    public void PointLoad_ContributesAtItsStation()
    {
        var loads = new StripLoadSet(
        [
            new StripLoad
            {
                SourceTag = "p", Kind = StripLoadKind.Point,
                StationFraction = 0.25, PzKn = -12.0
            }
        ]);

        var (force, moment) = StripLoadEquilibriumCheck.Resultant(loads, Length);

        Assert.Equal(-12.0, force.Z, 9);
        Assert.Equal(-12.0 * 1.5, moment, 9);
    }

    [Fact]
    public void CompleteTransfer_Passes()
    {
        var loads = new StripLoadSet([Uniform(-10.0), Point(-12.0, 0.5)]);

        var result = StripLoadEquilibriumCheck.Run(
            loads, Length, new PlanarVector3(0, 0, -72.0), -216.0);

        Assert.True(result.IsCalculable);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void IncompleteTransfer_IsReported()
    {
        // Ожидалась вся поверхностная нагрузка плюс краевое действие, перенесена только часть.
        var loads = new StripLoadSet([Uniform(-10.0)]);

        var result = StripLoadEquilibriumCheck.Run(
            loads, Length, new PlanarVector3(0, 0, -72.0));

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_boundary_equilibrium_mismatch");
    }

    [Fact]
    public void DoubleCountedLoad_IsReported()
    {
        var loads = new StripLoadSet([Uniform(-10.0), Uniform(-10.0)]);

        var result = StripLoadEquilibriumCheck.Run(
            loads, Length, new PlanarVector3(0, 0, -60.0));

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_boundary_equilibrium_mismatch");
    }

    [Fact]
    public void MomentMismatch_IsReported_EvenWhenForceMatches()
    {
        // Та же равнодействующая, но приложенная не там: силы совпадают, момент — нет.
        var loads = new StripLoadSet([Point(-60.0, 0.25)]);

        var result = StripLoadEquilibriumCheck.Run(
            loads, Length, new PlanarVector3(0, 0, -60.0), -180.0);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics,
            d => d.Code == "plate_strip_boundary_equilibrium_mismatch" && d.Message.Contains("My"));
    }

    [Fact]
    public void InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => StripLoadEquilibriumCheck.Resultant(null!, Length));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => StripLoadEquilibriumCheck.Resultant(new StripLoadSet([]), 0.0));
    }

    static StripLoad Uniform(double qz, double a = 0.0, double b = 1.0) => new()
    {
        SourceTag = "q",
        Kind = StripLoadKind.DistributedUniform,
        StationStartFraction = a,
        StationEndFraction = b,
        QzKnM = qz
    };

    static StripLoad Point(double pz, double station) => new()
    {
        SourceTag = "p",
        Kind = StripLoadKind.Point,
        StationFraction = station,
        PzKn = pz
    };
}
