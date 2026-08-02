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

        var constrainedRegion = PlanarRegion.CreateFromContour(new Contour
        {
            X = [0, 2, 2, 0],
            Y = [0, 0, 1, 1]
        });
        constrainedRegion.ConstraintObjects.Add(PlanarConstraintObject.Point(
            "point-1", new(0.5, 0.5),
            new PlanarStructuralFacet(PlanarStructuralKind.None),
            new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint)));
        constrainedRegion.RecalcFingerprint();

        Assert.NotEqual(baseline, PlanarMeshFingerprint.Compute(constrainedRegion, settings, provenance));
    }

    [Fact]
    public void Compute_ChangesWhenFEMSourceFingerprintChanges()
    {
        var region = PlanarRegion.CreateFromContour(new Contour
        {
            X = [0, 2, 2, 0],
            Y = [0, 0, 1, 1]
        });
        var settings = new PlanarMeshSettings(0.25, 6, PlanarMeshElementMode.Mixed);
        var provenance = new PlanarMeshProvenance("4.15.2", "geo-v1");

        var first = PlanarMeshFingerprint.Compute(region, settings, provenance, "source-a");
        var second = PlanarMeshFingerprint.Compute(region, settings, provenance, "source-b");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void MeshingRequest_UsesExplicitConstraintsWithoutMutatingRegion()
    {
        var region = PlanarRegion.CreateFromContour(new Contour
        {
            X = [0, 2, 2, 0],
            Y = [0, 0, 1, 1]
        });
        var constraint = PlanarConstraintObject.Point(
            "derived:point",
            new(1, 0.5),
            new PlanarStructuralFacet(PlanarStructuralKind.EmbeddedMember),
            new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint));

        var request = new PlanarMeshingRequest(
            region,
            new PlanarMeshSettings(0.25, 6, PlanarMeshElementMode.Mixed),
            [constraint],
            "source-a");

        Assert.Same(constraint, Assert.Single(request.EffectiveConstraintObjects));
        Assert.Equal("source-a", request.ConstraintSourceFingerprint);
        Assert.Empty(region.ConstraintObjects);
    }
}
