using Microsoft.Data.Sqlite;
using CScore.Fem;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Миграция v73: вложения FEM-схемы по видам (fem_schema_source_files).</summary>
public sealed class FemSchemaSourceFilesV73Tests
{
    [Fact]
    public void SaveGetReplace_ByKind()
    {
        string path = TempDbPath();
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "SCAD" };
            db.SaveFemSchema(schema);

            Assert.Null(db.GetFemSchemaSourceFile(schema.Id, FemSchemaSourceFileKind.ScadSelectedRebar));

            db.SaveFemSchemaSourceFile(schema.Id, FemSchemaSourceFileKind.ScadSelectedRebar, "a.opencs-scad.json", [1, 2]);
            db.SaveFemSchemaSourceFile(schema.Id, FemSchemaSourceFileKind.ScadConcreteGroups, "", [3]);
            db.SaveFemSchemaSourceFile(schema.Id, FemSchemaSourceFileKind.ScadSelectedRebar, "b.opencs-scad.json", [4, 5, 6]);

            var rebar = db.GetFemSchemaSourceFile(schema.Id, FemSchemaSourceFileKind.ScadSelectedRebar)!.Value;
            Assert.Equal("b.opencs-scad.json", rebar.FileName);
            Assert.Equal([4, 5, 6], rebar.Data);
            Assert.NotEqual("", rebar.ImportedAt);
            Assert.Equal([3], db.GetFemSchemaSourceFile(schema.Id, FemSchemaSourceFileKind.ScadConcreteGroups)!.Value.Data);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void DeleteSchema_RemovesSourceFiles()
    {
        string path = TempDbPath();
        try
        {
            using (var db = new DatabaseService(path))
            {
                var schema = new FemSchema { Tag = "SCAD" };
                db.SaveFemSchema(schema);
                db.SaveFemSchemaSourceFile(schema.Id, FemSchemaSourceFileKind.ScadSelectedRebar, "a", [1]);
                db.DeleteFemSchema(schema);
            }
            Assert.Equal(0L, Scalar(path, "SELECT COUNT(*) FROM fem_schema_source_files"));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void V72Database_GetsTable()
    {
        string path = TempDbPath();
        try
        {
            using (new DatabaseService(path)) { }
            Exec(path, """
                DROP TABLE fem_schema_source_files;
                UPDATE settings SET value_json = '72' WHERE key = 'schema_version';
                """);

            using (new DatabaseService(path)) { }

            Assert.Equal(1L, Scalar(path, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='fem_schema_source_files'"));
            Assert.Equal(DatabaseService.SchemaVersion.ToString(),
                Scalar(path, "SELECT value_json FROM settings WHERE key='schema_version'"));
        }
        finally
        {
            Delete(path);
        }
    }

    static object? Scalar(string path, string sql)
    {
        using var c = new SqliteConnection($"Data Source={path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    static void Exec(string path, string sql)
    {
        using var c = new SqliteConnection($"Data Source={path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    static string TempDbPath()
        => Path.Combine(Path.GetTempPath(), $"opencs-v73-{Guid.NewGuid():N}.db");

    static void Delete(string path)
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }
}
