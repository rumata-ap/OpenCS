using CScore.Fem;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Фрагмент этажа в БД (v92): миграция, поля схемы-фрагмента.</summary>
public sealed class StoreyFragmentDbTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs-fragment-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }

    static readonly string[] FragmentTables =
        ["fem_fragment_cuts", "fem_fragment_ref_elements", "fem_fragment_field_loads", "fem_fragment_rebar_sources"];

    static readonly string[] FragmentColumns =
        ["parent_schema_id", "fragment_z1", "fragment_z2", "parent_fingerprint", "fragment_build_report_json"];

    [Fact]
    public void NewDatabase_HasV92TablesAndColumns()
    {
        using (new DatabaseService(_path)) { }
        Assert.Equal(92, DatabaseService.SchemaVersion);
        Assert.Equal("92", Scalar("SELECT value_json FROM settings WHERE key='schema_version'"));
        foreach (var t in FragmentTables)
            Assert.Equal(1L, Scalar($"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{t}'"));
        foreach (var c in FragmentColumns)
            Assert.Equal(1L, Scalar($"SELECT COUNT(*) FROM pragma_table_info('fem_schemas') WHERE name='{c}'"));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='idx_fem_mesh_nodes_schema_z'"));
    }

    [Fact]
    public void V91Database_MigratesToV92()
    {
        using (new DatabaseService(_path)) { }
        Exec(string.Join(";", FragmentTables.Select(t => $"DROP TABLE {t}"))
             + ";UPDATE settings SET value_json='91' WHERE key='schema_version'");
        using (new DatabaseService(_path)) { }
        Assert.Equal("92", Scalar("SELECT value_json FROM settings WHERE key='schema_version'"));
        foreach (var t in FragmentTables)
            Assert.Equal(1L, Scalar($"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{t}'"));
        using (new DatabaseService(_path)) { }
    }

    [Fact]
    public void FragmentFields_RoundTrip()
    {
        int id;
        using (var db = new DatabaseService(_path))
        {
            var plain = new FemSchema { Tag = "Родитель" };
            db.SaveFemSchema(plain);
            var schema = new FemSchema
            {
                Tag = "Этаж 2", ParentSchemaId = plain.Id, FragmentZ1 = 1.5, FragmentZ2 = 4.5,
                ParentFingerprint = "ABC", FragmentBuildReportJson = "[]",
            };
            db.SaveFemSchema(schema);
            id = schema.Id;
        }
        using (var db = new DatabaseService(_path))
        {
            db.LoadAll();
            var s = db.FemSchemas.Single(x => x.Id == id);
            Assert.True(s.IsFragment);
            Assert.Equal(1.5, s.FragmentZ1);
            Assert.Equal(4.5, s.FragmentZ2);
            Assert.Equal("ABC", s.ParentFingerprint);
            Assert.Equal("[]", s.FragmentBuildReportJson);
            Assert.False(db.FemSchemas.Single(x => x.Tag == "Родитель").IsFragment);
        }
    }

    object? Scalar(string sql)
    {
        using var c = new SqliteConnection($"Data Source={_path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    void Exec(string sql)
    {
        using var c = new SqliteConnection($"Data Source={_path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
