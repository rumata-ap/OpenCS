using System.IO;
using System.Text.Json;
using CScore;
using CScore.Batch;
using CScore.Sp63Punching;
using OpenCS.Cli;
using OpenCS.Reporting;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Срез 2 продавливания: пакет по строкам набора усилий, огибающая, задача по строке, консоль.</summary>
[Collection("Loc language")]
public sealed class PunchingBatchTests
{
    /// <summary>Базовый узел книги «Продавливание»: пилон 250×800, h = 200, ax = 40, ay = 30, 2Ø6 шаг 50.</summary>
    static PunchingTaskParams Gosha() => new()
    {
        Area = new PunchingAreaParams { Shape = "rectangle", Bx = 0.25, By = 0.8 },
        Slab = new PunchingSlabParams { H = 0.2, Ax = 0.04, Ay = 0.03, RbtOverride = 1050 },
        Reinforcement = new PunchingReinforcementParams { Enabled = true, Diameter = 0.006, Count = 2, Step = 0.05, RswOverride = 170_000 },
        Forces = new PunchingForceParams { F = 1, Mx = 0, My = 0 }
    };

    static ForceSet Set() => new()
    {
        Id = 3, Kind = "bar", Tag = "РСУ К-1",
        Items =
        [
            new LoadItem { Id = 31, Num = 1, Label = "малое", N = -300, Mx = 10, My = 5 },
            new LoadItem { Id = 32, Num = 2, Label = "эталон", N = -600, Mx = 30, My = 20 },
            new LoadItem { Id = 33, Num = 3, Label = "отрыв", N = 120, Mx = 5, My = 0 },
            new LoadItem { Id = 34, Num = 4, Label = "ноль", N = 0, Mx = 0, My = 0 },
        ]
    };

    static CalcTask Batch(PunchingTaskParams p) => new()
    {
        Id = 9, Tag = "Узел К-1", Kind = PunchingBatchRunner.Kind, CalcType = CalcType.C, ForceSetId = 3, ParamsJson = p.ToJson()
    };

