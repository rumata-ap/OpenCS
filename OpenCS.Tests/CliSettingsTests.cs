using System.IO;
using System.Text.Json.Nodes;
using OpenCS.Cli;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Вид settings документа opencs-model: выгрузка, частичное применение, ошибки с путём, пересчёт «авто».</summary>
[Collection("Loc language")]
public sealed class CliSettingsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_cli_settings_{Guid.NewGuid():N}");

    public CliSettingsTests()
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
        File.WriteAllText(PathOf(name), json);
        return PathOf(name);
    }

    async Task<string> NewProject(string name)
    {
        string project = PathOf(name);
        Assert.Equal(CliExitCode.Ok, (await Run("new", project)).Code);
        return project;
    }

    async Task<JsonObject> ExportSettings(string project)
    {
        string to = PathOf($"export-{Guid.NewGuid():N}.json");
        var r = await Run("export", "model", project, "--to", to, "--only", "settings");
        Assert.True(r.Code == CliExitCode.Ok, r.Err);
        return (JsonObject)JsonNode.Parse(File.ReadAllText(to))!["settings"]!;
    }

    static CalcSettings Calc(string project)
    {
        using var db = new DatabaseService(project);
        return db.LoadCalcSettings();
    }

    [Fact]
    public async Task Export_IsObject_WithAllGroups_AndRoundTripsToNewProject()
    {
        string a = await NewProject("a.db");
        var r = await Run("apply", a, Doc("s.json", """
            { "format": "opencs-model", "version": 1,
              "settings": { "loads": { "valueKind": "design", "gammaF": { "permanent": 1.15 } },
                            "mesh": { "elements": "quads", "seamToleranceMm": 0.5 },
                            "openSees": { "geomTransf": "pDelta", "fiberStatesIntegrationPoints": [1, 5] },
                            "import": { "autocad": { "units": "m" } } } }
            """));
        Assert.True(r.Code == CliExitCode.Ok, r.Err + r.Out);

        var exported = await ExportSettings(a);
        foreach (var group in new[] { "sections", "loads", "import", "mesh", "csfea", "openSees", "submodels" })
            Assert.NotNull(exported[group]);
        Assert.Equal("design", (string)exported["loads"]!["valueKind"]!);
        Assert.Equal(1.15, (double)exported["loads"]!["gammaF"]!["permanent"]!);
        Assert.Equal("pDelta", (string)exported["openSees"]!["geomTransf"]!);
        Assert.Equal("[1,5]", exported["openSees"]!["fiberStatesIntegrationPoints"]!.ToJsonString());
        Assert.Null(exported["gui"]); // таймауты и потоки — только окно

        string b = await NewProject("b.db");
        var doc = new JsonObject { ["format"] = "opencs-model", ["version"] = 1, ["settings"] = exported.DeepClone() };
        r = await Run("apply", b, Doc("rt.json", doc.ToJsonString()));
        Assert.True(r.Code == CliExitCode.Ok, r.Err + r.Out);
        Assert.Equal(exported.ToJsonString(), (await ExportSettings(b)).ToJsonString());

        // Повтор того же документа — «без изменений».
        r = await Run("apply", b, PathOf("rt.json"));
        Assert.Contains(Loc.S("CliModelActionUnchanged"), r.Out);

        var calc = Calc(b);
        using (var gdb = new DatabaseService(b)) Assert.Equal(0.0005, gdb.LoadGmshSettings().SeamToleranceM, 12);
        Assert.Equal("PDelta", calc.OpenSeesGeomTransfKind);
    }

    [Fact]
    public async Task Partial_KeepsOtherFields()
    {
        string p = await NewProject("p.db");
        var before = Calc(p);
        var r = await Run("apply", p, Doc("one.json", """{ "format": "opencs-model", "version": 1, "settings": { "sections": { "gridDensity": 55 } } }"""));
        Assert.True(r.Code == CliExitCode.Ok, r.Err);
        var after = Calc(p);
        Assert.Equal(55, after.GridDensity);
        Assert.Equal(before.NewtonTolerance, after.NewtonTolerance);
        Assert.Equal(before.Sp20LoadValueKind, after.Sp20LoadValueKind);
    }

    [Fact]
    public async Task Errors_WithPaths_NothingWritten_DryRunWritesNothing()
    {
        string p = await NewProject("e.db");
        var r = await Run("apply", p, Doc("bad.json", """
            { "format": "opencs-model", "version": 1,
              "settings": { "sections": { "gridDensity": 50, "newton": { "tolerance": "x" } },
                            "loads": { "gammaF": { "permanent": 0 }, "valueKind": "nominal" },
                            "foo": 1, "mesh": { "algorithm": 7 } } }
            """));
        Assert.Equal(CliExitCode.Usage, r.Code);
        foreach (var path in new[] { "settings.sections.newton.tolerance", "settings.loads.gammaF.permanent", "settings.loads.valueKind",
                                     "settings.foo", "settings.mesh.algorithm" })
            Assert.Contains(path, r.Err);
        Assert.Equal(40, Calc(p).GridDensity);

        r = await Run("apply", p, Doc("arr.json", """{ "format": "opencs-model", "version": 1, "settings": [] }"""));
        Assert.Equal(CliExitCode.Usage, r.Code);

        r = await Run("apply", p, Doc("dry.json", """{ "format": "opencs-model", "version": 1, "settings": { "sections": { "gridDensity": 70 } } }"""), "--dry-run");
        Assert.True(r.Code == CliExitCode.Ok, r.Err);
        Assert.Equal(40, Calc(p).GridDensity);
    }

    [Fact]
    public async Task LoadValueKindChange_RecomputesAutoCombinations()
    {
        string p = await NewProject("c.db");
        var r = await Run("apply", p, Doc("seed.json", """
            { "format": "opencs-model", "version": 1,
              "materials": [ { "tag": "B25", "catalog": { "concrete": "B25" } } ],
              "sections": [ { "tag": "Б-1", "areas": [
                { "tag": "Бетон", "category": "region", "material": "B25", "diagram": "sp63",
                  "polygon": { "outer": [[-200, -300], [200, -300], [200, 300], [-200, 300]] } } ] } ],
              "femSchemas": [ { "tag": "Рама",
                "nodes": [ { "tag": 1, "x": 0, "y": 0, "z": 0, "fix": ["x", "y", "z", "ux", "uy", "uz"] }, { "tag": 2, "x": 0, "y": 0, "z": 3 } ],
                "members": [ { "tag": "К1", "nodes": [1, 2], "section": "Б-1" } ],
                "loadCases": [ { "tag": "Пост", "type": "permanent", "loads": [ { "node": 2, "f": [0, 0, -10] } ] } ],
                "combinations": [ { "tag": "РСН1", "type": "C", "terms": [ { "loadCase": "Пост", "role": "permanent" } ] } ] } ] }
            """));
        Assert.True(r.Code == CliExitCode.Ok, r.Err + r.Out);
        string Expression()
        {
            using var db = new DatabaseService(p);
            db.LoadAll();
            return db.GetFemLoadDefinitions(db.FemSchemas.Single().Id).Single(d => d.Tag == "РСН1").ExpressionJson;
        }
        string before = Expression();

        r = await Run("apply", p, Doc("design.json", """{ "format": "opencs-model", "version": 1, "settings": { "loads": { "valueKind": "design" } } }"""));
        Assert.True(r.Code == CliExitCode.Ok, r.Err + r.Out);
        Assert.Contains("Рама", r.Out + r.Err);
        Assert.NotEqual(before, Expression());
        Assert.Equal("design", Calc(p).Sp20LoadValueKind);
    }

    [Fact]
    public async Task Schema_Settings_IsGeneratedObject()
    {
        var r = await Run("schema", "settings");
        Assert.Equal(CliExitCode.Ok, r.Code);
        var schema = JsonNode.Parse(r.Out)!;
        Assert.Equal("object", (string)schema["type"]!);
        Assert.NotNull(schema["properties"]!["loads"]!["properties"]!["valueKind"]!["enum"]);
        Assert.Equal(0, (double)schema["properties"]!["loads"]!["properties"]!["gammaF"]!["properties"]!["permanent"]!["exclusiveMinimum"]!);
    }
}
