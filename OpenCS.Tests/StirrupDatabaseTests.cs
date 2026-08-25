using CScore;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки SQLite-хранения замкнутых хомутов материальной области.</summary>
public sealed class StirrupDatabaseTests
{
    [Fact]
    public void SaveMaterialArea_RoundTripsStirrups()
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

            Assert.Equal(2, loaded.Stirrups.Count);
            Assert.Equal(2, loaded.Stirrups[0].Elements.Count);
            Assert.Equal(0.15, loaded.Stirrups[0].SpacingM, 12);
            Assert.Equal(17, loaded.Stirrups[0].MaterialId);
            Assert.Equal(0.0000503, loaded.Stirrups[0].Elements[0].BarAreaM2, 12);
            Assert.Equal(0.008, loaded.Stirrups[0].Elements[0].BarDiameterM, 12);
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
            area.Stirrups.RemoveAt(1);
            area.Stirrups[0].SpacingM = 0.10;
            db.SaveMaterialArea(area);

            using var loadedDb = new DatabaseService(path);
            loadedDb.LoadAll();
            var loaded = Assert.Single(loadedDb.MaterialAreas);
            var group = Assert.Single(loaded.Stirrups);
            Assert.Equal(0.10, group.SpacingM, 12);
            Assert.Equal(2, group.Elements.Count);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void LoadMaterialAreas_LeavesStirrupsEmptyForLegacyArea()
    {
        string path = TempPath();
        try
        {
            using var db = new DatabaseService(path);
            var area = AreaWithStirrups();
            area.Stirrups.Clear();
            db.SaveMaterialArea(area);

            using var loadedDb = new DatabaseService(path);
            loadedDb.LoadAll();

            Assert.Empty(Assert.Single(loadedDb.MaterialAreas).Stirrups);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void DeleteMaterialArea_DeletesStirrupRowsWhenForeignKeysAreOff()
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

    [Fact]
    public void SaveMaterialArea_RoundTripsOffsetAndSource()
    {
        string path = TempPath();
        try
        {
            var area = new MaterialArea { Num = 1, Tag = "хомуты", Category = AreaCategory.Stirrups, MaterialId = 17 };
            area.Stirrups =
            [
                new StirrupGroup
                {
                    MaterialId = 17, SpacingM = 0.15, OffsetM = 0.03,
                    Elements =
                    [
                        new StirrupElement
                        {
                            CenterlineContour = Contour.Polyline([0.0, 0.0], [-0.2, 0.2], "срез"),
                            BarAreaM2 = 0.0000503, BarDiameterM = 0.008,
                            Source = new StirrupElementSource
                            {
                                Kind = StirrupElementKind.Cut, AnchorAreaId = 3,
                                Direction = StirrupCutDirection.Vertical, Position = 0.0, OffsetM = 0.03
                            }
                        }
                    ]
                }
            ];

            using (var db = new DatabaseService(path)) db.SaveMaterialArea(area);

            using var loadedDb = new DatabaseService(path);
            loadedDb.LoadAll();
            var loaded = Assert.Single(loadedDb.MaterialAreas);
            var group = Assert.Single(loaded.Stirrups);

            Assert.Equal(0.03, group.OffsetM!.Value, 12);
            var element = Assert.Single(group.Elements);
            Assert.False(element.IsClosed);
            Assert.Equal(StirrupElementKind.Cut, element.Source!.Kind);
            Assert.Equal(3, element.Source.AnchorAreaId);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void LoadMaterialAreas_LegacyRowWithoutSourceJson_KeepsNullSource()
    {
        string path = TempPath();
        try
        {
            var area = AreaWithStirrups();
            using (var db = new DatabaseService(path)) db.SaveMaterialArea(area);

            using (var conn = new SqliteConnection($"Data Source={path}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE material_area_closed_stirrup_loops SET source_json = NULL";
                cmd.ExecuteNonQuery();
            }

            using var loadedDb = new DatabaseService(path);
            loadedDb.LoadAll();
            var loaded = Assert.Single(loadedDb.MaterialAreas);

            Assert.Null(loaded.Stirrups[0].Elements[0].Source);

            using var resaveDb = new DatabaseService(path);
            resaveDb.LoadAll();
            resaveDb.SaveMaterialArea(resaveDb.MaterialAreas[0]);

            using var againDb = new DatabaseService(path);
            againDb.LoadAll();
            Assert.Equal(2, againDb.MaterialAreas[0].Stirrups[0].Elements.Count);
        }
        finally { TryDelete(path); }
    }

    static MaterialArea AreaWithStirrups()
    {
        var area = new MaterialArea { Num = 1, Tag = "бетон", Category = AreaCategory.Region };
        area.Hull = Rectangle(-0.15, -0.25, 0.15, 0.25);
        area.SetWKT();
        area.Stirrups =
        [
            new StirrupGroup { MaterialId = 17, SpacingM = 0.15, Elements = [Loop(-0.12, -0.20, 0.12, 0.20), Loop(-0.05, -0.15, 0.05, 0.15)] },
            new StirrupGroup { MaterialId = 18, SpacingM = 0.20, Elements = [Loop(-0.10, -0.18, 0.10, 0.18)] }
        ];
        return area;
    }

    static StirrupElement Loop(double x0, double y0, double x1, double y1) => new()
    {
        CenterlineContour = Rectangle(x0, y0, x1, y1), BarAreaM2 = 0.0000503, BarDiameterM = 0.008
    };

    static Contour Rectangle(double x0, double y0, double x1, double y1) => new([x0, x1, x1, x0, x0], [y0, y0, y1, y1, y0], "loop");
    static long ScalarCount(SqliteConnection connection, string sql) { using var cmd = connection.CreateCommand(); cmd.CommandText = sql; return (long)cmd.ExecuteScalar()!; }
    static string TempPath() => Path.Combine(Path.GetTempPath(), $"opencs-stirrups-{Guid.NewGuid():N}.db");
    static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
