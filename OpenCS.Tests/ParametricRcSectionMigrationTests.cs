using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

public sealed class ParametricRcSectionMigrationTests
{
    [Fact]
    public void Migration57To58CreatesParametricTablesAndRebarColumns()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-parametric-migration-{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new DatabaseService(path))
            {
                using var command = new SqliteConnection($"Data Source={path}");
                command.Open();
                using var setVersion = command.CreateCommand();
                setVersion.CommandText = "UPDATE settings SET value_json='57' WHERE key='schema_version'";
                setVersion.ExecuteNonQuery();
            }

            using var migrated = new DatabaseService(path);
            using var verify = new SqliteConnection($"Data Source={path}");
            verify.Open();
            Assert.Equal(DatabaseService.SchemaVersion, ReadVersion(verify));
            Assert.Contains("parametric_rc_sections", ReadTableNames(verify));
            Assert.Contains("parametric_rc_generated_areas", ReadTableNames(verify));
            Assert.Contains("rebar_representation", ReadColumns(verify, "material_areas"));
            Assert.Contains("idealized_axis", ReadColumns(verify, "material_areas"));
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    static int ReadVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value_json FROM settings WHERE key='schema_version'";
        return int.Parse((string)command.ExecuteScalar()!);
    }

    static HashSet<string> ReadTableNames(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        using var reader = command.ExecuteReader();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }

    static HashSet<string> ReadColumns(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        using var reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read()) columns.Add(reader.GetString(1));
        return columns;
    }
}
