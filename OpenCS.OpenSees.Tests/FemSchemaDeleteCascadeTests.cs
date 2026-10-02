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

public sealed class SubmodelExtractionDeleteTests
{
    [Fact]
    public void DeleteFemSchema_ChildSubmodel_RemovesRootAndMappings()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var seed = StraightBeamSubmodelPersistenceTests.CreateSeed(db);
            var extraction = db.CreateStraightBeamSubmodel(seed.Request);

            db.DeleteFemSchema(db.FemSchemas.Single(x => x.Id == extraction.SubmodelSchemaId));

            Assert.Equal(0, CountWhere(path, "submodel_extractions", "id", extraction.Id));
            Assert.Equal(0, CountWhere(path, "submodel_extraction_nodes", "extraction_id", extraction.Id));
            Assert.Equal(0, CountWhere(path, "submodel_extraction_segments", "extraction_id", extraction.Id));
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void DeleteFemSchema_ParentWithExtraction_ThrowsBeforeDelete()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var seed = StraightBeamSubmodelPersistenceTests.CreateSeed(db);
            _ = db.CreateStraightBeamSubmodel(seed.Request);

            Assert.Throws<InvalidOperationException>(() => db.DeleteFemSchema(seed.Schema));

            Assert.NotEmpty(db.GetFemMeshNodes(seed.Schema.Id));
            Assert.NotNull(db.GetFemAnalysis(seed.Analysis.Id));
        }
        finally { DeleteDatabase(path); }
    }

    static int CountWhere(string path, string table, string column, int value)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {column} = {value}";
        return (int)(long)command.ExecuteScalar()!;
    }

    static string TempDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"opencs-submodel-delete-{Guid.NewGuid():N}.db");

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

                // Две записи через обратную ссылку fem_check_id — только последняя становится
                // check.ResultId, но обе должны удалиться вместе с проверкой. Сохранение заменяет прежний
                // результат, поэтому историческая запись (из старых БД) вставляется напрямую.
                var r2 = new global::CScore.CalcResult { TaskKind = "steel_check", TaskTag = "run2", Created = "now", Status = "ok", DataJson = "{}" };
                db.SaveCalcResultRaw(r2, checkId);
                Exec(path, "INSERT INTO calc_results (task_id, task_kind, task_tag, created, status, data_json, fem_check_id) "
                         + $"VALUES (0, 'steel_check', 'run1', 'now', 'ok', '{{}}', {checkId})");
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

    [Fact]
    public void SaveCalcResultRaw_StoresRowsSeparately_AndReplacesPreviousResult()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fem-check-rows-{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new DatabaseService(path))
            {
                var schema = new FemSchema { Tag = "Схема", SourceType = "internal" };
                db.SaveFemSchema(schema);
                var check = new FemCheck { SchemaId = schema.Id, MemberId = 0, NormCode = "rc_plate_check", Tag = "Проверка" };
                db.SaveFemCheck(check);

                global::CScore.CalcResult Result(params FemCheckRow[] rows) => new()
                {
                    TaskKind = "rc_plate_check", TaskTag = "run", Created = "now", Status = "not_passed",
                    DataJson = "{\"perElement\":true,\"rowsStored\":true}", FemCheckRows = rows,
                };
                FemCheckRow Row(string label, int elem, double util, bool passed, bool notChecked = false) => new()
                {
                    Label = label, ElemNum = elem, SectionNum = 1, Utilization = util, Passed = passed, NotChecked = notChecked,
                    ForceSetTag = "РСУ (C)", CalcType = "C", WorstFormula = "(8.1)", WorstDescription = "Прочность",
                    SectionLabel = "ТЗА 1", RebarKey = "k", RebarSource = "selected",
                };

                var first = Result(Row("a", 1, 0.5, true), Row("b", 1, 1.2, false));
                db.SaveCalcResultRaw(first, check.Id);
                Assert.Null(first.FemCheckRows);
                Assert.DoesNotContain(db.CalcResults, r => r.Id == first.Id);

                var second = Result(Row("c", 1, 0.4, true), Row("d", 2, 1.5, false),
                                    Row("e", 2, double.NaN, false, notChecked: true), Row("f", 3, 0.9, true));
                db.SaveCalcResultRaw(second, check.Id);
                check.ResultId = second.Id;
                db.SaveFemCheck(check);

                Assert.Equal(1, CountResultsByFemCheck(path, check.Id));
                Assert.Equal(0, Scalar(path, $"SELECT COUNT(*) FROM fem_check_rows WHERE result_id = {first.Id}"));

                // Сначала худшие, непроверенные — в конце; строки восстанавливаются полностью.
                var rows = db.GetFemCheckRows(second.Id);
                Assert.Equal(["d", "f", "c", "e"], rows.Select(r => r.Label));
                Assert.Equal("РСУ (C)", rows[0].ForceSetTag);
                Assert.Equal("(8.1)", rows[0].WorstFormula);
                Assert.Equal("selected", rows[0].RebarSource);
                Assert.True(double.IsNaN(rows[3].Utilization));
                Assert.True(rows[3].NotChecked);

                Assert.Equal(["d", "e"], db.GetFemCheckRows(second.Id, elemNum: 2).Select(r => r.Label));
                Assert.Equal(["d", "e"], db.GetFemCheckRows(second.Id, onlyFailed: true).Select(r => r.Label));
                Assert.Equal(["d"], db.GetFemCheckRows(second.Id, limit: 1).Select(r => r.Label));
                Assert.Equal(4, db.CountFemCheckRows(second.Id));
                Assert.Equal(2, db.CountFemCheckRows(second.Id, onlyFailed: true));

                var light = db.GetFemCheckRowResults(check.Id);
                Assert.Equal(4, light.Count);
                Assert.Equal(new FemCheckRowResult(2, 1, "selected", 1.5, false, false), light[1]);

                db.DeleteFemCheck(check);
            }
            Assert.Equal(0, Scalar(path, "SELECT COUNT(*) FROM fem_check_rows"));
            Assert.Equal(0, Scalar(path, "SELECT COUNT(*) FROM fem_check_row_strings"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void FemCheckResultStream_WritesRowsByChunks_AndReplacesResultOnlyOnComplete()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fem-check-stream-{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new DatabaseService(path))
            {
                var schema = new FemSchema { Tag = "Схема", SourceType = "internal" };
                db.SaveFemSchema(schema);
                var check = new FemCheck { SchemaId = schema.Id, MemberId = 0, NormCode = "rc_plate_check", Tag = "Проверка" };
                db.SaveFemCheck(check);

                global::CScore.CalcResult Summary() => new()
                {
                    TaskKind = "rc_plate_check", TaskTag = "run", Created = "now", Status = "not_passed",
                    DataJson = "{\"perElement\":true,\"rowsStored\":true}",
                };
                FemCheckRow Row(string label, int elem, double util, bool passed) => new()
                {
                    Label = label, ElemNum = elem, Utilization = util, Passed = passed,
                    ForceSetTag = "РСУ (C)", CalcType = "C", RebarSource = "selected",
                };

                var first = Summary();
                db.SaveCalcResultRaw(first, check.Id);

                // Прерванная запись (отмена) не трогает прежний результат и не оставляет строк.
                using (var cancelled = db.BeginFemCheckResult())
                    cancelled.Write([Row("x", 1, 2.0, false)]);
                Assert.Equal(1, Scalar(path, "SELECT COUNT(*) FROM calc_results"));
                Assert.Equal(0, Scalar(path, "SELECT COUNT(*) FROM fem_check_rows"));

                var second = Summary();
                using (var stream = db.BeginFemCheckResult())
                {
                    stream.Write([Row("a", 1, 0.5, true), Row("b", 1, 1.2, false)]);
                    stream.Write([Row("c", 2, 0.9, true)]);
                    // До завершения результат проверке не принадлежит.
                    Assert.Equal(first.Id, db.GetCalcResultByFemCheck(check.Id)!.Id);
                    stream.Complete(second, check.Id);
                }
                Assert.True(second.Id > first.Id);
                Assert.Equal(1, CountResultsByFemCheck(path, check.Id));
                Assert.Equal(1, Scalar(path, "SELECT COUNT(*) FROM calc_results"));

                // Номера строк сквозные через порции, словарь строк — общий.
                Assert.Equal(["b", "c", "a"], db.GetFemCheckRows(second.Id).Select(r => r.Label));
                Assert.Equal(2, Scalar(path, $"SELECT MAX(idx) FROM fem_check_rows WHERE result_id = {second.Id}"));
                Assert.All(db.GetFemCheckRows(second.Id), r => Assert.Equal("РСУ (C)", r.ForceSetTag));
                Assert.Equal(second.Id, db.GetCalcResultByFemCheck(check.Id)!.Id);
            }

            // Заготовка, оставшаяся после аварийного выхода, удаляется при открытии.
            Exec(path, "INSERT INTO calc_results (task_id, task_kind, task_tag, created, status, data_json, fem_check_id) VALUES (0, '', '', '', 'running', '{}', NULL)");
            using (new DatabaseService(path)) { }
            Assert.Equal(1, Scalar(path, "SELECT COUNT(*) FROM calc_results"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    static void Exec(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    static int Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (int)(long)command.ExecuteScalar()!;
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

public sealed class CalcTaskDeleteCascadeTests
{
    [Fact]
    public void DeleteCalcTask_RemovesLinkedCalcResults()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-calc-task-delete-{Guid.NewGuid():N}.db");
        try
        {
            int taskId;
            using (var db = new DatabaseService(path))
            {
                var task = new global::CScore.CalcTask { Kind = "torsion_fem", Tag = "Задача 1" };
                db.SaveCalcTask(task);
                taskId = task.Id;

                var r1 = new global::CScore.CalcResult { TaskId = taskId, TaskKind = "torsion_fem", TaskTag = "run1", Created = "now", Status = "ok", DataJson = "{}" };
                db.SaveCalcResult(r1);
                var r2 = new global::CScore.CalcResult { TaskId = taskId, TaskKind = "torsion_fem", TaskTag = "run2", Created = "now", Status = "ok", DataJson = "{}" };
                db.SaveCalcResult(r2);

                Assert.Equal(2, CountResultsByTask(path, taskId));

                db.DeleteCalcTask(task);
            }

            Assert.Equal(0, CountResultsByTask(path, taskId));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    static int CountResultsByTask(string path, int taskId)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM calc_results WHERE task_id = {taskId}";
        return (int)(long)command.ExecuteScalar()!;
    }
}

public sealed class CrossSectionDeleteCascadeTests
{
    [Fact]
    public void DeleteCrossSection_RemovesStagesKurvatureAndAreaLinks()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-cross-section-delete-{Guid.NewGuid():N}.db");
        try
        {
            int sectionId;
            using (var db = new DatabaseService(path))
            {
                sectionId = InsertReturningId(path, "INSERT INTO cross_sections DEFAULT VALUES");
                int stage1Id = InsertReturningId(path, "INSERT INTO cross_sections DEFAULT VALUES");
                int areaId = InsertReturningId(path, "INSERT INTO material_areas DEFAULT VALUES");

                Exec(path, $"INSERT INTO cross_section_stages (section_id, stage1_section_id) VALUES ({sectionId}, {stage1Id})");
                Exec(path, $"INSERT INTO cross_section_stage_kurvature (section_id) VALUES ({sectionId})");
                Exec(path, $"INSERT INTO cross_section_areas (section_id, area_id) VALUES ({sectionId}, {areaId})");

                Assert.True(CountWhere(path, "cross_section_stages", "section_id", sectionId) > 0);
                Assert.True(CountWhere(path, "cross_section_stage_kurvature", "section_id", sectionId) > 0);
                Assert.True(CountWhere(path, "cross_section_areas", "section_id", sectionId) > 0);

                db.DeleteCrossSection(new global::CScore.CrossSection { Id = sectionId });
            }

            Assert.Equal(0, CountWhere(path, "cross_section_stages", "section_id", sectionId));
            Assert.Equal(0, CountWhere(path, "cross_section_stage_kurvature", "section_id", sectionId));
            Assert.Equal(0, CountWhere(path, "cross_section_areas", "section_id", sectionId));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    static int InsertReturningId(string path, string insertSql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = insertSql + "; SELECT last_insert_rowid();";
        return (int)(long)command.ExecuteScalar()!;
    }

    static void Exec(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    static int CountWhere(string path, string table, string column, int value)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {column} = {value}";
        return (int)(long)command.ExecuteScalar()!;
    }
}

public sealed class MaterialAreaDeleteCascadeTests
{
    [Fact]
    public void DeleteMaterialArea_RemovesPointAndMeshFibers()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-material-area-delete-{Guid.NewGuid():N}.db");
        try
        {
            int areaId;
            using (var db = new DatabaseService(path))
            {
                areaId = InsertReturningId(path, "INSERT INTO material_areas DEFAULT VALUES");
                Exec(path, $"INSERT INTO point_fibers (area_id) VALUES ({areaId})");
                Exec(path, $"INSERT INTO mesh_fibers (area_id) VALUES ({areaId})");

                Assert.True(CountWhere(path, "point_fibers", "area_id", areaId) > 0);
                Assert.True(CountWhere(path, "mesh_fibers", "area_id", areaId) > 0);

                db.DeleteMaterialArea(new global::CScore.MaterialArea { Id = areaId });
            }

            Assert.Equal(0, CountWhere(path, "point_fibers", "area_id", areaId));
            Assert.Equal(0, CountWhere(path, "mesh_fibers", "area_id", areaId));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    static int InsertReturningId(string path, string insertSql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = insertSql + "; SELECT last_insert_rowid();";
        return (int)(long)command.ExecuteScalar()!;
    }

    static void Exec(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    static int CountWhere(string path, string table, string column, int value)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {column} = {value}";
        return (int)(long)command.ExecuteScalar()!;
    }
}

public sealed class FireSectionDeleteCascadeTests
{
    [Fact]
    public void DeleteFireSection_RemovesEdgesAndThermalResults()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fire-section-delete-{Guid.NewGuid():N}.db");
        try
        {
            int fireSectionId;
            using (var db = new DatabaseService(path))
            {
                fireSectionId = InsertReturningId(path, "INSERT INTO fire_sections DEFAULT VALUES");
                Exec(path, $"INSERT INTO fire_section_edges (fire_section_id, edge_index) VALUES ({fireSectionId}, 0)");
                Exec(path, $"INSERT INTO fire_thermal_results (fire_section_id, blob) VALUES ({fireSectionId}, X'00')");

                Assert.True(CountWhere(path, "fire_section_edges", "fire_section_id", fireSectionId) > 0);
                Assert.True(CountWhere(path, "fire_thermal_results", "fire_section_id", fireSectionId) > 0);

                db.DeleteFireSection(fireSectionId);
            }

            Assert.Equal(0, CountWhere(path, "fire_section_edges", "fire_section_id", fireSectionId));
            Assert.Equal(0, CountWhere(path, "fire_thermal_results", "fire_section_id", fireSectionId));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    static int InsertReturningId(string path, string insertSql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = insertSql + "; SELECT last_insert_rowid();";
        return (int)(long)command.ExecuteScalar()!;
    }

    static void Exec(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    static int CountWhere(string path, string table, string column, int value)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {column} = {value}";
        return (int)(long)command.ExecuteScalar()!;
    }
}
