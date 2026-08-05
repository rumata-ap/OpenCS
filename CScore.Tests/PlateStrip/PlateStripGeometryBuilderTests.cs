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

    [Fact]
    public void PlateStripBeamAnalogy_StoresAllFields()
    {
        var start = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(2, 5, 0) } };
        var end = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(8, 5, 0) } };
        var geometry = new PlateStripGeometry { LengthM = 6 };

        var analogy = new PlateStripBeamAnalogy
        {
            Id = "strip-1",
            SourceRegionId = 77,
            StartSupportLocus = start,
            EndSupportLocus = end,
            StripFrame = Frame3D.Identity with { Origin = new PlanarVector3(2, 5, 0) },
            Geometry = geometry,
            ExplicitWidthM = 2,
            Fingerprint = "abc"
        };

        Assert.Equal("strip-1", analogy.Id);
        Assert.Equal(77, analogy.SourceRegionId);
        Assert.Equal(start, analogy.StartSupportLocus);
        Assert.Equal(6, analogy.Geometry.LengthM);
        Assert.Equal(2, analogy.ExplicitWidthM);
        Assert.Equal("abc", analogy.Fingerprint);
    }

    [Fact]
    public void Build_HappyPath_ProducesRectangleStripInsideHull()
    {
        var region = Region();
        var start = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(2, 5, 0) } };
        var end = new SupportLocus { Frame = Frame3D.Identity with { Origin = new PlanarVector3(8, 5, 0) } };

        var result = PlateStripGeometryBuilder.Build("strip-1", region, start, end, 2.0);

        Assert.True(result.IsCalculable, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message)));
        var analogy = result.Analogy!;
        Assert.Equal(6.0, analogy.Geometry.LengthM, 9);
        Assert.Equal(new[] { new PlanarPoint2D(2, 5), new PlanarPoint2D(8, 5) }, analogy.Geometry.CenterLine);
        Assert.Equal(new PlanarVector3(2, 5, 0), analogy.StripFrame.Origin);
        Assert.Equal(new PlanarVector3(1, 0, 0), analogy.StripFrame.LocalX);
        Assert.Equal(new PlanarVector3(0, 1, 0), analogy.StripFrame.LocalY);
        Assert.Equal(new PlanarVector3(0, 0, 1), analogy.StripFrame.LocalZ);

        var corners = new[] { new PlanarPoint2D(2, 4), new PlanarPoint2D(8, 4), new PlanarPoint2D(8, 6), new PlanarPoint2D(2, 6) };
        Assert.Equal(4, analogy.Geometry.Polygon.Count);
        foreach (var corner in corners)
            Assert.Contains(analogy.Geometry.Polygon, p => Close(p, corner));

        Assert.Equal(2, analogy.Geometry.LeftBoundary.Count);
        Assert.All(analogy.Geometry.LeftBoundary, p => Assert.Equal(6.0, p.V, 9));
        Assert.Equal(2, analogy.Geometry.RightBoundary.Count);
        Assert.All(analogy.Geometry.RightBoundary, p => Assert.Equal(4.0, p.V, 9));

        Assert.Equal(PlateStripFingerprint.Compute(region, start, end, 2.0), analogy.Fingerprint);
        Assert.Empty(result.Diagnostics);
    }

    static bool Close(PlanarPoint2D a, PlanarPoint2D b, double tol = 1e-9) =>
        Math.Abs(a.U - b.U) < tol && Math.Abs(a.V - b.V) < tol;

    static PlanarRegion Region(IEnumerable<Contour>? holes = null) =>
        PlanarRegion.CreateFromContour(new Contour { X = [0, 10, 10, 0], Y = [0, 0, 10, 10] }, holes);
}
