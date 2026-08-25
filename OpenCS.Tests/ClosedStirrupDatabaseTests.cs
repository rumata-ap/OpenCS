using CScore;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки SQLite-хранения замкнутых хомутов материальной области.</summary>
public sealed class ClosedStirrupDatabaseTests
{
    [Fact]
    public void SaveMaterialArea_RoundTripsClosedStirrups()
    {
        string path = TempPath();
        try
        {
            using var source = new DatabaseService(path);
            var area = AreaWithStirrups();
            source.SaveMaterialArea(area);

            using var loadedDb = new DatabaseService(path);
            loadedDb.LoadAll();
            var loaded = Assert.Single(loadedDb.MaterialAreas);

            Assert.Equal(2, loaded.ClosedStirrups.Count);
            Assert.Equal(2, loaded.ClosedStirrups[0].Loops.Count);
            Assert.Equal(0.15, loaded.ClosedStirrups[0].SpacingM, 12);
            Assert.Equal(17, loaded.ClosedStirrups[0].MaterialId);
            Assert.Equal(0.0000503, loaded.ClosedStirrups[0].Loops[0].BarAreaM2, 12);
            Assert.Equal(0.008, loaded.ClosedStirrups[0].Loops[0].BarDiameterM, 12);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void SaveMaterialArea_TwiceRecreatesClosedStirrupRowsWithoutDuplicatingThem()
    {
        string path = TempPath();
        try
        {
            using var db = new DatabaseService(path);
            var area = AreaWithStirrups();
            db.SaveMaterialArea(area);
            area.ClosedStirrups.RemoveAt(1);
            area.ClosedStirrups[0].SpacingM = 0.10;
            db.SaveMaterialArea(area);

            using var loadedDb = new DatabaseService(path);
            loadedDb.LoadAll();
            var loaded = Assert.Single(loadedDb.MaterialAreas);
            var group = Assert.Single(loaded.ClosedStirrups);
            Assert.Equal(0.10, group.SpacingM, 12);
            Assert.Equal(2, group.Loops.Count);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void LoadMaterialAreas_LeavesClosedStirrupsEmptyForLegacyArea()
    {
        string path = TempPath();
        try
        {
            using var db = new DatabaseService(path);
            var area = AreaWithStirrups();
            area.ClosedStirrups.Clear();
            db.SaveMaterialArea(area);

            using var loadedDb = new DatabaseService(path);
            loadedDb.LoadAll();

            Assert.Empty(Assert.Single(loadedDb.MaterialAreas).ClosedStirrups);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void DeleteMaterialArea_DeletesClosedStirrupRowsWhenForeignKeysAreOff()
    {
        string path = TempPath();
        try
        {
            using var db = new DatabaseService(path);
            var area = AreaWithStirrups();
            db.SaveMaterialArea(area);
            db.DeleteMaterialArea(area);

            using var connection = new SqliteConnection($"Data Source={path}");
            connection.Open();
            Assert.Equal(0L, ScalarCount(connection, "SELECT COUNT(*) FROM material_area_closed_stirrup_groups"));
            Assert.Equal(0L, ScalarCount(connection, "SELECT COUNT(*) FROM material_area_closed_stirrup_loops"));
        }
        finally { TryDelete(path); }
    }

    static MaterialArea AreaWithStirrups()
    {
        var area = new MaterialArea { Num = 1, Tag = "бетон", Category = AreaCategory.Region };
        area.Hull = Rectangle(-0.15, -0.25, 0.15, 0.25);
        area.SetWKT();
        area.ClosedStirrups =
        [
            new ClosedStirrupGroup { MaterialId = 17, SpacingM = 0.15, Loops = [Loop(-0.12, -0.20, 0.12, 0.20), Loop(-0.05, -0.15, 0.05, 0.15)] },
            new ClosedStirrupGroup { MaterialId = 18, SpacingM = 0.20, Loops = [Loop(-0.10, -0.18, 0.10, 0.18)] }
        ];
        return area;
    }

    static ClosedStirrupLoop Loop(double x0, double y0, double x1, double y1) => new()
    {
        CenterlineContour = Rectangle(x0, y0, x1, y1), BarAreaM2 = 0.0000503, BarDiameterM = 0.008
    };

    static Contour Rectangle(double x0, double y0, double x1, double y1) => new([x0, x1, x1, x0, x0], [y0, y0, y1, y1, y0], "loop");
    static long ScalarCount(SqliteConnection connection, string sql) { using var cmd = connection.CreateCommand(); cmd.CommandText = sql; return (long)cmd.ExecuteScalar()!; }
    static string TempPath() => Path.Combine(Path.GetTempPath(), $"opencs-stirrups-{Guid.NewGuid():N}.db");
    static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
