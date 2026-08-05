using CScore.Planar;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class PlateStripGeometryBuilderTests
{
    [Fact]
    public void SupportLocus_StoresFrameAndStructuralMode()
    {
        var frame = Frame3D.Identity with { Origin = new PlanarVector3(2, 5, 0) };
        var locus = new SupportLocus { Frame = frame, StructuralMode = BeamJunctionMode.Support };

        Assert.Equal(frame, locus.Frame);
        Assert.Equal(BeamJunctionMode.Support, locus.StructuralMode);
    }

    [Fact]
    public void PlateStripGeometry_StoresPointsAndLength()
    {
        var geometry = new PlateStripGeometry
        {
            CenterLine = [new PlanarPoint2D(2, 5), new PlanarPoint2D(8, 5)],
            LeftBoundary = [new PlanarPoint2D(2, 6), new PlanarPoint2D(8, 6)],
            RightBoundary = [new PlanarPoint2D(2, 4), new PlanarPoint2D(8, 4)],
            Polygon = [new PlanarPoint2D(2, 4), new PlanarPoint2D(8, 4), new PlanarPoint2D(8, 6), new PlanarPoint2D(2, 6)],
            LengthM = 6
        };

        Assert.Equal(2, geometry.CenterLine.Count);
        Assert.Equal(4, geometry.Polygon.Count);
        Assert.Equal(6, geometry.LengthM);
    }
}
