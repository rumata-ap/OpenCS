using CScore.Fire;
using CScore.Fire.Entities;
using CSfea.Thermal;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Тепловой расчёт огневой задачи: один на огневое сечение, устаревший — ошибка.
/// Регрессия 26.09.2026: после смены длительности пожара 90 → 120 мин задача молча
/// считала по старому полю, а сводка показывала «длительность 120» рядом с «временем 90».
/// </summary>
public sealed class FireHandlerGuardTests
{
    [Fact]
    public void NoThermalResult_IsNotComputedError()
    {
        WithDb((db, def, section) =>
        {
            var ex = Assert.Throws<FireThermalUnavailableException>(
                () => FireThermalReference.Resolve(db, def, section));
            Assert.Equal("FireThermal_NotComputed", ex.ErrorKey);
        });
    }

    [Fact]
    public void CurrentResult_IsResolved()
    {
        WithDb((db, def, section) =>
        {
            int id = Save(db, def, section);

            var r = FireThermalReference.Resolve(db, def, section);

            Assert.Equal(id, r.ResultId);
            Assert.NotNull(r.InputHash);
            Assert.Equal(90.0, r.Thermal.FireDurationMin, 6);
        });
    }

    [Fact]
    public void ChangedDuration_IsStaleError_UntilRerun()
    {
        WithDb((db, def, section) =>
        {
            Save(db, def, section);
            def.FireDurationMin = 120;

            var ex = Assert.Throws<FireThermalUnavailableException>(
                () => FireThermalReference.Resolve(db, def, section));
            Assert.Equal("FireThermal_Stale", ex.ErrorKey);
            Assert.Equal(Loc.S("FireStale_Fire"),
                FireThermalReference.StaleReason(db, def, section, db.GetFireThermalResultInfo(def.Id)!));

            int rerun = Save(db, def, section);
            var r = FireThermalReference.Resolve(db, def, section);
            Assert.Equal(rerun, r.ResultId);
            Assert.Equal(120.0, r.Thermal.FireDurationMin, 6);
        });
    }

    [Fact]
    public void ResultWithoutHash_IsStale()
    {
        WithDb((db, def, section) =>
        {
            var result = Result(def.FireDurationMin);
            db.SaveFireThermalResult(def.Id, result, "", "");

            var ex = Assert.Throws<FireThermalUnavailableException>(
                () => FireThermalReference.Resolve(db, def, section));
            Assert.Equal("FireThermal_Stale", ex.ErrorKey);
        });
    }

    [Fact]
    public void SnapshotTime_ResolvesNearestAndRejectsBeyondDuration()
    {
        var thermal = new FireThermalResult
        {
            MeshInfo = Result(90.0).MeshInfo,
            TimesMin = [0.0, 30.0, 60.0, 90.0],
            Snapshots = [[20.0], [300.0], [500.0], [700.0]],
            FireDurationMin = 90.0
        };

        Assert.Equal(-1, FireThermalReference.ResolveSnapshotIndex(thermal, null));
        Assert.Equal(2, FireThermalReference.ResolveSnapshotIndex(thermal, 60.0));
        Assert.Equal(1, FireThermalReference.ResolveSnapshotIndex(thermal, 40.0));
        Assert.Equal(3, FireThermalReference.ResolveSnapshotIndex(thermal, 100.0));
        var ex = Assert.Throws<FireThermalUnavailableException>(
            () => FireThermalReference.ResolveSnapshotIndex(thermal, 120.0));
        Assert.Equal("FireThermal_TimeBeyondDuration", ex.ErrorKey);

        // Задача, не прошедшая миграцию v62: старый индекс в диапазоне — используется.
        Assert.Equal(1, FireThermalReference.ResolveSnapshotIndex(thermal, null, legacyIndex: 1));
        Assert.Equal(-1, FireThermalReference.ResolveSnapshotIndex(thermal, null, legacyIndex: 9));
    }

    static void WithDb(Action<DatabaseService, FireSectionDef, CScore.CrossSection> body)
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fire-guard-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            db.LoadAll();
            var section = ReportFixtures.BuildBeam();
            var def = new FireSectionDef { Id = 5, SectionId = section.Id, FireDurationMin = 90, AggregateType = "silicate" };
            body(db, def, section);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    static int Save(DatabaseService db, FireSectionDef def, CScore.CrossSection section)
    {
        var input = FireThermalInputSnapshot.Build(def, section, "silicate");
        return db.SaveFireThermalResult(def.Id, Result(def.FireDurationMin), input.Json, input.Hash);
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
}
