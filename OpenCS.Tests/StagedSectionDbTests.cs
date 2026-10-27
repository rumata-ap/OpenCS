using CScore;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Стадийные сечения в БД v91 и миграция v90 → v91 (удаление двухстадийных).</summary>
public sealed class StagedSectionDbTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs_staged_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { }
    }

    /// <summary>Сечение-источник из двух областей пула (бетон и арматура из ReportFixtures).</summary>
    static CrossSection SaveSource(DatabaseService db, string tag)
    {
        var src = ReportFixtures.BuildBeam();
        foreach (var a in src.Areas) db.SaveMaterialArea(a);
        src.Tag = tag;
        db.SaveCrossSection(src);
        db.CrossSections.Add(src);
        return src;
    }

    /// <summary>Скалярный запрос отдельным соединением (у сервиса соединение закрытое).</summary>
    long Scalar(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (long)cmd.ExecuteScalar()!;
    }

    static DatabaseService Open(string path)
    {
        var db = new DatabaseService(path);
        db.LoadAll();
        return db;
    }

    [Fact]
    public void StagedSection_RoundTrip_SourceLinkAndOwnAreas()
    {
        int stagedId, srcId, ownAreaId;
        using (var db = Open(_path))
        {
            var src = SaveSource(db, "балка");
            srcId = src.Id;
            var own = ReportFixtures.BuildBeam().Areas[0];
            own.Tag = "надбетонка";
            db.SaveMaterialArea(own);
            ownAreaId = own.Id;
            var staged = new StagedSection
            {
                Tag = "усиление",
                Parts =
                [
                    new SectionPart { Index = 0, Tag = "исходное", SourceSectionId = src.Id, Areas = src.Areas },
                    new SectionPart { Index = 1, Tag = "обойма", Areas = [own] },
                ],
            };
            staged.SyncAreas();
            db.SaveCrossSection(staged);
            stagedId = staged.Id;
        }

        using (var db = Open(_path))
        {
            var s = Assert.IsType<StagedSection>(db.CrossSections.Single(c => c.Id == stagedId));
            Assert.Equal(["исходное", "обойма"], s.Parts.Select(p => p.Tag));
            Assert.Equal(srcId, s.Parts[0].SourceSectionId);
            var src = db.CrossSections.Single(c => c.Id == srcId);
            Assert.Same(src.Areas, s.Parts[0].Areas);          // живая ссылка
            Assert.Equal([ownAreaId], s.Parts[1].Areas.Select(a => a.Id));
            Assert.Equal(src.Areas.Count + 1, s.Areas.Count);  // объединение
            Assert.Single(db.SectionsUsingAsPart(srcId));
        }
    }

    [Fact]
    public void DeleteStaged_RemovesPartRows()
    {
        using (var db = Open(_path))
        {
            var src = SaveSource(db, "балка");
            var staged = new StagedSection { Tag = "у", Parts = [new SectionPart { Index = 0, SourceSectionId = src.Id, Areas = src.Areas }] };
            staged.SyncAreas();
            db.SaveCrossSection(staged);
            db.CrossSections.Add(staged);
            db.DeleteCrossSection(staged);
            Assert.Empty(db.SectionsUsingAsPart(src.Id));
        }
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM cross_section_parts"));
    }

    [Fact]
    public void MigrationV91_RemovesTwoStageSectionsTasksAndResults()
    {
        int keptSectionId, keptTaskId;
        using (var db = Open(_path))
        {
            var stage1 = SaveSource(db, "этап 1");
            keptSectionId = stage1.Id;
            var task = new CalcTask { Tag = "НДС", Kind = "strain_state", SectionId = stage1.Id, CalcType = CalcType.C };
            db.SaveCalcTask(task);
            keptTaskId = task.Id;
        }
        // Имитация базы v90 с двухстадийным сечением, задачей и результатом.
        using (var conn = new SqliteConnection($"Data Source={_path}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                CREATE TABLE IF NOT EXISTS cross_section_stages (section_id INTEGER NOT NULL, stage1_section_id INTEGER NOT NULL);
                CREATE TABLE IF NOT EXISTS cross_section_stage_kurvature (section_id INTEGER PRIMARY KEY, e0 REAL, ky REAL, kz REAL);
                DROP TABLE IF EXISTS cross_section_part_areas;
                DROP TABLE IF EXISTS cross_section_parts;
                INSERT INTO cross_sections (id, num, tag, type) VALUES (900, 9, 'двухстадийное', 'two_stage');
                INSERT INTO cross_section_stages VALUES (900, {keptSectionId});
                INSERT INTO calc_tasks (id, tag, kind, section_id) VALUES (901, 'дв', 'two_stage_strain', 900);
                INSERT INTO calc_tasks (id, tag, kind, section_id) VALUES (902, 'дв-п', 'two_stage_strain_batch', {keptSectionId});
                INSERT INTO calc_results (task_id, task_kind, status) VALUES (901, 'two_stage_strain', 'ok');
                UPDATE settings SET value_json = '90' WHERE key = 'schema_version';
                """;
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        using (var db = Open(_path))
        {
            Assert.Single(db.MigrationNotices); // одно уведомление: удалено 1 сечение (в тестах Loc возвращает ключ)
            Assert.DoesNotContain(db.CrossSections, c => c.Id == 900);
            Assert.Contains(db.CrossSections, c => c.Id == keptSectionId);
            Assert.DoesNotContain(db.CalcTasks, t => t.Id is 901 or 902);
            Assert.Contains(db.CalcTasks, t => t.Id == keptTaskId);
        }
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name IN ('cross_section_stages','cross_section_stage_kurvature')"));
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM calc_results WHERE task_id = 901"));
        Assert.Equal(2L, Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name IN ('cross_section_parts','cross_section_part_areas')"));
    }
}
