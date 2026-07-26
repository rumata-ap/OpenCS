using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public class PlanarRegionTopologyValidatorTests
{
    static readonly double[] SquareCcwOpen = [0, 1, 1, 0];
    static readonly double[] SquareCcwOpenY = [0, 0, 1, 1];

    [Fact]
    public void ToOpenLoop_StripsExplicitClosingVertex()
    {
        double[] x = [0, 1, 1, 0, 0];
        double[] y = [0, 0, 1, 1, 0];

        var (ox, oy) = PlanarRegionTopologyValidator.ToOpenLoop(x, y);

        Assert.Equal(4, ox.Length);
        Assert.Equal(4, oy.Length);
    }

    [Fact]
    public void ToOpenLoop_LeavesAlreadyOpenLoopUnchanged()
    {
        var (ox, oy) = PlanarRegionTopologyValidator.ToOpenLoop(SquareCcwOpen, SquareCcwOpenY);
        Assert.Equal(4, ox.Length);
    }

    [Fact]
    public void SignedArea_IsPositiveForCounterClockwiseSquare()
    {
        double area = PlanarRegionTopologyValidator.SignedArea(SquareCcwOpen, SquareCcwOpenY);
        Assert.True(area > 0);
        Assert.Equal(1.0, area, 9);
    }

    [Fact]
    public void SignedArea_IsNegativeForClockwiseSquare()
    {
        double[] x = [0, 0, 1, 1];
        double[] y = [0, 1, 1, 0];
        double area = PlanarRegionTopologyValidator.SignedArea(x, y);
        Assert.True(area < 0);
    }

    [Fact]
    public void HasSelfIntersection_IsFalseForSimpleSquare()
    {
        Assert.False(PlanarRegionTopologyValidator.HasSelfIntersection(SquareCcwOpen, SquareCcwOpenY));
    }

    [Fact]
    public void HasSelfIntersection_IsTrueForBowtiePolygon()
    {
        double[] x = [0, 1, 0, 1];
        double[] y = [0, 1, 1, 0];
        Assert.True(PlanarRegionTopologyValidator.HasSelfIntersection(x, y));
    }

    [Fact]
    public void NormalizeWinding_ReversesClockwiseToCounterClockwise()
    {
        double[] x = [0, 0, 1, 1];
        double[] y = [0, 1, 1, 0];

        var (nx, ny) = PlanarRegionTopologyValidator.NormalizeWinding(x, y, ccw: true);

        Assert.True(PlanarRegionTopologyValidator.SignedArea(nx, ny) > 0);
    }

    [Fact]
    public void NormalizeWinding_LeavesAlreadyCorrectWindingUnchanged()
    {
        var (nx, ny) = PlanarRegionTopologyValidator.NormalizeWinding(SquareCcwOpen, SquareCcwOpenY, ccw: true);
        Assert.Equal(SquareCcwOpen, nx);
        Assert.Equal(SquareCcwOpenY, ny);
    }

    [Fact]
    public void ValidateLoop_ThrowsForSelfIntersection()
    {
        double[] x = [0, 1, 0, 1];
        double[] y = [0, 1, 1, 0];
        Assert.Throws<InvalidOperationException>(
            () => PlanarRegionTopologyValidator.ValidateLoop(x, y, "тестовый контур"));
    }

    [Fact]
    public void ValidateLoop_ThrowsForZeroArea()
    {
        double[] x = [0, 1, 2];
        double[] y = [0, 0, 0];
        Assert.Throws<InvalidOperationException>(
            () => PlanarRegionTopologyValidator.ValidateLoop(x, y, "тестовый контур"));
    }

    [Fact]
    public void ValidateLoop_ThrowsForFewerThanThreeVertices()
    {
        double[] x = [0, 1];
        double[] y = [0, 0];
        Assert.Throws<InvalidOperationException>(
            () => PlanarRegionTopologyValidator.ValidateLoop(x, y, "тестовый контур"));
    }

    [Fact]
    public void ValidateLoop_AcceptsSimpleSquare()
    {
        PlanarRegionTopologyValidator.ValidateLoop(SquareCcwOpen, SquareCcwOpenY, "тестовый контур");
    }
}
