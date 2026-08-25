using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Миграция схемы 54 → 55: колонки offset_m и source_json.</summary>
public sealed class StirrupMigrationTests
{
    [Fact]
    public void OpeningV54Database_AddsNewStirrupColumns()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs_mig_{Guid.NewGuid():N}.db");
        try
        {
            using (var conn = new SqliteConnection($"Data Source={path}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE settings (key TEXT PRIMARY KEY, value_json TEXT);
                    INSERT INTO settings(key, value_json) VALUES ('schema_version','54');
                    CREATE TABLE material_area_closed_stirrup_groups (
                        id INTEGER PRIMARY KEY AUTOINCREMENT, area_id INTEGER NOT NULL,
                        material_id INTEGER NOT NULL, spacing_m REAL NOT NULL);
                    CREATE TABLE material_area_closed_stirrup_loops (
                        id INTEGER PRIMARY KEY AUTOINCREMENT, group_id INTEGER NOT NULL,
                        centerline_wkt TEXT NOT NULL, bar_area_m2 REAL NOT NULL, bar_diameter_m REAL NOT NULL);
                    """;
                cmd.ExecuteNonQuery();
            }

            using (var db = new DatabaseService(path)) { }

            using (var check = new SqliteConnection($"Data Source={path}"))
            {
                check.Open();
                Assert.True(HasColumn(check, "material_area_closed_stirrup_groups", "offset_m"));
                Assert.True(HasColumn(check, "material_area_closed_stirrup_loops", "source_json"));
            }
        }
        finally { TryDelete(path); }
    }

    static bool HasColumn(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
