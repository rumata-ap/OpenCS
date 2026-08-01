using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public class PlanarRegionTests
{
    static Contour OpenSquareContour(string tag = "outer") => new()
    {
        Tag = tag,
        X = [0, 1, 1, 0],
        Y = [0, 0, 1, 1],
        Id = 42
    };

    [Fact]
    public void CreateFromContour_NormalizesHullToCounterClockwise()
    {
        var clockwise = new Contour { X = [0, 0, 1, 1], Y = [0, 1, 1, 0] };

        var region = PlanarRegion.CreateFromContour(clockwise);

        Assert.True(PlanarRegionTopologyValidator.SignedArea(region.Hull.X, region.Hull.Y) > 0);
    }

    [Fact]
    public void CreateFromContour_SetsContourTypeHullOnOuterContour()
    {
        var region = PlanarRegion.CreateFromContour(OpenSquareContour());
        Assert.Equal(ContourType.Hull, region.Hull.Type);
    }

    [Fact]
    public void CreateFromContour_DefaultsMeshMaxElementSizeM()
    {
        var region = PlanarRegion.CreateFromContour(OpenSquareContour());

        Assert.Equal(0.2, region.MeshMaxElementSizeM);
    }

    [Fact]
    public void CreateFromContour_RecoversFrameWhenNoneProvided()
    {
        var region = PlanarRegion.CreateFromContour(OpenSquareContour());

        Assert.True(region.FrameIsRecovered);
        Assert.Equal(Frame3D.Identity, region.Frame);
        Assert.True(Math.Abs(region.Frame.LocalZ.Z - 1.0) < 1e-9);
    }

    [Fact]
    public void CreateFromContour_WithRecoveredFrame_KeepsContourCoordinatesInIdentityPlane()
    {
        var source = new Contour { X = [10, 12, 12, 10], Y = [20, 20, 21, 21] };

        var region = PlanarRegion.CreateFromContour(source);

        Assert.Equal(source.X, region.Hull.X.Take(4));
        Assert.Equal(source.Y, region.Hull.Y.Take(4));
        Assert.Equal(PlanarVector3.Zero, region.Frame.Origin);
        Assert.Equal(new PlanarVector3(1, 0, 0), region.Frame.LocalX);
        Assert.Equal(new PlanarVector3(0, 1, 0), region.Frame.LocalY);
        Assert.Equal(new PlanarVector3(0, 0, 1), region.Frame.LocalZ);
    }

    [Fact]
    public void CreateFromContour_UsesExplicitFrameWhenProvided()
    {
        var explicitFrame = new Frame3D(
            new PlanarVector3(0, 0, 3),
            new PlanarVector3(1, 0, 0),
            new PlanarVector3(0, 0, 1),
            new PlanarVector3(0, -1, 0));

        var region = PlanarRegion.CreateFromContour(OpenSquareContour(), frame: explicitFrame);

        Assert.False(region.FrameIsRecovered);
        Assert.Equal(explicitFrame, region.Frame);
    }

    [Fact]
    public void CreateFromContour_KeepsSourceContourIdForProvenance()
    {
        var region = PlanarRegion.CreateFromContour(OpenSquareContour());
        Assert.Equal(42, region.SourceContourId);
    }

    [Fact]
    public void CreateFromContour_DoesNotShareGeometryWithSourceContour()
    {
        var source = OpenSquareContour();
        var region = PlanarRegion.CreateFromContour(source);

        source.X[0] = 999;

        Assert.NotEqual(999, region.Hull.X[0]);
    }

    [Fact]
    public void CreateFromContour_AddsNormalizedHoleWithClockwiseWinding()
    {
        var hole = new Contour { X = [0.2, 0.8, 0.8, 0.2], Y = [0.2, 0.2, 0.8, 0.8] }; // CCW входной

        var region = PlanarRegion.CreateFromContour(OpenSquareContour(), holes: [hole]);

        var storedHole = Assert.Single(region.Holes);
        Assert.Equal(ContourType.Hole, storedHole.Type);
        Assert.True(PlanarRegionTopologyValidator.SignedArea(storedHole.X, storedHole.Y) < 0);
    }

    [Fact]
    public void CreateFromContour_ThrowsForSelfIntersectingOuterContour()
    {
        var bowtie = new Contour { X = [0, 1, 0, 1], Y = [0, 1, 1, 0] };
        Assert.Throws<InvalidOperationException>(() => PlanarRegion.CreateFromContour(bowtie));
    }

    [Fact]
    public void CreateFromContour_ComputesNonEmptyFingerprint()
    {
        var region = PlanarRegion.CreateFromContour(OpenSquareContour());
        Assert.False(string.IsNullOrEmpty(region.GeometryFingerprint));
    }

    [Fact]
    public void RecalcFingerprint_ChangesAfterAddingBoundarySegment()
    {
        var region = PlanarRegion.CreateFromContour(OpenSquareContour());
        var before = region.GeometryFingerprint;

        region.BoundarySegments.Add(new BoundarySegment { StartVertex = 0, EndVertex = 1, Role = BoundaryRole.Support });
        region.RecalcFingerprint();

        Assert.NotEqual(before, region.GeometryFingerprint);
    }
}
