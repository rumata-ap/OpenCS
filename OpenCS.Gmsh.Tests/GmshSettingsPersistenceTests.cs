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
    public void SaveGmshSettings_RoundTripsSeamTolerance()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opencs-gmsh-settings-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            db.SaveGmshSettings(new GmshSettings { SeamToleranceM = 5e-4 });

            Assert.Equal(5e-4, db.LoadGmshSettings().SeamToleranceM);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>Старая запись без поля — 0,1 мм; значение вне 0,001–1 мм (ручная правка базы) — зажимается.</summary>
    [Theory]
    [InlineData("{\"Algorithm\":6}", 1e-4)]
    [InlineData("{\"SeamToleranceM\":0.5}", 1e-3)]
    [InlineData("{\"SeamToleranceM\":0}", 1e-6)]
    public void LoadGmshSettings_SeamToleranceDefaultsAndClamps(string json, double expected)
    {
        var path = Path.Combine(Path.GetTempPath(), $"opencs-gmsh-settings-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            db.SaveGmshSettings(new GmshSettings());
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE settings SET value_json=$v WHERE key='gmsh'";
                command.Parameters.AddWithValue("$v", json);
                command.ExecuteNonQuery();
            }

            Assert.Equal(expected, db.LoadGmshSettings().SeamToleranceM, 12);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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
