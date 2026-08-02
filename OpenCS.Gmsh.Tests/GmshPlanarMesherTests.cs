using CScore;
using CScore.Fem;
using CScore.Planar;
using OpenCS.Gmsh;

namespace OpenCS.Gmsh.Tests;

public sealed class GmshPlanarMesherTests
{
    [Fact]
    public async Task BuildAsync_MeshesNonConvexRegionWithHoleAndMapsBoundaries()
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 4, 4, 1, 1, 0], Y = [0, 0, 1, 1, 4, 4] },
            [new Contour { X = [0.25, 0.75, 0.75, 0.25], Y = [2, 2, 3, 3] }]);
        var root = Path.Combine(Path.GetTempPath(), "opencs-gmsh-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
            {
                ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe",
                ArtifactRoot = root
            });

            var snapshot = await mesher.BuildAsync(new PlanarMeshingRequest(region,
                new PlanarMeshSettings(0.45, 6, PlanarMeshElementMode.Mixed)));

            Assert.True(snapshot.IsCalculable);
            Assert.NotEmpty(snapshot.Elements);
            Assert.Equal(10, snapshot.BoundaryMappings.Count);
            Assert.All(snapshot.BoundaryMappings, mapping => Assert.True(mapping.NodeIndices.Count >= 2));
            Assert.All(snapshot.Nodes, node => Assert.Equal(0, node.Z, 10));
            Assert.DoesNotContain(snapshot.Elements, element =>
            {
                var centerU = element.NodeIndices.Average(index => snapshot.Nodes[index].U);
                var centerV = element.NodeIndices.Average(index => snapshot.Nodes[index].V);
                return centerU > 0.25 && centerU < 0.75 && centerV > 2 && centerV < 3;
            });
            Assert.Equal("4.15.2", snapshot.Provenance?.GmshVersion);
            var artifactDirectory = Assert.IsType<string>(snapshot.Provenance?.ArtifactDirectory);
            Assert.True(File.Exists(Path.Combine(artifactDirectory, "model.geo")));
            Assert.True(File.Exists(Path.Combine(artifactDirectory, "model.msh")));
            Assert.True(File.Exists(Path.Combine(artifactDirectory, "manifest.json")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildAsync_DoesNotLaunchGmshWhenPreflightHasBlockingDiagnostic()
    {
        var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 1, 1, 0], Y = [0, 0, 1, 1] });
        var root = Path.Combine(Path.GetTempPath(), "opencs-gmsh-preflight", Guid.NewGuid().ToString("N"));

        try
        {
            var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
            {
                ExecutablePath = Path.Combine(root, "missing-gmsh.exe"),
                ArtifactRoot = root
            });
            var snapshot = await mesher.BuildAsync(new PlanarMeshingRequest(
                region,
                new PlanarMeshSettings(0.25, 6, PlanarMeshElementMode.Mixed),
                [],
                "source",
                [new FemValidationDiagnostic("preflight_blocked", "blocked")]));

            Assert.False(snapshot.IsCalculable);
            Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Code == "preflight_blocked");
            Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Code == "gmsh_executable_not_found");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
