using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class StripLoadTests
{
    [Fact]
    public void Validate_DistributedWithFiniteFields_DoesNotThrow()
    {
        var load = new StripLoad
        {
            Kind = StripLoadKind.DistributedUniform,
            SourceTag = "surface-1",
            StationStartFraction = 0.0,
            StationEndFraction = 1.0,
            QxKnM = 1.0,
            QyKnM = 0.0,
            QzKnM = -5.0
        };

        load.Validate();
    }

    [Fact]
    public void Validate_PointWithFiniteFields_DoesNotThrow()
    {
        var load = new StripLoad
        {
            Kind = StripLoadKind.Point,
            SourceTag = "point-1",
            StationFraction = 0.5,
            PxKn = 1.0,
            PyKn = 2.0,
            PzKn = 3.0,
            MxKnM = 0.0,
            MzKnM = 0.4
        };

        load.Validate();
    }

    [Fact]
    public void Validate_NonFiniteField_Throws()
    {
        var load = new StripLoad { Kind = StripLoadKind.DistributedUniform, QxKnM = double.NaN };

        Assert.Throws<ArgumentException>(() => load.Validate());
    }

    [Fact]
    public void Validate_DistributedStartAfterEnd_Throws()
    {
        var load = new StripLoad
        {
            Kind = StripLoadKind.DistributedUniform,
            StationStartFraction = 0.7,
            StationEndFraction = 0.3
        };

        Assert.Throws<ArgumentException>(() => load.Validate());
    }

    [Fact]
    public void Validate_DistributedPartialRange_Throws()
    {
        // В этом срезе Distributed всегда занимает весь пролёт (Project не умеет частичное
        // перекрытие) — Validate() запрещает диапазон уже, а не молча теряет его в Project.
        var load = new StripLoad
        {
            Kind = StripLoadKind.DistributedUniform,
            StationStartFraction = 0.2,
            StationEndFraction = 0.8
        };

        Assert.Throws<ArgumentException>(() => load.Validate());
    }

    [Fact]
    public void Validate_PointStationOutOfRange_Throws()
    {
        var load = new StripLoad { Kind = StripLoadKind.Point, StationFraction = 1.5 };

        Assert.Throws<ArgumentOutOfRangeException>(() => load.Validate());
    }

    [Fact]
    public void Validate_DistributedStationOutOfRange_Throws()
    {
        var load = new StripLoad
        {
            Kind = StripLoadKind.DistributedUniform,
            StationStartFraction = -0.1,
            StationEndFraction = 1.0
        };

        Assert.Throws<ArgumentException>(() => load.Validate());
    }
}
