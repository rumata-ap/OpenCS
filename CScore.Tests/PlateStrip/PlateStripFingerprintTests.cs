using CScore.Planar;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class PlateStripFingerprintTests
{
    [Fact]
    public void Compute_IsDeterministic()
    {
        var region = Region();
        var start = Locus(2, 5, 0);
        var end = Locus(8, 5, 0);

        var a = PlateStripFingerprint.Compute(region, start, end, 2.0);
        var b = PlateStripFingerprint.Compute(region, start, end, 2.0);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Compute_ChangesWithRegionGeometry()
    {
        var start = Locus(2, 5, 0);
        var end = Locus(8, 5, 0);

        var a = PlateStripFingerprint.Compute(Region(), start, end, 2.0);
        var otherRegion = PlanarRegion.CreateFromContour(new Contour { X = [0, 20, 20, 0], Y = [0, 0, 20, 20] });
        var b = PlateStripFingerprint.Compute(otherRegion, start, end, 2.0);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Compute_ChangesWithStartLocus()
    {
        var region = Region();
        var end = Locus(8, 5, 0);

        var a = PlateStripFingerprint.Compute(region, Locus(2, 5, 0), end, 2.0);
        var b = PlateStripFingerprint.Compute(region, Locus(3, 5, 0), end, 2.0);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Compute_ChangesWithEndLocus()
    {
        var region = Region();
        var start = Locus(2, 5, 0);

        var a = PlateStripFingerprint.Compute(region, start, Locus(8, 5, 0), 2.0);
        var b = PlateStripFingerprint.Compute(region, start, Locus(9, 5, 0), 2.0);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Compute_ChangesWithWidth()
    {
        var region = Region();
        var start = Locus(2, 5, 0);
        var end = Locus(8, 5, 0);

        var a = PlateStripFingerprint.Compute(region, start, end, 2.0);
        var b = PlateStripFingerprint.Compute(region, start, end, 3.0);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Compute_ChangesWithStructuralMode()
    {
        var region = Region();
        var end = Locus(8, 5, 0);

        var a = PlateStripFingerprint.Compute(region, Locus(2, 5, 0, BeamJunctionMode.Support), end, 2.0);
        var b = PlateStripFingerprint.Compute(region, Locus(2, 5, 0, BeamJunctionMode.Tie), end, 2.0);

        Assert.NotEqual(a, b);
    }

    static PlanarRegion Region() =>
        PlanarRegion.CreateFromContour(new Contour { X = [0, 10, 10, 0], Y = [0, 0, 10, 10] });

    static SupportLocus Locus(double x, double y, double z, BeamJunctionMode mode = BeamJunctionMode.Support) =>
        new() { Frame = Frame3D.Identity with { Origin = new PlanarVector3(x, y, z) }, StructuralMode = mode };
}
