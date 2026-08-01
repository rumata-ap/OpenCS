using OpenCS.Utilites;

namespace OpenCS.Gmsh.Tests;

public sealed class GmshSettingsPersistenceTests
{
    [Fact]
    public void SaveGmshSettings_RoundTripsExecutablePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opencs-gmsh-settings-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            db.SaveGmshSettings(new GmshSettings { ExecutablePath = @"C:\Tools\gmsh\gmsh.exe" });

            Assert.Equal(@"C:\Tools\gmsh\gmsh.exe", db.LoadGmshSettings().ExecutablePath);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SaveGmshSettings_RoundTripsMeshingDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opencs-gmsh-settings-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            db.SaveGmshSettings(new GmshSettings
            {
                Algorithm = 8,
                ElementMode = CScore.Planar.PlanarMeshElementMode.Quads,
                TimeoutSeconds = 90,
                KeepArtifacts = false,
                ArtifactsPath = @"C:\Temp\gmsh-artifacts"
            });

            var loaded = db.LoadGmshSettings();

            Assert.Equal(8, loaded.Algorithm);
            Assert.Equal(CScore.Planar.PlanarMeshElementMode.Quads, loaded.ElementMode);
            Assert.Equal(90, loaded.TimeoutSeconds);
            Assert.False(loaded.KeepArtifacts);
            Assert.Equal(@"C:\Temp\gmsh-artifacts", loaded.ArtifactsPath);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ResolveArtifactsPath_EmptyPath_ReturnsDefaultNextToExecutable()
    {
        var settings = new GmshSettings { ArtifactsPath = null };

        var resolved = settings.ResolveArtifactsPath();

        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "GmshArtifacts"), resolved);
    }
}
