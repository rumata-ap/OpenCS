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
}
