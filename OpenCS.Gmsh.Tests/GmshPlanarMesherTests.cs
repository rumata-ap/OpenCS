using CScore;
using CScore.Planar;
using OpenCS.Gmsh;

namespace OpenCS.Gmsh.Tests;

public sealed class GmshPlanarMesherTests
{
    [Fact]
    public async Task BuildAsync_MeshesRegionWithHole()
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 2, 2, 0], Y = [0, 0, 2, 2] },
            [new Contour { X = [0.75, 1.25, 1.25, 0.75], Y = [0.75, 0.75, 1.25, 1.25] }]);
        var root = Path.Combine(Path.GetTempPath(), "opencs-gmsh-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
            {
                ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe",
                ArtifactRoot = root
            });

            var snapshot = await mesher.BuildAsync(new PlanarMeshingRequest(region,
                new PlanarMeshSettings(0.35, 6, PlanarMeshElementMode.Mixed)));

            Assert.True(snapshot.IsCalculable);
            Assert.NotEmpty(snapshot.Elements);
            Assert.All(snapshot.Nodes, node => Assert.Equal(0, node.Z, 10));
            Assert.DoesNotContain(snapshot.Nodes, node => node.U > 0.75 && node.U < 1.25 && node.V > 0.75 && node.V < 1.25);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
