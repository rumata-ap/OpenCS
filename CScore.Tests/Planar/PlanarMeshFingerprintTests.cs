using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public class PlanarMeshFingerprintTests
{
    [Fact]
    public void Compute_IsDeterministicAndChangesForEachMeshingInput()
    {
        var region = PlanarRegion.CreateFromContour(new Contour
        {
            X = [0, 2, 2, 0],
            Y = [0, 0, 1, 1]
        });
        var settings = new PlanarMeshSettings(0.25, 6, PlanarMeshElementMode.Mixed);
        var provenance = new PlanarMeshProvenance("4.15.2", "geo-v1");

        var baseline = PlanarMeshFingerprint.Compute(region, settings, provenance);

        Assert.Equal(baseline, PlanarMeshFingerprint.Compute(region, settings, provenance));
        Assert.NotEqual(baseline, PlanarMeshFingerprint.Compute(region,
            settings with { MaxElementSizeM = 0.2 }, provenance));
        Assert.NotEqual(baseline, PlanarMeshFingerprint.Compute(region,
            settings with { Algorithm = 5 }, provenance));
        Assert.NotEqual(baseline, PlanarMeshFingerprint.Compute(region,
            settings with { ElementMode = PlanarMeshElementMode.Triangles }, provenance));
        Assert.NotEqual(baseline, PlanarMeshFingerprint.Compute(region, settings,
            provenance with { GmshVersion = "4.16.0" }));
        Assert.NotEqual(baseline, PlanarMeshFingerprint.Compute(region, settings,
            provenance with { GeneratorVersion = "geo-v2" }));
    }
}
