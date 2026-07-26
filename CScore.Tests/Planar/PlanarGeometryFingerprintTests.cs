using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public class PlanarGeometryFingerprintTests
{
    static Contour Hull(double[] x, double[] y) => new(
        [.. x, x[0]], [.. y, y[0]], "hull") { Type = ContourType.Hull };

    [Fact]
    public void Compute_IsDeterministicForIdenticalInput()
    {
        var contours = new List<Contour> { Hull([0, 1, 1, 0], [0, 0, 1, 1]) };
        var f1 = PlanarGeometryFingerprint.Compute(contours, Frame3D.Identity, []);
        var f2 = PlanarGeometryFingerprint.Compute(contours, Frame3D.Identity, []);
        Assert.Equal(f1, f2);
    }

    [Fact]
    public void Compute_ChangesWhenGeometryChanges()
    {
        var a = new List<Contour> { Hull([0, 1, 1, 0], [0, 0, 1, 1]) };
        var b = new List<Contour> { Hull([0, 2, 2, 0], [0, 0, 1, 1]) };

        var fa = PlanarGeometryFingerprint.Compute(a, Frame3D.Identity, []);
        var fb = PlanarGeometryFingerprint.Compute(b, Frame3D.Identity, []);

        Assert.NotEqual(fa, fb);
    }

    [Fact]
    public void Compute_ChangesWhenFrameChanges()
    {
        var contours = new List<Contour> { Hull([0, 1, 1, 0], [0, 0, 1, 1]) };
        var otherFrame = new Frame3D(
            new PlanarVector3(0, 0, 5),
            new PlanarVector3(1, 0, 0),
            new PlanarVector3(0, 1, 0),
            new PlanarVector3(0, 0, 1));

        var f1 = PlanarGeometryFingerprint.Compute(contours, Frame3D.Identity, []);
        var f2 = PlanarGeometryFingerprint.Compute(contours, otherFrame, []);

        Assert.NotEqual(f1, f2);
    }

    [Fact]
    public void Compute_ChangesWhenBoundarySegmentsChange()
    {
        var contours = new List<Contour> { Hull([0, 1, 1, 0], [0, 0, 1, 1]) };
        var segments = new List<BoundarySegment>
        {
            new() { StartVertex = 0, EndVertex = 1, Role = BoundaryRole.Support }
        };

        var f1 = PlanarGeometryFingerprint.Compute(contours, Frame3D.Identity, []);
        var f2 = PlanarGeometryFingerprint.Compute(contours, Frame3D.Identity, segments);

        Assert.NotEqual(f1, f2);
    }

    [Fact]
    public void Compute_ReturnsLowercaseHex64Characters()
    {
        var contours = new List<Contour> { Hull([0, 1, 1, 0], [0, 0, 1, 1]) };
        var fp = PlanarGeometryFingerprint.Compute(contours, Frame3D.Identity, []);

        Assert.Equal(64, fp.Length);
        Assert.Equal(fp.ToLowerInvariant(), fp);
    }
}
