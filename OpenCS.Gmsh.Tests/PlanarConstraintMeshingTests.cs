using CScore;
using CScore.Planar;
using Xunit;

namespace OpenCS.Gmsh.Tests;

public sealed class PlanarConstraintMeshingTests
{
    [Fact]
    public async Task BuildAsync_MapsPointCurveAndConformingRegionOnMsh41()
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 5, 5, 0], Y = [0, 0, 5, 5] },
            [new Contour { X = [2, 3, 3, 2], Y = [2, 2, 3, 3] }]);
        region.ConstraintObjects =
        [
            PlanarConstraintObject.Point("point-1", new(0.5, 0.5),
                new PlanarStructuralFacet(PlanarStructuralKind.PointMpc, new PlanarMasterReference("test", "master")),
                new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint)),
            PlanarConstraintObject.Curve("curve-1", [new(0.5, 1), new(1.5, 1)],
                new PlanarStructuralFacet(PlanarStructuralKind.Tie),
                new PlanarMeshFacet(PlanarMeshKind.EmbeddedCurve)),
            PlanarConstraintObject.Region("region-1", [new(3.5, 0.5), new(4.5, 0.5), new(4.5, 1.5), new(3.5, 1.5)],
                new PlanarStructuralFacet(PlanarStructuralKind.RigidBody, new PlanarMasterReference("test", "rigid")),
                new PlanarMeshFacet(PlanarMeshKind.ConformingPartition))
        ];
        var root = Path.Combine(Path.GetTempPath(), "opencs-gmsh-constraints", Guid.NewGuid().ToString("N"));

        try
        {
            var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
            {
                ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe",
                ArtifactRoot = root
            });

            var snapshot = await mesher.BuildAsync(new PlanarMeshingRequest(region,
                new PlanarMeshSettings(0.4, 6, PlanarMeshElementMode.Mixed)));

            Assert.True(snapshot.IsCalculable, string.Join(Environment.NewLine, snapshot.Diagnostics));
            Assert.Equal("msh41", snapshot.MeshFormatVersion);
            Assert.Equal(3, snapshot.ConstraintMappings.Count);
            Assert.Single(snapshot.ConstraintMappings, mapping => mapping.ConstraintObjectId == "point-1");
            Assert.NotEmpty(Assert.Single(snapshot.ConstraintMappings, mapping => mapping.ConstraintObjectId == "curve-1").OrderedCurveEdges);
            Assert.NotEmpty(Assert.Single(snapshot.ConstraintMappings, mapping => mapping.ConstraintObjectId == "region-1").RegionElementIndices);
            Assert.NotEmpty(snapshot.EntityProvenance);
            Assert.All(snapshot.Nodes, node => Assert.Equal(0, node.Z, 10));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
