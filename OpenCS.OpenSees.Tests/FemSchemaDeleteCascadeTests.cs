using CScore.Fem;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;

namespace OpenCS.OpenSees.Tests;

public sealed class FemSchemaDeleteCascadeTests
{
    static readonly string[] ChildTables =
    [
        "fem_nodes",
        "fem_members",
        "fem_mesh_nodes",
        "fem_elements",
        "fem_member_groups",
        "fem_load_cases",
        "fem_node_loads",
        "fem_load_definitions",
        "fem_analyses",
        "fem_checks",
    ];

    [Fact]
    public void DeleteFemSchema_RemovesAllChildRowsAcrossEveryFemTable()
    {
        string path = TempDatabasePath();
        try
        {
            int schemaId;
            using (var db = new DatabaseService(path))
            {
                var schema = new FemSchema { Tag = "Импорт Лира", SourceType = "lira" };
                db.SaveFemSchema(schema);
                schemaId = schema.Id;

                SeedChildRow(path, "fem_nodes", schemaId,
                    "node_tag, x, y, z, dof_mask", "'1', 0, 0, 0, 0");
                SeedChildRow(path, "fem_members", schemaId,
                    "elem_tag, elem_type, node_ids_json", "'1', 'beam', '[1,2]'");
                SeedChildRow(path, "fem_mesh_nodes", schemaId,
                    "node_tag, x, y, z", "'1', 0, 0, 0");
                SeedChildRow(path, "fem_elements", schemaId,
                    "elem_tag, elem_type, node_ids_json", "'1', 'beam', '[1,2]'");
                SeedChildRow(path, "fem_member_groups", schemaId,
                    "tag, member_tags_json", "'Группа', '[1]'");
                SeedChildRow(path, "fem_load_cases", schemaId,
                    "tag", "'ЗН1'");
                SeedChildRow(path, "fem_node_loads", schemaId,
                    "load_case_id, node_id, fx", "1, 1, 10.0");
                SeedChildRow(path, "fem_load_definitions", schemaId,
                    "tag", "'Комбинация 1'");
                SeedChildRow(path, "fem_analyses", schemaId,
                    "tag", "'Расчёт 1'");
                SeedChildRow(path, "fem_checks", schemaId,
                    "member_id, norm_code", "1, 'steel_check'");

                foreach (var table in ChildTables)
                    Assert.True(CountRows(path, table, schemaId) > 0, $"seed row missing in {table}");

                db.DeleteFemSchema(schema);
            }

            foreach (var table in ChildTables)
                Assert.Equal(0, CountRows(path, table, schemaId));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    static void SeedChildRow(string path, string table, int schemaId, string columns, string values)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {table} (schema_id, {columns}) VALUES ({schemaId}, {values})";
        command.ExecuteNonQuery();
    }

    static int CountRows(string path, string table, int schemaId)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE schema_id = {schemaId}";
        return (int)(long)command.ExecuteScalar()!;
    }

    static string TempDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"opencs-fem-schema-delete-{Guid.NewGuid():N}.db");

    static void DeleteDatabase(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}

public sealed class ForceSetDeleteCascadeTests
{
    [Fact]
    public void DeleteForceSet_RemovesItemsAndShellItems()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-force-set-delete-{Guid.NewGuid():N}.db");
        try
        {
            int setId;
            using (var db = new DatabaseService(path))
            {
                var fs = new global::CScore.ForceSet { Tag = "РСН 1", Kind = "shell" };
                fs.Items.Add(new global::CScore.LoadItem { Label = "1", N = 10 });
                fs.ShellItems.Add(new global::CScore.ShellLoadItem { Label = "1", Nx = 5 });
                db.SaveForceSet(fs);
                setId = fs.Id;

                Assert.True(CountRows(path, "force_items", setId) > 0);
                Assert.True(CountRows(path, "force_shell_items", setId) > 0);

                db.DeleteForceSet(fs);
            }

            Assert.Equal(0, CountRows(path, "force_items", setId));
            Assert.Equal(0, CountRows(path, "force_shell_items", setId));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    static int CountRows(string path, string table, int setId)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE set_id = {setId}";
        return (int)(long)command.ExecuteScalar()!;
    }
}

public sealed class FemCheckDeleteCascadeTests
{
    [Fact]
    public void DeleteFemCheck_RemovesAllLinkedCalcResults()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fem-check-delete-{Guid.NewGuid():N}.db");
        try
        {
            int checkId;
            using (var db = new DatabaseService(path))
            {
                var schema = new FemSchema { Tag = "Схема", SourceType = "internal" };
                db.SaveFemSchema(schema);

                var check = new FemCheck { SchemaId = schema.Id, MemberId = 0, NormCode = "steel_check", Tag = "Проверка 1" };
                db.SaveFemCheck(check);
                checkId = check.Id;

                // Две исторические записи через обратную ссылку fem_check_id — только последняя
                // становится check.ResultId, но обе должны удалиться вместе с проверкой.
                var r1 = new global::CScore.CalcResult { TaskKind = "steel_check", TaskTag = "run1", Created = "now", Status = "ok", DataJson = "{}" };
                db.SaveCalcResultRaw(r1, checkId);
                var r2 = new global::CScore.CalcResult { TaskKind = "steel_check", TaskTag = "run2", Created = "now", Status = "ok", DataJson = "{}" };
                db.SaveCalcResultRaw(r2, checkId);
                check.ResultId = r2.Id;
                db.SaveFemCheck(check);

                Assert.Equal(2, CountResultsByFemCheck(path, checkId));

                db.DeleteFemCheck(check);
            }

            Assert.Equal(0, CountResultsByFemCheck(path, checkId));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    static int CountResultsByFemCheck(string path, int checkId)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM calc_results WHERE fem_check_id = {checkId}";
        return (int)(long)command.ExecuteScalar()!;
    }
}
