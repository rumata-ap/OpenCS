using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

public sealed class ParametricSteelSectionMigrationTests
{
    [Fact]
    public void Migration60To61CreatesParametricSteelTables()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-parametric-steel-migration-{Guid.NewGuid():N}.db");
        try
        {
            using (new DatabaseService(path)) { }
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var downgrade = connection.CreateCommand();
                downgrade.CommandText = """
                    DROP TABLE parametric_steel_generated_areas;
                    DROP TABLE parametric_steel_sections;
                    UPDATE settings SET value_json='60' WHERE key='schema_version';
                    """;
                downgrade.ExecuteNonQuery();
            }

            using (new DatabaseService(path)) { }

            using var verify = new SqliteConnection($"Data Source={path};Pooling=False");
            verify.Open();
            Assert.True(DatabaseService.SchemaVersion >= 61);
            Assert.Equal(DatabaseService.SchemaVersion, ReadVersion(verify));
            var tables = ReadTableNames(verify);
            Assert.Contains("parametric_steel_sections", tables);
            Assert.Contains("parametric_steel_generated_areas", tables);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
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
}
