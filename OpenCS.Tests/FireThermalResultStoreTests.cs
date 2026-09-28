using CScore.Fire;
using CSfea.Thermal;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

public sealed class FireThermalResultStoreTests
{
    [Fact]
    public void Save_ReplacesPreviousResult_WithNewId()
    {
        string path = TempDbPath();
        try
        {
            using var db = new DatabaseService(path);
            db.LoadAll();

            int first = db.SaveFireThermalResult(1, Result(durationMin: 30), "{\"schema\":1}", "aaaaaaaaaaaaaaaa");
            int other = db.SaveFireThermalResult(2, Result(durationMin: 45), "{}", "cccccccccccccccc");
            int second = db.SaveFireThermalResult(1, Result(durationMin: 60), "{\"schema\":1}", "bbbbbbbbbbbbbbbb");

            Assert.NotEqual(first, second);
            Assert.False(db.FireThermalResultExists(first));
            Assert.True(db.FireThermalResultExists(second));
            Assert.True(db.FireThermalResultExists(other));

            var info = db.GetFireThermalResultInfo(1);
            Assert.NotNull(info);
            Assert.Equal(second, info!.Id);
            Assert.Equal("bbbbbbbbbbbbbbbb", info.InputHash);
            Assert.Equal(2, info.SnapshotCount);
            Assert.Equal(60.0, info.DurationMin!.Value, 6);
            Assert.Equal(1, CountRows(path, 1));
        }
        finally { Delete(path); }
    }

    [Fact]
    public void DeleteForSection_RemovesOnlyThatSection()
    {
        string path = TempDbPath();
        try
        {
            using var db = new DatabaseService(path);
            db.LoadAll();
            db.SaveFireThermalResult(1, Result(30), "{}", "1111111111111111");
            int b = db.SaveFireThermalResult(2, Result(60), "{}", "2222222222222222");

            db.DeleteFireThermalResultForSection(1);

            Assert.Null(db.GetFireThermalResultInfo(1));
            Assert.Equal(b, db.GetFireThermalResultInfo(2)!.Id);
        }
        finally { Delete(path); }
    }

    [Fact]
    public void LegacyRowWithoutMetadata_ReadsWithNulls()
    {
        string path = TempDbPath();
        try
        {
            using (var db = new DatabaseService(path))
            {
                db.LoadAll();
                db.SaveFireThermalResult(1, Result(60), "{}", "3333333333333333");
            }

            ClearMetadata(path);

            using (var db = new DatabaseService(path))
            {
                db.LoadAll();
                var info = db.GetFireThermalResultInfo(1)!;
                Assert.Null(info.InputHash);
                Assert.Null(info.SnapshotCount);
                Assert.Null(info.DurationMin);
            }
        }
        finally { Delete(path); }
    }

    [Fact]
    public void InputJson_RoundTrips()
    {
        string path = TempDbPath();
        try
        {
            using var db = new DatabaseService(path);
            db.LoadAll();

            const string json = "{\"schema\":1,\"mesh\":{\"step_m\":\"0.02\"}}";
            int id = db.SaveFireThermalResult(1, Result(60), json, "4444444444444444");

            Assert.Equal(json, db.GetFireThermalResultInputJson(id));
        }
        finally { Delete(path); }
    }

    static FireThermalResult Result(double durationMin)
    {
        var mesh = new HeatMesh(x: [0.0, 1.0, 0.0], y: [0.0, 0.0, 1.0], elements: [[0, 1, 2]]);
        return new FireThermalResult
        {
            MeshInfo = new FireMeshBuildResult { Mesh = mesh, BoundaryEdges = [], Rebars = [] },
            TimesMin = [0.0, durationMin],
            Snapshots = [[20.0, 20.0, 20.0], [500.0, 500.0, 500.0]],
            AggregateType = "silicate",
            FireDurationMin = durationMin
        };
    }

    static string TempDbPath()
        => Path.Combine(Path.GetTempPath(), $"opencs-fire-store-{Guid.NewGuid():N}.db");

    static void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    static long CountRows(string path, int fireSectionId)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM fire_thermal_results WHERE fire_section_id=@s";
        cmd.Parameters.AddWithValue("@s", fireSectionId);
        return (long)cmd.ExecuteScalar()!;
    }

    static void ClearMetadata(string path)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE fire_thermal_results SET input_json=NULL, input_hash=NULL, snapshot_count=NULL, duration_min=NULL";
        cmd.ExecuteNonQuery();
    }
}
