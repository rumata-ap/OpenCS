using CScore;
using CScore.Planar;
using OpenCS.Gmsh;

namespace OpenCS.Gmsh.Tests;

[CollectionDefinition("Gmsh user options isolation", DisableParallelization = true)]
public sealed class GmshUserOptionsIsolationCollection;

/// <summary>Сетка OpenCS не должна зависеть от личных настроек Gmsh на машине
/// (<c>%APPDATA%\gmsh-options</c>): иначе один и тот же регион даёт разную сетку на разных ПК,
/// а отпечаток снапшота этого не отражает.</summary>
[Collection("Gmsh user options isolation")]
public sealed class GmshUserOptionsIsolationTests
{
    [Fact]
    public async Task BuildAsync_IgnoresUserGmshOptionsFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "opencs-gmsh-user-options", Guid.NewGuid().ToString("N"));
        var fakeHome = Path.Combine(root, "home");
        Directory.CreateDirectory(fakeHome);
        // Настройка, которая тихо превращает треугольную сетку в смешанную.
        File.WriteAllText(Path.Combine(fakeHome, "gmsh-options"), "Mesh.RecombineAll = 1;\n");

        var oldAppData = Environment.GetEnvironmentVariable("APPDATA");
        var oldGmshHome = Environment.GetEnvironmentVariable("GMSH_HOME");
        try
        {
            Environment.SetEnvironmentVariable("APPDATA", fakeHome);
            Environment.SetEnvironmentVariable("GMSH_HOME", fakeHome);

            var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 4, 4, 0], Y = [0, 0, 2, 2] });
            var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions
            {
                ExecutablePath = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe",
                ArtifactRoot = Path.Combine(root, "artifacts")
            });

            var snapshot = await mesher.BuildAsync(new PlanarMeshingRequest(region,
                new PlanarMeshSettings(0.35, 6, PlanarMeshElementMode.Triangles)));

            Assert.True(snapshot.IsCalculable);
            Assert.NotEmpty(snapshot.Elements);
            Assert.All(snapshot.Elements, element => Assert.Equal(PlanarMeshElementKind.Triangle3, element.Kind));
        }
        finally
        {
            Environment.SetEnvironmentVariable("APPDATA", oldAppData);
            Environment.SetEnvironmentVariable("GMSH_HOME", oldGmshHome);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
