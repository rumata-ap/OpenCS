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
    public void Validate_DistributedPartialRange_DoesNotThrow()
    {
        // Срез 6: участок разрешён — восстановленная нагрузка кусочно-задана по станциям.
        var load = new StripLoad
        {
            Kind = StripLoadKind.DistributedUniform,
            StationStartFraction = 0.2,
            StationEndFraction = 0.8,
            QzKnM = -3.0
        };

        load.Validate();
    }

    [Fact]
    public void Validate_LinearWithFiniteFields_DoesNotThrow()
    {
        var load = new StripLoad
        {
            Kind = StripLoadKind.DistributedLinear,
            SourceTag = "recovered-1",
            StationStartFraction = 0.25,
            StationEndFraction = 0.5,
            QzKnM = -1.0,
            QzEndKnM = -4.0
        };

        load.Validate();
    }

    [Fact]
    public void Validate_UniformWithNonZeroEndIntensity_Throws()
    {
        var load = new StripLoad
        {
            Kind = StripLoadKind.DistributedUniform,
            StationStartFraction = 0.0,
            StationEndFraction = 1.0,
            QzKnM = -1.0,
            QzEndKnM = -4.0
        };

        Assert.Throws<ArgumentException>(() => load.Validate());
    }

    [Fact]
    public void Validate_LinearWithNonFiniteEndIntensity_Throws()
    {
        var load = new StripLoad
        {
            Kind = StripLoadKind.DistributedLinear,
            StationStartFraction = 0.0,
            StationEndFraction = 1.0,
            QyEndKnM = double.NaN
        };

        Assert.Throws<ArgumentException>(() => load.Validate());
    }

    [Fact]
    public void Validate_DistributedZeroLengthRange_Throws()
    {
        var load = new StripLoad
        {
            Kind = StripLoadKind.DistributedLinear,
            StationStartFraction = 0.4,
            StationEndFraction = 0.4
        };

        Assert.Throws<ArgumentException>(() => load.Validate());
    }

    [Fact]
    public void IntensityAt_Linear_InterpolatesInsideRangeAndZeroOutside()
    {
        var load = new StripLoad
        {
            Kind = StripLoadKind.DistributedLinear,
            StationStartFraction = 0.2,
            StationEndFraction = 0.6,
            QzKnM = -2.0,
            QzEndKnM = -6.0
        };

        Assert.Equal(-2.0, load.IntensityAt(0.2).Qz, 12);
        Assert.Equal(-4.0, load.IntensityAt(0.4).Qz, 12);
        Assert.Equal(-6.0, load.IntensityAt(0.6).Qz, 12);
        Assert.Equal(0.0, load.IntensityAt(0.1).Qz, 12);
        Assert.Equal(0.0, load.IntensityAt(0.9).Qz, 12);
    }

    [Fact]
    public void IntensityAt_Point_Throws()
    {
        var load = new StripLoad { Kind = StripLoadKind.Point, StationFraction = 0.5 };

        Assert.Throws<InvalidOperationException>(() => load.IntensityAt(0.5));
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
