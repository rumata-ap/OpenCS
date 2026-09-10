using CScore.Planar;
using CScore.Submodel;
using Xunit;

namespace CScore.Tests.Submodel;

public sealed class ChainAngleMathTests
{
    static readonly PlanarVector3 AxisX = new(1, 0, 0);

    [Fact]
    public void AngleBetweenLinesDeg_TreatsOppositeDirectionAsZero() =>
        Assert.Equal(0.0, ChainAngleMath.AngleBetweenLinesDeg(new PlanarVector3(-1, 0, 0), AxisX), 9);

    [Fact]
    public void AngleBetweenLinesDeg_ReturnsDegreesNotRadians() =>
        Assert.Equal(45.0, ChainAngleMath.AngleBetweenLinesDeg(new PlanarVector3(1, 1, 0), AxisX), 6);

    [Fact]
    public void AngleBetweenLinesDeg_IsFiniteOnNearlyParallelVectors() =>
        Assert.True(double.IsFinite(ChainAngleMath.AngleBetweenLinesDeg(new PlanarVector3(1, 1e-12, 0), AxisX)));

    [Fact]
    public void AngleToleranceDeg_IsNotApplicable_WhenSegmentShorterThanLineTolerance() =>
        Assert.Null(ChainAngleMath.AngleToleranceDeg(0.001, 0.002, 0.5));

    [Fact]
    public void AngleToleranceDeg_IsNotApplicable_AtExactBoundary() =>
        Assert.Null(ChainAngleMath.AngleToleranceDeg(0.002, 0.002, 0.5));

    [Fact]
    public void AngleToleranceDeg_RelaxesForShortSegments_InDegrees()
    {
        Assert.Equal(0.5, ChainAngleMath.AngleToleranceDeg(0.25, 0.002, 0.5)!.Value, 6);
        var tenth = ChainAngleMath.AngleToleranceDeg(0.1, 0.002, 0.5);
        Assert.Equal(Math.Asin(0.02) * 180.0 / Math.PI, tenth!.Value, 9);
        Assert.True(tenth.Value > 0.5);
    }

    [Fact]
    public void AngleToleranceDeg_KeepsAbsoluteToleranceOnLongSegments() =>
        Assert.Equal(0.5, ChainAngleMath.AngleToleranceDeg(10.0, 0.002, 0.5)!.Value, 9);
}
