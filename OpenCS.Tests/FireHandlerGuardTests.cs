using CScore.Fire;
using CSfea.Thermal;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

public sealed class FireHandlerGuardTests
{
    [Fact]
    public void ExplicitId_OfOwnSection_IsAccepted()
    {
        string path = TempDbPath();
        try
        {
            using var db = new DatabaseService(path);
            db.LoadAll();
            int id = db.SaveFireThermalResult(5, Result(), "{}", "aaaaaaaaaaaaaaaa");

            var r = FireThermalReference.Resolve(db, fireSectionId: 5, thermalResultId: id);

            Assert.Equal(id, r.ResultId);
            Assert.False(r.IsLegacyFallback);
            Assert.Null(r.ErrorKey);
        }
        finally { Delete(path); }
    }

    [Fact]
    public void ExplicitId_OfForeignSection_IsRejected()
    {
        string path = TempDbPath();
        try
        {
            using var db = new DatabaseService(path);
            db.LoadAll();
            int id = db.SaveFireThermalResult(5, Result(), "{}", "aaaaaaaaaaaaaaaa");

            var r = FireThermalReference.Resolve(db, fireSectionId: 6, thermalResultId: id);

            Assert.Equal("FireThermalResultOwnerMismatch", r.ErrorKey);
        }
        finally { Delete(path); }
    }

    [Fact]
    public void MissingId_IsRejected()
    {
        string path = TempDbPath();
        try
        {
            using var db = new DatabaseService(path);
            db.LoadAll();

            var r = FireThermalReference.Resolve(db, fireSectionId: 5, thermalResultId: 999);

            Assert.Equal("FireThermalResultNotFound", r.ErrorKey);
        }
        finally { Delete(path); }
    }

    [Fact]
    public void ZeroId_FallsBackToLatestWithLegacyFlag()
    {
        string path = TempDbPath();
        try
        {
            using var db = new DatabaseService(path);
            db.LoadAll();
            db.SaveFireThermalResult(5, Result(), "{}", "aaaaaaaaaaaaaaaa");
            int latest = db.SaveFireThermalResult(5, Result(), "{}", "bbbbbbbbbbbbbbbb");

            var r = FireThermalReference.Resolve(db, fireSectionId: 5, thermalResultId: 0);

            Assert.Equal(latest, r.ResultId);
            Assert.True(r.IsLegacyFallback);
            Assert.Null(r.ErrorKey);
        }
        finally { Delete(path); }
    }

    [Fact]
    public void ZeroId_WithEmptyHistory_IsRejected()
    {
        string path = TempDbPath();
        try
        {
            using var db = new DatabaseService(path);
            db.LoadAll();

            var r = FireThermalReference.Resolve(db, fireSectionId: 5, thermalResultId: 0);

            Assert.Equal("FireThermalResultNotFound", r.ErrorKey);
        }
        finally { Delete(path); }
    }

    static FireThermalResult Result()
    {
        var mesh = new HeatMesh(x: [0.0, 1.0, 0.0], y: [0.0, 0.0, 1.0], elements: [[0, 1, 2]]);
        return new FireThermalResult
        {
            MeshInfo = new FireMeshBuildResult { Mesh = mesh, BoundaryEdges = [], Rebars = [] },
            TimesMin = [0.0, 60.0],
            Snapshots = [[20.0, 20.0, 20.0], [500.0, 500.0, 500.0]],
            AggregateType = "silicate",
            FireDurationMin = 60.0
        };
    }

    static string TempDbPath()
        => Path.Combine(Path.GetTempPath(), $"opencs-fire-guard-{Guid.NewGuid():N}.db");

    static void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
