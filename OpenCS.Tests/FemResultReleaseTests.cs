using CScore;
using CScore.Fem;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Прежний результат постановки МКЭ удаляется при сбросе, новом расчёте и удалении постановки (раньше строка
/// calc_results с полями шагов оставалась навсегда); результат, удержанный извлечением субмодели, остаётся до удаления
/// субмодели; миграция v103 чистит накопленные осиротевшие строки.
/// </summary>
[Collection("Loc language")]
public sealed class FemResultReleaseTests : IDisposable
{
    public FemResultReleaseTests() => Loc.SetLanguage("ru-RU");
    public void Dispose() => Loc.SetLanguage(null);

    static string TempDb() => Path.Combine(Path.GetTempPath(), "opencs_release_" + Guid.NewGuid().ToString("N") + ".db");

    static CalcResult NewResult(DatabaseService db, string tag)
    {
        var r = new CalcResult { TaskId = 0, TaskKind = FemCsfeaRunner.TaskKind, TaskTag = tag, Created = "2026-11-27 12:00:00", DataJson = "{}" };
        db.SaveCalcResult(r);
        db.SaveFemResultSteps(r.Id, [(1, new byte[] { 1, 2, 3 })]);
        return r;
    }

    static long Scalar(string path, string sql)
    {
        using var c = new SqliteConnection($"Data Source={path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    static void Exec(string path, string sql)
    {
        using var c = new SqliteConnection($"Data Source={path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=OFF;" + sql;
        cmd.ExecuteNonQuery();
    }

    static long Results(string path, int id) => Scalar(path, $"SELECT COUNT(*) FROM calc_results WHERE id={id}");
    static long Steps(string path, int id) => Scalar(path, $"SELECT COUNT(*) FROM fem_result_steps WHERE result_id={id}");

    static void Cleanup(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { path, path + "-journal", path + "-wal", path + "-shm" })
            try { if (File.Exists(f)) File.Delete(f); } catch (IOException) { }
    }

    [Fact]
    public void RerunAndReset_DeletePreviousResultWithSteps()
    {
        string path = TempDb();
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Схема" };
            db.SaveFemSchema(schema);
            var analysis = new FemAnalysis { SchemaId = schema.Id, Tag = "Секущий", Kind = FemCsfeaRunner.AnalysisKind };
            db.SaveFemAnalysis(analysis);

            var r1 = NewResult(db, "Секущий");
            analysis.ResultId = r1.Id;
            db.SaveFemAnalysis(analysis);
            Assert.Equal(1, Results(path, r1.Id));

            // Новый расчёт: прежний результат и его поля шагов удаляются.
            var r2 = NewResult(db, "Секущий");
            analysis.ResultId = r2.Id;
            db.SaveFemAnalysis(analysis);
            Assert.Equal(0, Results(path, r1.Id));
            Assert.Equal(0, Steps(path, r1.Id));
            Assert.Equal(1, Results(path, r2.Id));

            // Повторная запись без смены результата (статус, имя) ничего не удаляет.
            analysis.Tag = "Секущий 2";
            db.SaveFemAnalysis(analysis);
            Assert.Equal(1, Results(path, r2.Id));

            // Сброс (смена входа): результат удаляется.
            analysis.InvalidateResult();
            db.SaveFemAnalysis(analysis);
            Assert.Equal(0, Results(path, r2.Id));
            Assert.Equal(0, Steps(path, r2.Id));
            Assert.DoesNotContain(db.CalcResults, r => r.Id == r2.Id);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void ResultPinnedBySubmodel_KeptUntilSubmodelDeleted_AndNamedInDeleteQuestion()
    {
        string path = TempDb();
        try
        {
            using var db = new DatabaseService(path);
            var parent = new FemSchema { Tag = "Родитель" };
            db.SaveFemSchema(parent);
            var sub = new FemSchema { Tag = "Субмодель ригеля" };
            db.SaveFemSchema(sub);
            var analysis = new FemAnalysis { SchemaId = parent.Id, Tag = "Секущий", Kind = FemCsfeaRunner.AnalysisKind };
            db.SaveFemAnalysis(analysis);
            var r = NewResult(db, "Секущий");
            analysis.ResultId = r.Id;
            db.SaveFemAnalysis(analysis);
            Exec(path, $"""
                INSERT INTO submodel_extractions (parent_schema_id, submodel_schema_id, parent_analysis_id, parent_result_id)
                VALUES ({parent.Id}, {sub.Id}, {analysis.Id}, {r.Id})
                """);

            var impact = FemAnalysisAuthoring.DeleteImpact(db, analysis);
            Assert.Contains(string.Format(Loc.S("FemAnalysisDependentSubmodel"), "Субмодель ригеля"), impact.Dependents);

            analysis.InvalidateResult();
            db.SaveFemAnalysis(analysis);
            Assert.Equal(1, Results(path, r.Id));      // субмодель читает данные родителя по этому Id

            db.DeleteFemAnalysis(analysis);
            Assert.Equal(1, Results(path, r.Id));

            db.DeleteFemSchema(sub);                   // извлечение ушло со схемой — результат освобождён
            Assert.Equal(0, Results(path, r.Id));
            Assert.Equal(0, Steps(path, r.Id));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void DeleteAnalysis_DeletesItsResult()
    {
        string path = TempDb();
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Схема" };
            db.SaveFemSchema(schema);
            var analysis = new FemAnalysis { SchemaId = schema.Id, Tag = "Линейный", Kind = FemCsfeaRunner.LinearAnalysisKind };
            db.SaveFemAnalysis(analysis);
            var r = NewResult(db, "Линейный");
            analysis.ResultId = r.Id;
            db.SaveFemAnalysis(analysis);

            db.DeleteFemAnalysis(analysis);

            Assert.Equal(0, Results(path, r.Id));
            Assert.Equal(0, Steps(path, r.Id));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void MigrationV103_RemovesOnlyOrphanedAnalysisResults()
    {
        string path = TempDb();
        try
        {
            int live, orphan, pinned, task;
            using (var db = new DatabaseService(path))
            {
                var schema = new FemSchema { Tag = "Схема" };
                db.SaveFemSchema(schema);
                var analysis = new FemAnalysis { SchemaId = schema.Id, Tag = "Секущий", Kind = FemCsfeaRunner.AnalysisKind };
                db.SaveFemAnalysis(analysis);
                live = NewResult(db, "Секущий").Id;
                analysis.ResultId = live;
                db.SaveFemAnalysis(analysis);
                orphan = NewResult(db, "Секущий").Id;   // как после сброса до v103: строка без ссылки
                pinned = NewResult(db, "Секущий").Id;
                var t = new CalcResult { TaskId = 77, TaskKind = "strain_state", TaskTag = "НДС", Created = "2026-11-27", DataJson = "{}" };
                db.SaveCalcResult(t);
                task = t.Id;
                Exec(path, $"""
                    INSERT INTO submodel_extractions (parent_schema_id, submodel_schema_id, parent_analysis_id, parent_result_id)
                    VALUES ({schema.Id}, 9999, {analysis.Id}, {pinned});
                    UPDATE settings SET value_json = '102' WHERE key = 'schema_version';
                    """);
            }
            SqliteConnection.ClearAllPools();

            using (var db = new DatabaseService(path))
            {
                Assert.Equal(1, Results(path, live));
                Assert.Equal(0, Results(path, orphan));
                Assert.Equal(0, Steps(path, orphan));
                Assert.Equal(1, Results(path, pinned));
                Assert.Equal(1, Results(path, task));
                Assert.Contains(string.Format(Loc.S("FemOrphanResultsRemovedNotice"), 1, "0"), db.MigrationNotices);
            }
        }
        finally { Cleanup(path); }
    }
}