    static (CalcResult Result, string Path) RunBatch(PunchingTaskParams p, ForceSet? set = null)
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-punch-batch-{Guid.NewGuid():N}.db");
        using var database = new DatabaseService(path);
        if (set is not null) database.ForceSets.Add(set);
        var result = TaskRunner.Run(Batch(p), null!, new LoadItem(), CalcSettings.Default, new TaskRunContext { Database = database });
        return (result, path);
    }

    static void Delete(string path)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(path); } catch (IOException) { }
    }

    [Fact]
    public void Core_WithoutTrace_SameNumbers()
    {
        var input = PunchingBatchRunner.RowParams(Gosha(), Set().Items[1]).ToInput(1050, 170_000);
        var full = PunchingChecker.Check(input);
        var fast = PunchingChecker.Check(input, includeTrace: false);
        Assert.Equal(full.Utilization, fast.Utilization, 15);
        Assert.Equal(full.Variants.Count, fast.Variants.Count);
        Assert.All(fast.Variants, v => Assert.Empty(v.Trace));
        Assert.All(full.Variants, v => Assert.NotEmpty(v.Trace));
    }

    [Fact]
    public void Rows_MatchSingleTask_EnvelopeAndGoverning()
    {
        var (result, path) = RunBatch(Gosha(), Set());
        try
        {
            Assert.Equal("not_passed", result.Status);
            using var doc = JsonDocument.Parse(result.DataJson);
            var root = doc.RootElement;
            var rows = root.GetProperty("rows").EnumerateArray().ToList();
            Assert.Equal(4, rows.Count);

            // Каждая строка — та же задача «punching» с усилиями строки.
            foreach (var (row, item) in rows.Zip(Set().Items))
            {
                var single = PunchingRunner.Run(new CalcTask
                {
                    Id = 1, Kind = PunchingRunner.Kind, CalcType = CalcType.C,
                    ParamsJson = PunchingBatchRunner.RowParams(Gosha(), item).ToJson()
                }, CalcSettings.Default, null);
                using var s = JsonDocument.Parse(single.DataJson);
                Assert.Equal(s.RootElement.GetProperty("ratio").GetDouble(), row.GetProperty("ratio").GetDouble(), 12);
                Assert.Equal(s.RootElement.GetProperty("governing").GetString(), row.GetProperty("governing").GetString());
            }

            // Эталон книги: строка 2 определяет, контур — разорванный у торцов пилона.
            Assert.Equal(2, root.GetProperty("governing_num").GetInt32());
            Assert.Equal("pylon_broken", root.GetProperty("governing_variant").GetString());
            Assert.Equal(1.0181060000790736, root.GetProperty("utilization").GetDouble(), 9);
            Assert.Equal("reinforcement_required", root.GetProperty("status").GetString());

            var env = root.GetProperty("variants").EnumerateArray().ToDictionary(v => v.GetProperty("id").GetString()!);
            Assert.Equal(0.9272591179416902, env["closed"].GetProperty("envelope").GetDouble(), 9);
            Assert.Equal(2, env["closed"].GetProperty("envelope_num").GetInt32());

            // Нулевая строка — Кисп 0, проходит; строка отрыва помечена.
            Assert.Equal(0, rows[3].GetProperty("ratio").GetDouble());
            Assert.Equal("ok", rows[3].GetProperty("status").GetString());
            Assert.True(rows[2].GetProperty("reversed").GetBoolean());
            Assert.False(rows[1].GetProperty("reversed").GetBoolean());
            Assert.Contains("N другого знака", BatchRowVerdict.TryRead(rows[2])!.Text);
            Assert.Contains(root.GetProperty("warnings").EnumerateArray(), w => w.GetProperty("code").GetString() == "reversed_rows");

            // Ход определяющей строки — в формате одиночной задачи.
            var detail = PunchingReportData.Parse(root.GetProperty("governing_detail").GetRawText());
            Assert.Equal("pylon_broken", detail.Governing);
            Assert.True(detail.Variants.Single(v => v.Id == "closed").Trace.Count > 10);
            var scene = PunchingSceneJson.Read(root.GetProperty("scene"));
            Assert.Single(scene.Variants, v => v.Governs && v.Id == "pylon_broken");
            Assert.False(root.GetProperty("inputs").TryGetProperty("F", out _));
            Assert.Equal(2, root.GetProperty("verdict_summary").GetProperty("governing_num").GetInt32());
        }
        finally { Delete(path); }
    }

    [Fact]
    public void Errors_NoSet_EmptySet_InvalidInput()
    {
        var (noSet, p1) = RunBatch(Gosha());
        Delete(p1);
        Assert.Equal("error", noSet.Status);
        Assert.Contains("force_set_missing", noSet.DataJson);

        var (empty, p2) = RunBatch(Gosha(), new ForceSet { Id = 3, Kind = "bar", Tag = "пусто" });
        Delete(p2);
        Assert.Contains("force_set_empty", empty.DataJson);

        var bad = Gosha() with { Slab = new PunchingSlabParams { H = 0.2, Ax = 0.3, Ay = 0.03, RbtOverride = 1050 } };
        var (invalid, p3) = RunBatch(bad, Set());
        Delete(p3);
        Assert.Equal("error", invalid.Status);
        Assert.Contains("h0_nonpositive", invalid.DataJson);
    }

    [Fact]
    public void DominantSign_ByAbsoluteSum()
    {
        Assert.Equal(-1, PunchingBatchRunner.DominantSign(Set().Items));
        Assert.Equal(1, PunchingBatchRunner.DominantSign([new LoadItem { N = 50 }, new LoadItem { N = -10 }]));
        Assert.Equal(1, PunchingBatchRunner.DominantSign([]));
    }

    [Fact]
    public void RowTaskFactory_PunchingRow_ForcesInParams()
    {
        var batch = Batch(Gosha());
        var single = BatchRowTaskFactory.Create(batch, 2, batch.Tag, [Set()], []);
        Assert.Equal(PunchingRunner.Kind, single.Kind);
        Assert.Equal(0, single.SectionId);
        Assert.Equal(32, single.ForceItemId);
        Assert.Equal("Узел К-1 — эталон", single.Tag);
        var p = PunchingTaskParams.Parse(single.ParamsJson);
        Assert.Equal(600, p.Forces.F);
        Assert.Equal(30, p.Forces.Mx);
        Assert.Equal(20, p.Forces.My);
        Assert.Equal(0.25, p.Area.Bx);

        single.Id = 50;
        Assert.Same(single, BatchRowTaskFactory.FindExisting([single], batch, 2));
        Assert.Null(BatchRowTaskFactory.FindExisting([single], batch, 1));
    }

    [Fact]
    public async Task Cli_RunTask_ReturnsBatchData()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"opencs_punch_batch_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string project = Path.Combine(dir, "Проект.db");
        try
        {
            TestProjectDb.Create(project);
            int taskId;
            using (var db = new DatabaseService(project))
            {
                db.LoadAll();
                var set = new ForceSet { Kind = "bar", Tag = "РСУ К-1", Items = Set().Items.Select(i => new LoadItem
                {
                    Num = i.Num, Label = i.Label, N = i.N, Mx = i.Mx, My = i.My
                }).ToList() };
                db.SaveForceSet(set);
                var task = new CalcTask { Num = 5, Tag = "Узел К-1", Kind = PunchingBatchRunner.Kind, CalcType = CalcType.C,
                    ForceSetId = set.Id, ParamsJson = Gosha().ToJson() };
                db.SaveCalcTask(task);
                taskId = task.Id;
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            var stdout = new StringWriter();
            int code = await CliApp.RunAsync(["run", "task", project, taskId.ToString(), "--json", "--with-data", "-q"], stdout, new StringWriter());
            Assert.Equal(CliExitCode.Ok, code);
            using var json = JsonDocument.Parse(stdout.ToString());
            var item = Assert.Single(json.RootElement.EnumerateArray().ToList());
            Assert.Equal("not_passed", item.GetProperty("status").GetString());
            var data = item.GetProperty("data");
            Assert.Equal(2, data.GetProperty("governing_num").GetInt32());
            Assert.Equal(4, data.GetProperty("rows").GetArrayLength());
        }
        finally
        {
            Loc.SetLanguage(null);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
