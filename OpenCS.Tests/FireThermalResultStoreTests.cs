using CScore.Fire;
using CSfea.Thermal;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

public sealed class FireThermalResultStoreTests
{
    [Fact]
    public void SaveAndList_ReturnsNewestFirstWithMetadata()
    {
        string path = TempDbPath();
        try
        {
            using var db = new DatabaseService(path);
            db.LoadAll();

            int first = db.SaveFireThermalResult(1, Result(durationMin: 30), "{\"schema\":1}", "aaaaaaaaaaaaaaaa");
            int second = db.SaveFireThermalResult(1, Result(durationMin: 60), "{\"schema\":1}", "bbbbbbbbbbbbbbbb");

            var list = db.ListFireThermalResults(1);

            Assert.Equal(2, list.Count);
            Assert.Equal(second, list[0].Id);
            Assert.Equal(first, list[1].Id);
            Assert.Equal("bbbbbbbbbbbbbbbb", list[0].InputHash);
            Assert.Equal(2, list[0].SnapshotCount);
            Assert.Equal(60.0, list[0].DurationMin!.Value, 6);
        }
        finally { Delete(path); }
    }

    [Fact]
    public void GetOwner_ReturnsFireSectionId_AndNullForMissing()
    {
        string path = TempDbPath();
        try
        {
            using var db = new DatabaseService(path);
            db.LoadAll();
            int id = db.SaveFireThermalResult(7, Result(60), "{}", "aaaaaaaaaaaaaaaa");

            Assert.Equal(7, db.GetFireThermalResultOwner(id));
            Assert.Null(db.GetFireThermalResultOwner(id + 1000));
        }
        finally { Delete(path); }
    }

    [Fact]
    public void Delete_RemovesOnlyTargetRow()
    {
        string path = TempDbPath();
        try
        {
            using var db = new DatabaseService(path);
            db.LoadAll();
            int a = db.SaveFireThermalResult(1, Result(30), "{}", "1111111111111111");
            int b = db.SaveFireThermalResult(1, Result(60), "{}", "2222222222222222");

            db.DeleteFireThermalResult(a);
            var list = db.ListFireThermalResults(1);

            Assert.Single(list);
            Assert.Equal(b, list[0].Id);
        }
        finally { Delete(path); }
    }

    [Fact]
    public void LegacyRowWithoutMetadata_ListsWithNulls()
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
                var info = Assert.Single(db.ListFireThermalResults(1));
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

    static void ClearMetadata(string path)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE fire_thermal_results SET input_json=NULL, input_hash=NULL, snapshot_count=NULL, duration_min=NULL";
        cmd.ExecuteNonQuery();
    }
}
