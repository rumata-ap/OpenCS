using CScore.Planar;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class PlateStripWidthSamplerTests
{
    [Fact]
    public void Point_AtMidStationAndZeroV_ReturnsCenterLineMidpoint()
    {
        var point = PlateStripWidthSampler.Point(Analogy(), 0.5, 0.0);

        Assert.Equal(5.0, point.U, 9);
        Assert.Equal(5.0, point.V, 9);
    }

    [Fact]
    public void Point_AtMidStationAndPositiveHalfWidth_ReturnsLeftBoundaryMidpoint()
    {
        var point = PlateStripWidthSampler.Point(Analogy(), 0.5, 1.0);

        Assert.Equal(5.0, point.U, 9);
        Assert.Equal(6.0, point.V, 9);
    }

    [Fact]
    public void Point_AtMidStationAndNegativeHalfWidth_ReturnsRightBoundaryMidpoint()
    {
        var point = PlateStripWidthSampler.Point(Analogy(), 0.5, -1.0);

        Assert.Equal(5.0, point.U, 9);
        Assert.Equal(4.0, point.V, 9);
    }

    [Fact]
    public void Point_AtStationZero_ReturnsStartOfCenterLine()
    {
        var point = PlateStripWidthSampler.Point(Analogy(), 0.0, 0.0);

        Assert.Equal(2.0, point.U, 9);
        Assert.Equal(5.0, point.V, 9);
    }

    [Fact]
    public void Point_AtStationOne_ReturnsEndOfCenterLine()
    {
        var point = PlateStripWidthSampler.Point(Analogy(), 1.0, 0.0);

        Assert.Equal(8.0, point.U, 9);
        Assert.Equal(5.0, point.V, 9);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void Point_StationOutOfRange_Throws(double station)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlateStripWidthSampler.Point(Analogy(), station, 0.0));
    }

    [Theory]
    [InlineData(-1.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void Point_VOutOfRange_Throws(double v)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlateStripWidthSampler.Point(Analogy(), 0.5, v));
    }

    [Fact]
    public void Point_LeftBoundaryHasFewerThanTwoPoints_ThrowsArgumentException()
    {
        var analogy = Analogy();
        analogy.Geometry.LeftBoundary = [new PlanarPoint2D(2, 6)];

        Assert.Throws<ArgumentException>(() => PlateStripWidthSampler.Point(analogy, 0.5, 0.0));
    }

    [Fact]
    public void Point_RightBoundaryIsEmpty_ThrowsArgumentException()
    {
        var analogy = Analogy();
        analogy.Geometry.RightBoundary = [];

        Assert.Throws<ArgumentException>(() => PlateStripWidthSampler.Point(analogy, 0.5, 0.0));
    }

    static PlateStripBeamAnalogy Analogy() => new()
    {
        Id = "strip-1",
        SourceRegionId = 77,
        ExplicitWidthM = 2.0,
        Fingerprint = "strip-fp",
        Geometry = new PlateStripGeometry
        {
            CenterLine = [new PlanarPoint2D(2, 5), new PlanarPoint2D(8, 5)],
            LeftBoundary = [new PlanarPoint2D(2, 6), new PlanarPoint2D(8, 6)],
            RightBoundary = [new PlanarPoint2D(2, 4), new PlanarPoint2D(8, 4)],
            Polygon = [new PlanarPoint2D(2, 4), new PlanarPoint2D(8, 4), new PlanarPoint2D(8, 6), new PlanarPoint2D(2, 6)],
            LengthM = 6.0
        }
    };
}
