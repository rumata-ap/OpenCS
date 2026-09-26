using CScore.Fire;
using CScore.Fire.Entities;
using CSfea.Thermal;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Актуальность теплового расчёта огневой задачи. Регрессия 26.09.2026: после смены
/// длительности пожара 90 → 120 мин задача молча считала по старому полю, а сводка
/// показывала «длительность 120» рядом с «временем 90».
/// </summary>
public sealed class FireThermalFreshnessTests
{
    [Fact]
    public void ChangedDurationAndNewerResultAreReported()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fire-fresh-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            db.LoadAll();
            var section = ReportFixtures.BuildBeam();
            var def = new FireSectionDef { Id = 5, SectionId = section.Id, FireDurationMin = 90, AggregateType = "silicate" };

            int first = Save(db, def, section);
            var fresh = FireThermalFreshness.Check(db, def, section, first);
            Assert.False(fresh.HasWarning);
            Assert.Null(fresh.WarningText);

            def.FireDurationMin = 120;
            var stale = FireThermalFreshness.Check(db, def, section, first);
            Assert.Null(stale.NewerResultId);
            Assert.Equal(Loc.S("FireStale_Fire"), stale.StaleReason);
            Assert.Equal(string.Format(Loc.S("FireRCheck_ThermalStale"), first, stale.StaleReason),
                stale.WarningText);

            int second = Save(db, def, section);
            var old = FireThermalFreshness.Check(db, def, section, first);
            Assert.Equal(second, old.NewerResultId);
            Assert.NotNull(old.StaleReason);

            Assert.False(FireThermalFreshness.Check(db, def, section, second).HasWarning);
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
        var mesh = new HeatMesh(x: [0.0, 1.0, 0.0], y: [0.0, 0.0, 1.0], elements: [[0, 1, 2]]);
        var result = new FireThermalResult
        {
            MeshInfo = new FireMeshBuildResult { Mesh = mesh, BoundaryEdges = [], Rebars = [] },
            TimesMin = [0.0, def.FireDurationMin],
            Snapshots = [[20.0, 20.0, 20.0], [500.0, 500.0, 500.0]],
            AggregateType = "silicate",
            FireDurationMin = def.FireDurationMin
        };
        return db.SaveFireThermalResult(def.Id, result, input.Json, input.Hash);
    }
}
