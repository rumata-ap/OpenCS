using System.IO;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Миграция v72: индексы строк наборов усилий по (set_id, num).</summary>
public sealed class ForceItemsIndexV72Tests
{
    static readonly string[] Indexes = ["idx_force_items_set_num", "idx_force_shell_items_set_num"];

    [Fact]
    public void FreshDatabase_HasForceItemIndexes()
    {
        string path = TempDbPath();
        try
        {
            using (var db = new DatabaseService(path)) { }

            foreach (string index in Indexes)
                Assert.True(HasIndex(path, index), index);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void V71Database_GetsForceItemIndexes()
    {
        string path = TempDbPath();
        try
        {
            using (var db = new DatabaseService(path)) { }
            Exec(path, """
                DROP INDEX idx_force_items_set_num;
                DROP INDEX idx_force_shell_items_set_num;
                UPDATE settings SET value_json='71' WHERE key='schema_version';
                """);

            using (var db = new DatabaseService(path)) { }

            foreach (string index in Indexes)
                Assert.True(HasIndex(path, index), index);
        }
        finally
        {
            Delete(path);
        }
    }

    static bool HasIndex(string path, string name)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name=$n";
        cmd.Parameters.AddWithValue("$n", name);
        return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
    }

    static void Exec(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    static string TempDbPath() => Path.Combine(Path.GetTempPath(), $"opencs_v72_{Guid.NewGuid():N}.db");

    static void Delete(string path)
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(path)) File.Delete(path);
    }
}
