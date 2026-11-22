using System.IO;
using System.Text.Json.Nodes;
using OpenCS.Cli;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Консоль: документ opencs-model — выгрузка, применение, проверка до записи, пробный прогон.</summary>
[Collection("Loc language")]
public sealed class CliModelTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_cli_model_{Guid.NewGuid():N}");
    public CliModelTests() => Directory.CreateDirectory(_dir);
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

    async Task<string> NewProject(string name = "p.db")
    {
        string project = PathOf(name);
        Assert.Equal(CliExitCode.Ok, (await Run("new", project)).Code);
        return project;
    }

    [Fact]
    public async Task Apply_WrongFormatOrVersion_Usage()
    {
        string project = await NewProject();
        var bad = await Run("apply", project, Doc("a.json", """{"format":"x","version":1}"""));
        Assert.Equal(CliExitCode.Usage, bad.Code);
        Assert.Contains("format", bad.Err);
        var ver = await Run("apply", project, Doc("b.json", """{"format":"opencs-model","version":99}"""));
        Assert.Equal(CliExitCode.Usage, ver.Code);
        Assert.Contains("version", ver.Err);
    }

    [Fact]
    public async Task Apply_UnknownSectionOrBadJson_Usage()
    {
        string project = await NewProject();
        var unknown = await Run("apply", project, Doc("a.json", """{"format":"opencs-model","version":1,"foo":[]}"""));
        Assert.Equal(CliExitCode.Usage, unknown.Code);
        Assert.Contains("foo", unknown.Err);
        var broken = await Run("apply", project, Doc("b.json", "{ not json"));
        Assert.Equal(CliExitCode.Usage, broken.Code);
    }

    [Fact]
    public async Task ExportModel_EmptyProject_Header()
    {
        string project = await NewProject();
        string to = PathOf("out.json");
        var r = await Run("export", "model", project, "--to", to);
        Assert.True(r.Code == CliExitCode.Ok, r.Err);
        var doc = JsonNode.Parse(File.ReadAllText(to))!.AsObject();
        Assert.Equal("opencs-model", (string?)doc["format"]);
        Assert.Equal(1, (int?)doc["version"]);
    }

    [Fact]
    public async Task ExportModel_UnknownOnly_Usage()
    {
        string project = await NewProject();
        var r = await Run("export", "model", project, "--to", PathOf("o.json"), "--only", "nothing");
        Assert.Equal(CliExitCode.Usage, r.Code);
    }

    [Fact]
    public async Task Apply_EmptyDocument_Ok()
    {
        string project = await NewProject();
        var r = await Run("apply", project, Doc("a.json", """{"format":"opencs-model","version":1}"""));
        Assert.True(r.Code == CliExitCode.Ok, r.Err);
    }

    static List<CScore.Material> Materials(string project)
    {
        using var db = new DatabaseService(project);
        db.LoadAll();
        var list = db.Materials.ToList();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        return list;
    }

    [Fact]
    public async Task Apply_Catalog_CreatesMaterials_ThenUpdatesInPlace()
    {
        string project = await NewProject();
        string doc = Doc("m.json", """
            { "format": "opencs-model", "version": 1, "materials": [
              { "catalog": { "concrete": "B25" } },
              { "tag": "B30 бетонная", "catalog": { "concrete": "B30", "plain": true } },
              { "catalog": { "rebar": "A500C" } },
              { "catalog": { "steel": "С255" } } ] }
            """);
        var r = await Run("apply", project, doc, "--json");
        Assert.True(r.Code == CliExitCode.Ok, r.Err);
        Assert.Equal(4, JsonNode.Parse(r.Out)!["changes"]!.AsArray().Count);

        var created = Materials(project);
        Assert.Equal(4, created.Count);
        Assert.Contains("γb2 = 0,9", created.Single(m => m.Tag == "B30 бетонная").Description);
        int idBefore = created.Single(m => m.Tag == "B25").Id;

        var again = await Run("apply", project, Doc("u.json", """
            { "format": "opencs-model", "version": 1, "materials": [
              { "tag": "B25", "catalog": { "concrete": "B25", "tallLayer": true } } ] }
            """));
        Assert.True(again.Code == CliExitCode.Ok, again.Err);
        var updated = Materials(project);
        Assert.Equal(4, updated.Count);
        var b25 = updated.Single(m => m.Tag == "B25");
        Assert.Equal(idBefore, b25.Id);
        Assert.Contains("γb3 = 0,85", b25.Description);
    }

    [Fact]
    public async Task Apply_Errors_AllReportedWithPaths_NothingWritten()
    {
        string project = await NewProject();
        var r = await Run("apply", project, Doc("bad.json", """
            { "format": "opencs-model", "version": 1, "materials": [
              { "catalog": { "concrete": "B999" } },
              { "tag": "X", "type": "concrete", "chars": { "C": { "e": 30, "fc": 14.5 } } },
              { "tag": "Y", "type": "custom" },
              { "catalog": { "rebar": "A500C" }, "type": "concrete" },
              { "catalog": { "concrete": "B25", "kind": "light" } },
              { "tag": "Z", "type": "steel", "chars": { "N": { "e": "много" } } } ] }
            """));
        Assert.Equal(CliExitCode.Usage, r.Code);
        Assert.Contains("materials[0].catalog.concrete", r.Err);
        Assert.Contains("materials[1].chars.CL", r.Err);
        Assert.Contains("materials[2].type", r.Err);
        Assert.Contains("materials[3].type", r.Err);
        Assert.Contains("materials[4].catalog.kind", r.Err);
        Assert.Contains("materials[5].chars.N", r.Err);
        Assert.Empty(Materials(project));
    }

    [Fact]
    public async Task Apply_DuplicateTagInDocument_Error()
    {
        string project = await NewProject();
        var r = await Run("apply", project, Doc("d.json", """
            { "format": "opencs-model", "version": 1, "materials": [
              { "catalog": { "concrete": "B25" } }, { "catalog": { "concrete": "B25" } } ] }
            """));
        Assert.Equal(CliExitCode.Usage, r.Code);
        Assert.Contains("materials[1].tag", r.Err);
    }

    [Fact]
    public async Task Apply_DryRun_WritesNothing()
    {
        string project = await NewProject();
        var r = await Run("apply", project, Doc("m.json", """
            { "format": "opencs-model", "version": 1, "materials": [ { "catalog": { "concrete": "B25" } } ] }
            """), "--dry-run");
        Assert.True(r.Code == CliExitCode.Ok, r.Err);
        Assert.Contains("B25", r.Out);
        Assert.Empty(Materials(project));
    }

    [Fact]
    public async Task Materials_RoundTrip_ExportApplyExport_Same()
    {
        string first = await NewProject("a.db");
        var seed = await Run("apply", first, Doc("seed.json", """
            { "format": "opencs-model", "version": 1, "materials": [
              { "catalog": { "concrete": "B25", "dampness": "below40" } },
              { "catalog": { "concrete": "B20", "kind": "fineA", "heatTreated": true } },
              { "catalog": { "rebar": "A500C" } },
              { "catalog": { "steel": "С255", "thicknessMm": 12, "shaped": true } } ] }
            """));
        Assert.True(seed.Code == CliExitCode.Ok, seed.Err);

        string exported = PathOf("e1.json");
        Assert.Equal(CliExitCode.Ok, (await Run("export", "model", first, "--to", exported)).Code);

        string second = await NewProject("b.db");
        var apply = await Run("apply", second, exported);
        Assert.True(apply.Code == CliExitCode.Ok, apply.Err);
        string exported2 = PathOf("e2.json");
        Assert.Equal(CliExitCode.Ok, (await Run("export", "model", second, "--to", exported2)).Code);

        var a = JsonNode.Parse(File.ReadAllText(exported))!;
        var b = JsonNode.Parse(File.ReadAllText(exported2))!;
        Assert.Equal(4, a["materials"]!.AsArray().Count);
        AssertJsonClose(a, b, "$");
        // Нулевая деформация арматуры (ec1 = 0 в модели, множитель −1) не выгружается как «-0».
        Assert.DoesNotContain("-0,", File.ReadAllText(exported).Replace(" ", ""));
    }

    [Fact]
    public async Task Schema_Materials_IsJson()
    {
        var r = await Run("schema", "materials");
        Assert.True(r.Code == CliExitCode.Ok, r.Err);
        Assert.NotNull(JsonNode.Parse(r.Out)!["properties"]);
    }

    /// <summary>JSON совпадает; числа — с относительным допуском 1e-12 (пересчёт кПа ↔ МПа).</summary>
    static void AssertJsonClose(JsonNode? a, JsonNode? b, string path)
    {
        switch (a)
        {
            case JsonObject oa:
                var ob = Assert.IsType<JsonObject>(b);
                Assert.True(oa.Select(p => p.Key).Order().SequenceEqual(ob.Select(p => p.Key).Order()), path);
                foreach (var (k, v) in oa) AssertJsonClose(v, ob[k], $"{path}.{k}");
                break;
            case JsonArray aa:
                var ab = Assert.IsType<JsonArray>(b);
                Assert.Equal(aa.Count, ab.Count);
                for (int i = 0; i < aa.Count; i++) AssertJsonClose(aa[i], ab[i], $"{path}[{i}]");
                break;
            case JsonValue va when va.TryGetValue(out double da):
                double db = b!.GetValue<double>();
                Assert.True(Math.Abs(da - db) <= 1e-12 * Math.Max(1, Math.Abs(da)), $"{path}: {da} ≠ {db}");
                break;
            default:
                Assert.Equal(a?.ToJsonString(), b?.ToJsonString());
                break;
        }
    }
}
