using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

public sealed class FireSchemaV56Tests
{
    [Fact]
    public void FreshDatabase_HasAllV56Columns()
    {
        string path = TempDbPath();
        try
        {
            using (var db = new DatabaseService(path))
                db.LoadAll();

            Assert.True(HasColumn(path, "materials", "fire_rebar_class"));
            Assert.True(HasColumn(path, "fire_thermal_results", "input_json"));
            Assert.True(HasColumn(path, "fire_thermal_results", "input_hash"));
            Assert.True(HasColumn(path, "fire_thermal_results", "snapshot_count"));
            Assert.True(HasColumn(path, "fire_thermal_results", "duration_min"));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void OldV55Database_MigratesAndKeepsData()
    {
        string path = TempDbPath();
        try
        {
            CreateV55Database(path);

            // Минимальная v55 fixture содержит только таблицы, необходимые для
            // проверки миграции; LoadAll здесь не нужен и требует полной схемы.
            using (var db = new DatabaseService(path)) { }

            Assert.True(HasColumn(path, "materials", "fire_rebar_class"));
            Assert.True(HasColumn(path, "fire_thermal_results", "input_hash"));
            Assert.Equal("56", ReadSchemaVersion(path));
            Assert.Equal(1, CountRows(path, "materials"));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void MigrationIsIdempotent()
    {
        string path = TempDbPath();
        try
        {
            CreateV55Database(path);
            using (var db1 = new DatabaseService(path)) { }
            using (var db2 = new DatabaseService(path)) { }

            Assert.Equal("56", ReadSchemaVersion(path));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void FireRebarClass_RoundTrips()
    {
        string path = TempDbPath();
        try
        {
            using (var db = new DatabaseService(path))
            {
                db.LoadAll();
                var m = new CScore.Material
                {
                    Type = CScore.MatType.ReSteelF,
                    Tag = "A500",
                    FireRebarClass = "a500c_25g2s"
                };
                db.SaveMaterial(m);
            }

            using (var db = new DatabaseService(path))
            {
                db.LoadAll();
                var loaded = Assert.Single(db.Materials);
                Assert.Equal("a500c_25g2s", loaded.FireRebarClass);
            }
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void EmptyFireRebarClass_LoadsAsEmptyString()
    {
        string path = TempDbPath();
        try
        {
            CreateV55Database(path);

            using (var db = new DatabaseService(path)) { }

            Assert.Equal("", ReadMaterialFireRebarClass(path));
        }
        finally
        {
            Delete(path);
        }
    }

    static string TempDbPath()
        => Path.Combine(Path.GetTempPath(), $"opencs-fire-v56-{Guid.NewGuid():N}.db");

    static void Delete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    static void CreateV55Database(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE settings (key TEXT PRIMARY KEY, value_json TEXT NOT NULL);
            INSERT INTO settings (key, value_json) VALUES ('schema_version', '55');

            CREATE TABLE materials (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                type INTEGER NOT NULL DEFAULT 0,
                tag TEXT NOT NULL DEFAULT '',
                description TEXT NOT NULL DEFAULT '',
                e REAL NOT NULL DEFAULT 0,
                chars_json TEXT NOT NULL DEFAULT '[]',
                aggregate_type TEXT NOT NULL DEFAULT 'silicate',
                base_type INTEGER NOT NULL DEFAULT 0,
                custom_diagram_ids TEXT NOT NULL DEFAULT '{}'
            );
            INSERT INTO materials (type, tag, chars_json) VALUES (2, 'A500', '[]');

            CREATE TABLE fire_sections (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                num INTEGER NOT NULL DEFAULT 0,
                tag TEXT NOT NULL DEFAULT '',
                section_id INTEGER NOT NULL DEFAULT 0,
                fire_duration_min REAL NOT NULL DEFAULT 60,
                fire_curve TEXT NOT NULL DEFAULT 'iso834',
                mesh_step_m REAL NOT NULL DEFAULT 0.01,
                time_step_s REAL NOT NULL DEFAULT 5,
                theta REAL NOT NULL DEFAULT 1,
                picard_tol_celsius REAL NOT NULL DEFAULT 0.5,
                picard_max_iter INTEGER NOT NULL DEFAULT 20,
                snapshot_step_min REAL NOT NULL DEFAULT 5,
                bc_preset TEXT NOT NULL DEFAULT 'manual',
                hole_bc_preset TEXT NOT NULL DEFAULT 'ambient',
                algorithm TEXT NOT NULL DEFAULT 'ruppert',
                smooth_iter_tri INTEGER NOT NULL DEFAULT 5,
                aggregate_type TEXT NOT NULL DEFAULT '',
                mesh_element_type TEXT NOT NULL DEFAULT 'linear'
            );

            CREATE TABLE fire_thermal_results (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                fire_section_id INTEGER NOT NULL REFERENCES fire_sections(id) ON DELETE CASCADE,
                created TEXT NOT NULL DEFAULT '',
                blob BLOB NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    static bool HasColumn(string path, string table, string column)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    static string ReadSchemaVersion(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value_json FROM settings WHERE key='schema_version'";
        return cmd.ExecuteScalar()?.ToString() ?? "";
    }

    static int CountRows(string path, string table)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    static string ReadMaterialFireRebarClass(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT fire_rebar_class FROM materials WHERE id=1";
        return cmd.ExecuteScalar()?.ToString() ?? "";
    }
}
