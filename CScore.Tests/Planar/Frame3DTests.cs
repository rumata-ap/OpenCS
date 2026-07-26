using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public class Frame3DTests
{
    [Fact]
    public void Identity_PassesValidation()
    {
        Frame3D.Identity.Validate();
    }

    [Fact]
    public void Validate_ThrowsForNonOrthogonalAxes()
    {
        var frame = new Frame3D(
            PlanarVector3.Zero,
            new PlanarVector3(1, 0, 0),
            new PlanarVector3(0.7071, 0.7071, 0),
            new PlanarVector3(0, 0, 1));

        Assert.Throws<InvalidOperationException>(() => frame.Validate());
    }

    [Fact]
    public void Validate_ThrowsForNonUnitLocalX()
    {
        var frame = new Frame3D(
            PlanarVector3.Zero,
            new PlanarVector3(2, 0, 0),
            new PlanarVector3(0, 1, 0),
            new PlanarVector3(0, 0, 1));

        Assert.Throws<InvalidOperationException>(() => frame.Validate());
    }

    [Fact]
    public void Validate_ThrowsForLeftHandedTriple()
    {
        var frame = new Frame3D(
            PlanarVector3.Zero,
            new PlanarVector3(1, 0, 0),
            new PlanarVector3(0, 1, 0),
            new PlanarVector3(0, 0, -1));

        Assert.Throws<InvalidOperationException>(() => frame.Validate());
    }

    [Fact]
    public void FromPolygon_UnitSquareCounterClockwise_ReturnsFlatUpwardFrame()
    {
        double[] x = [0, 1, 1, 0];
        double[] y = [0, 0, 1, 1];
        double[] z = [0, 0, 0, 0];

        var frame = Frame3D.FromPolygon(x, y, z);

        Assert.True(Math.Abs(frame.LocalZ.X) < 1e-9);
        Assert.True(Math.Abs(frame.LocalZ.Y) < 1e-9);
        Assert.True(Math.Abs(frame.LocalZ.Z - 1.0) < 1e-9);
        Assert.True(Math.Abs(frame.LocalX.X - 1.0) < 1e-9);
        Assert.True(Math.Abs(frame.LocalY.Y - 1.0) < 1e-9);
        frame.Validate();
    }

    [Fact]
    public void FromPolygon_TwoPoints_Throws()
    {
        double[] x = [0, 1];
        double[] y = [0, 0];
        double[] z = [0, 0];

        Assert.Throws<ArgumentException>(() => Frame3D.FromPolygon(x, y, z));
    }

    [Fact]
    public void FromPolygon_DegenerateColinearPolygon_Throws()
    {
        double[] x = [0, 1, 2];
        double[] y = [0, 0, 0];
        double[] z = [0, 0, 0];

        Assert.Throws<InvalidOperationException>(() => Frame3D.FromPolygon(x, y, z));
    }
}
