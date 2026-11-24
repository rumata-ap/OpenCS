using System.IO;
using System.Text.Json.Nodes;
using OpenCS.Cli;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Консоль: вид «fireSections» и огневые задачи в «tasks» — ключ огневого сечения, кругооборот, ошибки с путём без
/// записи, обновление на месте (Id и тепловой расчёт сохраняются, устаревание — предупреждением), смена сечения
/// переводит задачи; <c>run fire</c> и <c>list fire</c>.
/// </summary>
[Collection("Loc language")]
public sealed class CliFireModelTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_cli_fire_{Guid.NewGuid():N}");
    public CliFireModelTests()
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

    async Task<string> NewProject(string name)
    {
        string project = PathOf(name);
        Assert.Equal(CliExitCode.Ok, (await Run("new", project)).Code);
        return project;
    }

    const string Seed = """
        { "format": "opencs-model", "version": 1,
          "materials": [ { "tag": "B25", "catalog": { "concrete": "B25" } }, { "tag": "A500", "catalog": { "rebar": "A500" } } ],
          "sections": [
            { "tag": "Б-1", "areas": [
              { "tag": "Бетон", "category": "region", "material": "B25", "diagram": "sp63",
                "polygon": { "outer": [[-100, -200], [100, -200], [100, 200], [-100, 200]] } },
              { "tag": "Низ", "category": "rebarGroup", "material": "A500", "diagram": "l2", "host": "Бетон",
                "bars": [[-60, -160, 16], [60, -160, 16]] } ] },
            { "tag": "Б-2", "areas": [
              { "tag": "Бетон", "category": "region", "material": "B25", "diagram": "sp63",
                "polygon": { "outer": [[-150, -200], [150, -200], [150, 200], [-150, 200]] } } ] } ],
          "fireSections": [
            { "tag": "Б-1 R60", "section": "Б-1",
              "fire": { "durationMin": 1, "curve": "iso834", "aggregate": "carbonate" },
              "mesh": { "stepMm": 20, "algorithm": "ruppert", "smoothing": 2 },
              "time": { "stepS": 30, "snapshotStepMin": 0.5 },
              "bc": { "preset": "3-sided", "holes": "ambient",
                      "edges": [ { "contour": "outer", "edge": 2, "type": "ambient" } ] } },
            { "tag": "Б-1 R30", "section": "Б-1", "fire": { "durationMin": 30 } } ],
          "forceSets": [ { "tag": "РСУ", "kind": "bar", "columns": ["label", "N", "Mx"], "rows": [ ["1", -100, -10] ] } ],
          "tasks": [
            { "tag": "Кривизна", "kind": "fire_thermal_curvature", "fireSection": "Б-1 R60",
              "params": { "normalized_limit_min": 60, "tension_rebar_at_heated_face": true } },
            { "tag": "R-проверка", "kind": "fire_r_check", "fireSection": "Б-1 R60", "forceSet": "РСУ", "row": 1,
              "params": { "method": "fiber", "snapshot_time_min": 1 } },
            { "tag": "R-пакет", "kind": "fire_r_check_batch", "fireSection": "Б-1 R30", "forceSet": "РСУ" },
            { "tag": "R(t)", "kind": "fire_r_time", "fireSection": "Б-1 R30", "forceSet": "РСУ", "row": 1 } ] }
        """;

    static JsonNode Part(string file, string kind) => JsonNode.Parse(File.ReadAllText(file))![kind]!;

    [Fact]
    public async Task Fire_Apply_RoundTrip_TasksLinked()
    {
        string first = await NewProject("a.db");
        var r = await Run("apply", first, Doc("seed.json", Seed));
        Assert.True(r.Code == CliExitCode.Ok, r.Err);

        using (var db = new DatabaseService(first))
        {
            db.LoadAll();
            var f = db.FireSections.Single(x => x.Tag == "Б-1 R60");
            Assert.Equal(0.02, f.MeshStepM, 12);
            Assert.Equal("carbonate", f.AggregateType);
            Assert.Single(f.Edges);
            Assert.Equal(9, f.Edges[0].AlphaConv);
            var section = db.CrossSections.Single(s => s.Tag == "Б-1");
            foreach (var t in db.CalcTasks)
            {
                Assert.Equal(section.Id, t.SectionId);
                Assert.True(FireSectionAuthoring.FireSectionIdOf(t) > 0, t.Tag);
                Assert.Empty(CalcTaskAuthoring.Validate(db, t));
            }
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        string e1 = PathOf("e1.json"), e2 = PathOf("e2.json");
        var ex = await Run("export", "model", first, "--to", e1);
        Assert.True(ex.Code == CliExitCode.Ok, ex.Err);
        Assert.DoesNotContain("fire_section_id", File.ReadAllText(e1));
        string second = await NewProject("b.db");
        var applied = await Run("apply", second, e1);
        Assert.True(applied.Code == CliExitCode.Ok, applied.Err);
        Assert.Equal(CliExitCode.Ok, (await Run("export", "model", second, "--to", e2)).Code);
        Assert.True(JsonNode.DeepEquals(Part(e1, "fireSections"), Part(e2, "fireSections")), File.ReadAllText(e2));
        Assert.True(JsonNode.DeepEquals(Part(e1, "tasks"), Part(e2, "tasks")), File.ReadAllText(e2));
        Assert.Equal("Б-1 R60", (string)Part(e1, "tasks")[0]!["fireSection"]!);
        Assert.Null(Part(e1, "tasks")[0]!["section"]);
    }

    [Fact]
    public async Task Fire_Errors_WithPaths_NothingWritten()
    {
        string project = await NewProject("e.db");
        Assert.True((await Run("apply", project, Doc("seed.json", Seed))).Code == CliExitCode.Ok);
        var r = await Run("apply", project, Doc("bad.json", """
            { "format": "opencs-model", "version": 1,
              "fireSections": [
                { "tag": "Нет сечения", "section": "Нет" },
                { "tag": "Ребро", "section": "Б-1", "bc": { "edges": [ { "edge": 7, "type": "fire" } ] } },
                { "tag": "Поле", "section": "Б-1", "mesh": { "step": 10 } },
                { "tag": "Длит", "section": "Б-1", "fire": { "durationMin": 0 } } ],
              "tasks": [
                { "tag": "Т1", "kind": "fire_thermal_curvature", "fireSection": "Б-1 R60", "section": "Б-1" },
                { "tag": "Т2", "kind": "fire_r_check", "fireSection": "Б-1 R60", "forceSet": "РСУ", "row": 1,
                  "params": { "snapshot_time_min": 90 } },
                { "tag": "Т3", "kind": "fire_thermal_curvature", "fireSection": "Нет такого" },
                { "tag": "Т4", "kind": "strain_state", "section": "Б-1", "fireSection": "Б-1 R60" },
                { "tag": "Т5", "kind": "fire_thermal_curvature", "fireSection": "Б-1 R60", "params": { "fire_section_id": 1 } } ] }
            """));
        Assert.Equal(CliExitCode.Usage, r.Code);
        foreach (var path in new[] { "fireSections[0].section", "fireSections[1].bc.edges[0].edge", "fireSections[2].mesh.step",
                     "fireSections[3].fire.durationMin", "tasks[0].section", "tasks[1].params.snapshot_time_min",
                     "tasks[2].fireSection", "tasks[3].fireSection", "tasks[4].params.fire_section_id" })
            Assert.Contains(path, r.Err);
        using var db = new DatabaseService(project);
        db.LoadAll();
        Assert.Equal(2, db.FireSections.Count);
        Assert.Equal(4, db.CalcTasks.Count);
    }

    [Fact]
    public async Task Fire_RunThermal_Update_StaleWarned_SectionChangeMovesTasks()
    {
        string project = await NewProject("u.db");
        Assert.True((await Run("apply", project, Doc("seed.json", Seed))).Code == CliExitCode.Ok);
        int fireId;
        using (var db = new DatabaseService(project))
        {
            db.LoadAll();
            fireId = db.FireSections.Single(x => x.Tag == "Б-1 R60").Id;
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var run = await Run("run", "fire", project, fireId.ToString(), "--in-place", "-q");
        Assert.True(run.Code == CliExitCode.Ok, run.Out + run.Err);
        var list = await Run("list", "fire", project, "--json");
        var row = JsonNode.Parse(list.Out)!.AsArray().Single(x => (int)x!["id"]! == fireId)!;
        Assert.Equal("current", (string)row["thermal"]!);

        // Огневая задача на актуальном поле считается.
        var task = await Run("run", "task", project, "--all", "--in-place", "--json", "-q");
        var curvature = JsonNode.Parse(task.Out)!.AsArray().Single(x => (string)x!["tag"]! == "Кривизна")!;
        Assert.NotEqual("not_run", (string)curvature["status"]!);

        // Длительность изменена — расчёт остаётся, но устарел; Id огневого сечения прежний.
        string changed = Seed.Replace("\"durationMin\": 1,", "\"durationMin\": 2,");
        var upd = await Run("apply", project, Doc("upd.json", changed));
        Assert.True(upd.Code == CliExitCode.Ok, upd.Err);
        Assert.Contains("устарел", upd.Err);
        list = await Run("list", "fire", project, "--json");
        row = JsonNode.Parse(list.Out)!.AsArray().Single(x => (int)x!["id"]! == fireId)!;
        Assert.Equal("stale", (string)row["thermal"]!);

        // Смена сечения: задачи огневого сечения переходят на новое.
        string moved = changed.Replace("{ \"tag\": \"Б-1 R30\", \"section\": \"Б-1\"", "{ \"tag\": \"Б-1 R30\", \"section\": \"Б-2\"");
        var mv = await Run("apply", project, Doc("mv.json", moved));
        Assert.True(mv.Code == CliExitCode.Ok, mv.Err);
        using var db2 = new DatabaseService(project);
        db2.LoadAll();
        int b2 = db2.CrossSections.Single(s => s.Tag == "Б-2").Id;
        Assert.Equal(b2, db2.FireSections.Single(f => f.Tag == "Б-1 R30").SectionId);
        Assert.All(db2.CalcTasks.Where(t => t.Tag is "R-пакет" or "R(t)"), t => Assert.Equal(b2, t.SectionId));
        Assert.Equal(fireId, db2.FireSections.Single(f => f.Tag == "Б-1 R60").Id);
    }
}
