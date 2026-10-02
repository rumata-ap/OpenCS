using System.IO;
using CScore.Fem;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>БД v71: путь к файлу проекта-источника схемы (fem_schemas.source_path).</summary>
public class FemSchemaSourcePathTests
{
    [Fact]
    public void SourcePath_SavedLoadedAndUpdated()
    {
        string path = TempDb();
        try
        {
            int id;
            using (var db = Open(path))
            {
                var schema = new FemSchema { Tag = "Музей", SourceType = "scad", SourcePath = @"C:\Модели\музей.SPR" };
                db.SaveFemSchema(schema);
                id = schema.Id;
                db.SaveFemSchema(new FemSchema { Tag = "Ручная" });
            }

            using (var db = Open(path))
            {
                var loaded = db.FemSchemas.Single(s => s.Id == id);
                Assert.Equal(@"C:\Модели\музей.SPR", loaded.SourcePath);
                Assert.Null(db.FemSchemas.Single(s => s.Tag == "Ручная").SourcePath);

                loaded.SourcePath = @"D:\Перенесено\музей.SPR";
                db.SaveFemSchema(loaded);
            }

            using (var db = Open(path))
                Assert.Equal(@"D:\Перенесено\музей.SPR", db.FemSchemas.Single(s => s.Id == id).SourcePath);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void MigrationV71_AddsColumnToOldDatabase()
    {
        string path = TempDb();
        try
        {
            using (new DatabaseService(path)) { }
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = """
                    ALTER TABLE fem_schemas DROP COLUMN source_path;
                    INSERT INTO fem_schemas (tag, source_type, created) VALUES ('Старая', 'scad', '');
                    UPDATE settings SET value_json='70' WHERE key='schema_version';
                    """;
                cmd.ExecuteNonQuery();
            }

            using (var db = Open(path))
            {
                var old = db.FemSchemas.Single(s => s.Tag == "Старая");
                Assert.Null(old.SourcePath);
                old.SourcePath = @"C:\музей.SPR";
                db.SaveFemSchema(old);
            }

            using var verify = new SqliteConnection($"Data Source={path};Pooling=False");
            verify.Open();
            using var check = verify.CreateCommand();
            check.CommandText = "SELECT source_path FROM fem_schemas WHERE tag='Старая'";
            Assert.Equal(@"C:\музей.SPR", check.ExecuteScalar());
        }
        finally { Cleanup(path); }
    }

    static DatabaseService Open(string path)
    {
        var db = new DatabaseService(path);
        db.LoadAll();
        return db;
    }

    static string TempDb() => Path.Combine(Path.GetTempPath(), $"opencs-schema-source-{Guid.NewGuid():N}.db");

    static void Cleanup(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { path, path + "-wal", path + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }
}
