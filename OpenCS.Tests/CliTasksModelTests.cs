using System.IO;
using System.Text.Json.Nodes;
using CScore;
using OpenCS.Cli;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Консоль: вид «tasks» документа opencs-model — ключи сечения, набора и строки, Id внутри параметров по ключам,
/// кругооборот, ошибки с путём без записи, обновление на месте с удалением устаревших результатов; add tasks через службу.
/// </summary>
[Collection("Loc language")]
public sealed class CliTasksModelTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_cli_tasks_{Guid.NewGuid():N}");
    public CliTasksModelTests()
    {
        Directory.CreateDirectory(_dir);
        Loc.SetLanguage("ru-RU");
    }
    public void Dispose()
    {
        Loc.SetLanguage(null);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    static async Task<(int Code, string Out, string Err)> Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = await CliApp.RunAsync(args, stdout, stderr);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        return (code, stdout.ToString(), stderr.ToString());
    }

    string PathOf(string name) => Path.Combine(_dir, name);

    string Doc(string name, string json)
    {
        string path = PathOf(name);
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>Проект с сечением «К1» и сечением пластины «Плита» (без материалов: задачам нужны только ключи).</summary>
    async Task<string> Project(string name)
    {
        string project = PathOf(name);
        Assert.Equal(CliExitCode.Ok, (await Run("new", project)).Code);
        using (var db = new DatabaseService(project))
        {
            db.LoadAll();
            db.SaveCrossSection(new CrossSection { Tag = "К1" });
            db.SavePlateSection(new PlateSection { Tag = "Плита", H = 0.2 });
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        return project;
    }

    static (List<CalcTask> Tasks, List<ForceSet> Sets, List<CalcResult> Results, List<CrossSection> Sections) Load(string project)
    {
        using var db = new DatabaseService(project);
        db.LoadAll();
        foreach (var fs in db.ForceSets) fs.EnsureRows();
        var r = (db.CalcTasks.ToList(), db.ForceSets.ToList(), db.CalcResults.ToList(), db.CrossSections.ToList());
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        return r;
    }

    const string Seed = """
        { "format": "opencs-model", "version": 1,
          "forceSets": [
            { "tag": "G", "kind": "bar", "columns": ["label", "N", "Mx"], "rows": [ ["1", -100, 10], ["2", -200, 20], ["3", -300, 30] ] },
            { "tag": "РСУ плиты", "kind": "shell", "columns": ["label", "Mx", "My"], "rows": [ ["1", 5, 1], ["2", 6, 2] ] } ],
          "tasks": [
            { "tag": "НДС", "kind": "strain_state", "section": "К1",
              "params": { "solver": "bisection", "N": -100, "Mx": 12.5, "My": 0, "etaEnabled": true, "etaL": 3.6 } },
            { "tag": "Строка пакета", "kind": "limit_force", "section": "К1",
              "params": { "solver": "fast", "N": -200, "Mx": 20, "My": 0, "batchSource": { "task": "Пакет", "num": 2 } } },
            { "tag": "Пакет", "kind": "limit_force_batch", "section": "К1", "forceSet": "G", "params": { "solver": "fast" } },
            { "tag": "Строка", "kind": "limit_force", "section": "К1", "forceSet": "G", "row": 2 },
            { "tag": "Трещины", "kind": "crack_width", "calcType": "N", "section": "К1",
              "params": { "N": -100, "Mx": 10, "My": 0, "AcrcUltLong": 0.3, "AcrcUltShort": 0.4, "Phi2": 0.5,
                          "ForcesMode": "force_item_long", "LongShare": 1, "LongPartUseNL": true,
                          "ForceSetLong": "G", "ForceItemLong": 3 } },
            { "tag": "Плита", "kind": "shell_simpl_wa_uls_batch", "section": "Плита", "forceSet": "РСУ плиты",
              "params": { "step_deg": 15, "acrc_lim_mm": 0.3, "phi1": 1, "phi2": 0.5, "auto_stress_to_force": true } }
          ] }
        """;

    [Fact]
    public async Task Tasks_Apply_RefsResolved_RoundTripSame()
    {
        string first = await Project("a.db");
        var dry = await Run("apply", first, Doc("seed.json", Seed), "--dry-run");
        Assert.True(dry.Code == CliExitCode.Ok, dry.Err);
        Assert.Empty(Load(first).Tasks);

        var r = await Run("apply", first, PathOf("seed.json"));
        Assert.True(r.Code == CliExitCode.Ok, r.Err);
        var (tasks, sets, _, sections) = Load(first);
        Assert.Equal(6, tasks.Count);
        Assert.Equal(tasks.Count, tasks.Select(t => t.Num).Distinct().Count());
        var g = sets.Single(s => s.Tag == "G");
        var k1 = sections.Single(s => s.Tag == "К1");
        var row = tasks.Single(t => t.Tag == "Строка");
        Assert.Equal(k1.Id, row.SectionId);
        Assert.Equal(g.Id, row.ForceSetId);
        Assert.Equal(g.Items[1].Id, row.ForceItemId);
        var crack = tasks.Single(t => t.Tag == "Трещины");
        Assert.Equal(CalcType.N, crack.CalcType);
        var cw = CrackWidthTaskParams.Parse(crack.ParamsJson);
        Assert.Equal(g.Id, cw.ForceSetLongId);
        Assert.Equal(g.Items[2].Id, cw.ForceItemLongId);
        var batch = tasks.Single(t => t.Tag == "Пакет");
        var single = JsonNode.Parse(tasks.Single(t => t.Tag == "Строка пакета").ParamsJson)!;
        Assert.Equal(batch.Id, (int)single["batchSource"]!["taskId"]!);
        Assert.Equal(3.6, LimitForceParams.Parse(tasks.Single(t => t.Tag == "НДС").ParamsJson).EtaL);
        var plate = tasks.Single(t => t.Tag == "Плита");
        Assert.Equal(sets.Single(s => s.Kind == "shell").Id, plate.ForceSetId);
        Assert.Equal(15, OpenCS.Tasks.ShellSimplParams.Parse(plate.ParamsJson).StepDeg);

        string e1 = PathOf("e1.json");
        Assert.Equal(CliExitCode.Ok, (await Run("export", "model", first, "--to", e1)).Code);
        string second = await Project("b.db");
        var applied = await Run("apply", second, e1);
        Assert.True(applied.Code == CliExitCode.Ok, applied.Err);
        string e2 = PathOf("e2.json");
        Assert.Equal(CliExitCode.Ok, (await Run("export", "model", second, "--to", e2)).Code);
        var a = JsonNode.Parse(File.ReadAllText(e1))!["tasks"]!;
        var b = JsonNode.Parse(File.ReadAllText(e2))!["tasks"]!;
        Assert.Equal(6, a.AsArray().Count);
        Assert.True(JsonNode.DeepEquals(a, b), $"{a.ToJsonString()}\n{b.ToJsonString()}");
        var crackDoc = a.AsArray().Single(t => (string?)t!["tag"] == "Трещины")!;
        Assert.Equal("G", (string?)crackDoc["params"]!["ForceSetLong"]);
        Assert.Equal(3, (int)crackDoc["params"]!["ForceItemLong"]!);
        Assert.Null(crackDoc["params"]!["ForceSetLongId"]);
    }

    [Fact]
    public async Task Tasks_Errors_WithPaths_NothingWritten()
    {
        string project = await Project("e.db");
        var r = await Run("apply", project, Doc("bad.json", """
            { "format": "opencs-model", "version": 1,
              "forceSets": [ { "tag": "G", "kind": "bar", "columns": ["label", "N"], "rows": [ ["1", -1] ] } ],
              "tasks": [
                { "tag": "А", "kind": "nope", "section": "К1" },
                { "tag": "Б", "kind": "strain_state_batch", "section": "К1" },
                { "tag": "В", "kind": "shell_simpl_wa_uls_batch", "section": "Плита", "forceSet": "G" },
                { "tag": "Г", "kind": "limit_force", "section": "К1", "forceSet": "G", "row": 9 },
                { "tag": "Д", "kind": "strain_state", "section": "К1", "params": { "N": "12,5" } },
                { "tag": "Е", "kind": "strain_state", "section": "К1", "params": { "etaL2": 1 } },
                { "tag": "Ж", "kind": "strain_state", "section": "Нет такого" },
                { "tag": "З", "kind": "fire_r_check", "section": "К1" },
                { "tag": "И", "kind": "cracking", "calcType": "C", "section": "К1" },
                { "tag": "К", "kind": "strain_state", "section": "К1", "lable": 1 }
              ] }
            """));
        Assert.Equal(CliExitCode.Usage, r.Code);
        foreach (var path in new[] { "tasks[0].kind", "tasks[1].forceSet", "tasks[2].forceSet", "tasks[3].row",
                     "tasks[4].params", "tasks[5].params.etaL2", "tasks[6].section", "tasks[7].kind", "tasks[8].calcType", "tasks[9].lable" })
            Assert.Contains(path, r.Err);
        var (tasks, sets, _, _) = Load(project);
        Assert.Empty(tasks);
        Assert.Empty(sets);
    }

    [Fact]
    public async Task Tasks_Update_KeepsId_DeletesStaleResults()
    {
        string project = await Project("u.db");
        Assert.True((await Run("apply", project, Doc("seed.json", Seed))).Code == CliExitCode.Ok);
        var before = Load(project).Tasks.Single(t => t.Tag == "НДС");
        using (var db = new DatabaseService(project))
        {
            db.LoadAll();
            db.SaveCalcResult(new CalcResult { TaskId = before.Id, TaskKind = before.Kind, TaskTag = before.Tag, Created = "2026-11-24 12:00:00" });
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // Тот же документ — входные данные не изменились, результаты целы.
        Assert.True((await Run("apply", project, PathOf("seed.json"))).Code == CliExitCode.Ok);
        Assert.Single(Load(project).Results, x => x.TaskId == before.Id);

        var u = await Run("apply", project, Doc("u.json", """
            { "format": "opencs-model", "version": 1,
              "tasks": [ { "tag": "НДС", "kind": "strain_state", "section": "К1", "params": { "N": -500, "Mx": 1, "My": 0 } } ] }
            """));
        Assert.True(u.Code == CliExitCode.Ok, u.Err);
        var (tasks, _, results, _) = Load(project);
        var after = tasks.Single(t => t.Tag == "НДС");
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.Num, after.Num);
        Assert.Equal(-500, LimitForceParams.Parse(after.ParamsJson).N);
        Assert.DoesNotContain(results, x => x.TaskId == before.Id);
        Assert.Contains("НДС", u.Err);
    }

    [Fact]
    public async Task AddTasks_DefaultCalcTypeByKind_ValidatedByService()
    {
        string project = await Project("add.db");
        var crack = await Run("add", "tasks", project, "--kind", "cracking", "--section", "К1", "--tag", "Трещ");
        Assert.True(crack.Code == CliExitCode.Ok, crack.Err);
        Assert.Equal(CalcType.N, Load(project).Tasks.Single(t => t.Tag == "Трещ").CalcType);

        var bad = await Run("add", "tasks", project, "--kind", "strain_state", "--section", "К1", "--param", "etaL2=1");
        Assert.Equal(CliExitCode.Usage, bad.Code);
        Assert.Contains("etaL2", bad.Err);
        Assert.Single(Load(project).Tasks);
    }
}
