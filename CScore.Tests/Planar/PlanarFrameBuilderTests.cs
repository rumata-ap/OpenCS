using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public class PlanarFrameBuilderTests
{
    [Fact]
    public void BuildPlateFrame_UsesNodeAsOriginAndGlobalAxes()
    {
        var origin = new PlanarVector3(3, 4, 2.5);

        var frame = PlanarFrameBuilder.BuildPlateFrame(origin);

        Assert.Equal(origin, frame.Origin);
        Assert.Equal(new PlanarVector3(1, 0, 0), frame.LocalX);
        Assert.Equal(new PlanarVector3(0, 1, 0), frame.LocalY);
        Assert.Equal(new PlanarVector3(0, 0, 1), frame.LocalZ);
        frame.Validate();
    }

    [Fact]
    public void BuildWallFrame_UsesFirstNodeAsOriginAndDirectionAsLocalX()
    {
        var a = new PlanarVector3(0, 0, 3);
        var b = new PlanarVector3(5, 0, 3);

        var frame = PlanarFrameBuilder.BuildWallFrame(a, b);

        Assert.Equal(a, frame.Origin);
        Assert.Equal(new PlanarVector3(1, 0, 0), frame.LocalX);
        Assert.Equal(new PlanarVector3(0, 0, 1), frame.LocalY);
        frame.Validate();
    }

    [Fact]
    public void BuildWallFrame_DirectionIgnoresElevationDifference()
    {
        var a = new PlanarVector3(0, 0, 0);
        var b = new PlanarVector3(0, 5, 10); // разные Z — не должно влиять на план

        var frame = PlanarFrameBuilder.BuildWallFrame(a, b);

        Assert.Equal(new PlanarVector3(0, 1, 0), frame.LocalX);
    }

    [Fact]
    public void BuildWallFrame_ThrowsWhenNodesCoincideInPlan()
    {
        var a = new PlanarVector3(2, 2, 0);
        var b = new PlanarVector3(2, 2, 5); // совпадают в плане, различаются только по Z

        Assert.Throws<InvalidOperationException>(() => PlanarFrameBuilder.BuildWallFrame(a, b));
    }

    [Fact]
    public void BuildSpatialPlateFrame_BuildsOrthonormalBasisFromThreeNodes()
    {
        var a = new PlanarVector3(0, 0, 0);
        var b = new PlanarVector3(1, 0, 0);
        var c = new PlanarVector3(0, 1, 1);

        var frame = PlanarFrameBuilder.BuildSpatialPlateFrame(a, b, c);

        Assert.Equal(a, frame.Origin);
        Assert.Equal(new PlanarVector3(1, 0, 0), frame.LocalX);
        frame.Validate();
    }

    [Fact]
    public void BuildSpatialPlateFrame_ThrowsWhenFirstTwoNodesCoincide()
    {
        var a = new PlanarVector3(1, 1, 1);
        var c = new PlanarVector3(0, 5, 0);

        Assert.Throws<InvalidOperationException>(() => PlanarFrameBuilder.BuildSpatialPlateFrame(a, a, c));
    }

    [Fact]
    public void BuildSpatialPlateFrame_ThrowsWhenThreeNodesAreCollinear()
    {
        var a = new PlanarVector3(0, 0, 0);
        var b = new PlanarVector3(1, 0, 0);
        var c = new PlanarVector3(2, 0, 0); // на одной прямой с a,b

        Assert.Throws<InvalidOperationException>(() => PlanarFrameBuilder.BuildSpatialPlateFrame(a, b, c));
    }
}
