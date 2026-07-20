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
