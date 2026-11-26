using System.IO;
using System.Text.Json.Nodes;
using CScore.Fem;
using OpenCS.Cli;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Подраздел 7а: постановки расчёта схем — вид <c>femAnalyses</c> документа opencs-model (все пять видов, ссылки по
/// тегам, кругооборот, неизменённое не перезаписывается) и служба <see cref="FemAnalysisAuthoring"/>.
/// </summary>
[Collection("Loc language")]
public sealed class CliFemAnalysesTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_cli_analyses_{Guid.NewGuid():N}");
    public CliFemAnalysesTests()
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

    /// <summary>Рама: колонны К1, К2, ригель Р1; загружения «Пост», «Снег», несовершенство; комбинации РСН1, Все.</summary>
    const string Schema = """
        "materials": [
          { "tag": "B25", "catalog": { "concrete": "B25" } },
          { "tag": "A500", "catalog": { "rebar": "A500" } } ],
        "sections": [ { "tag": "Б-1", "areas": [
          { "tag": "Бетон", "category": "region", "material": "B25", "diagram": "sp63",
            "polygon": { "outer": [[-200, -300], [200, -300], [200, 300], [-200, 300]] } } ] } ],
        "femSchemas": [
          { "tag": "Рама",
            "nodes": [
              { "tag": 1, "x": 0, "y": 0, "z": 0, "fix": ["x", "y", "z", "ux", "uy", "uz"] },
              { "tag": 2, "x": 0, "y": 0, "z": 3 },
              { "tag": 3, "x": 6, "y": 0, "z": 3 },
              { "tag": 4, "x": 6, "y": 0, "z": 0, "fix": ["x", "y", "z", "ux", "uy", "uz"] } NODES ],
            "members": [
              { "tag": "К1", "nodes": [1, 2], "section": "Б-1" },
              { "tag": "Р1", "nodes": [2, 3], "section": "Б-1" },
              { "tag": "К2", "nodes": [3, 4], "section": "Б-1" } ],
            "loadCases": [
              { "tag": "Пост", "type": "permanent", "gammaF": 1.1, "selfWeight": 1,
                "loads": [ { "member": "Р1", "q": [0, 0, -5] } ] },
              { "tag": "Снег", "type": "short_term", "gammaF": 1.4, "longTermShare": 0.5,
                "loads": [ { "member": "Р1", "q": [0, 0, -3] } ] },
              { "tag": "Крен", "type": "imperfection",
                "imperfections": [ { "tag": "Крен X", "kind": "sway", "params": { "scope": "all", "angleDeg": 0, "phiMode": "en1993", "m": 2 } } ] } ],
            "combinations": [
              { "tag": "РСН1", "type": "C", "terms": [ { "loadCase": "Пост", "k": 1.1 }, { "loadCase": "Снег", "k": 1.4 } ] },
              { "tag": "РСН2", "type": "N", "terms": [ { "loadCase": "Пост", "k": 1 }, { "loadCase": "Снег", "k": 1 } ] },
              { "tag": "Все", "all": true } ] } ]
        """;

    /// <summary>Пять видов: секущий по стадиям, пакет, линейный, расчётные длины, OpenSees линейный и нелинейный с путём.</summary>
    const string Analyses = """
        "femAnalyses": [
          { "schema": "Рама", "tag": "Секущий", "kind": "csfea", "calcType": "N",
            "stages": [ { "tag": "Пост", "load": { "loadCase": "Пост" }, "step": 0.2, "max": 1 },
                        { "load": { "combination": "РСН2" }, "step": 0.1, "max": 1 } ],
            "csfea": { "geom": true, "iterations": { "max": 40, "omega0": 0.6 }, "controlNode": 3, "controlDof": "uz" } },
          { "schema": "Рама", "tag": "Пакет", "kind": "csfea", "calcType": "C",
            "batch": { "items": [ { "combination": "РСН1" }, { "loadCase": "Снег" } ], "step": 0.25, "max": 1,
                       "imperfections": [ { "loadCase": "Крен", "bothSigns": false } ] } },
          { "schema": "Рама", "tag": "Линейный", "kind": "csfeaLinear", "loadCases": ["Пост", "Снег"] },
          { "schema": "Рама", "tag": "Длины", "kind": "csfeaBuckling",
            "combinations": [ { "combination": "РСН1" } ], "buckling": { "modes": 6 } },
          { "schema": "Рама", "tag": "OS лин", "kind": "openseesLinear", "load": { "terms": [ { "loadCase": "Пост", "k": 2 } ] } },
          { "schema": "Рама", "tag": "OS нелин", "kind": "openseesNonlinear", "calcType": "C",
            "stages": [ { "load": { "loadCase": "Пост" }, "step": 0.1, "max": 1 },
                        { "load": { "combination": "Все" },
                          "pathControl": { "mode": "displacement", "node": 3, "dof": "uz", "initial": 0.001, "min": 0.0001,
                                           "max": 0.01, "target": -0.05, "maxSteps": 100 } } ],
            "opensees": { "materials": "native", "hardeningMpa": 2000 } } ]
        """;

    static string Full(string nodes = "", string analyses = Analyses) =>
        "{ \"format\": \"opencs-model\", \"version\": 1, " + Schema.Replace("NODES", nodes) + (analyses.Length > 0 ? ", " + analyses : "") + " }";

    static (DatabaseService Db, FemSchema Schema) Open(string project)
    {
        var db = new DatabaseService(project);
        db.LoadAll();
        return (db, db.FemSchemas.Single(s => s.Tag == "Рама"));
    }

    static JsonArray Export(string file) => JsonNode.Parse(File.ReadAllText(file))!["femAnalyses"]!.AsArray();

    [Fact]
    public async Task Apply_AllKinds_RefsByTag_DryRunNothing_RoundTripSame_ReapplyUnchanged()
    {
        string project = PathOf("a.db");
        Assert.Equal(CliExitCode.Ok, (await Run("new", project)).Code);
        var dry = await Run("apply", project, Doc("dry.json", Full()), "--dry-run");
        Assert.True(dry.Code == CliExitCode.Ok, dry.Err + dry.Out);
        using (var db0 = new DatabaseService(project)) { db0.LoadAll(); Assert.Empty(db0.FemSchemas); }

        var r = await Run("apply", project, Doc("seed.json", Full()));
        Assert.True(r.Code == CliExitCode.Ok, r.Err + r.Out);
        var (db, schema) = Open(project);
        string paramsBefore;
        using (db)
        {
            var list = db.GetFemAnalyses(schema.Id);
            Assert.Equal(["Секущий", "Пакет", "Линейный", "Длины", "OS лин", "OS нелин"], list.Select(a => a.Tag));
            Assert.Equal([FemCsfeaRunner.AnalysisKind, FemCsfeaRunner.AnalysisKind, FemCsfeaRunner.LinearAnalysisKind,
                FemBucklingRunner.AnalysisKind, "linear", "nonlinear"], list.Select(a => a.Kind));
            var lc = schema.LoadCases.ToDictionary(c => c.Tag, c => c.Id);

            var secant = FemAnalysisParams.Parse(list[0].ParamsJson);
            Assert.Equal(2, secant.Stages.Count);
            Assert.Equal("РСН2", secant.Stages[1].Tag);                                   // имя стадии — по нагрузке
            Assert.Equal(schema.LoadDefinitions.Single(d => d.Tag == "РСН2").GetExpression().ToJson(), secant.Stages[1].LoadExpressionJson);
            Assert.True(secant.Csfea!.GeomNonlinear);
            Assert.Equal((40, 0.6, "3", 2), (secant.Csfea.MaxIterations, secant.Csfea.Omega0, secant.Csfea.ControlNodeTag, secant.Csfea.ControlDof));

            var batch = FemAnalysisParams.Parse(list[1].ParamsJson).Batch!;
            Assert.Equal(["РСН1", "Снег"], batch.Items.Select(i => i.Tag));
            Assert.Equal(["C", "Q"], batch.Items.Select(i => i.Type));
            Assert.Equal((lc["Крен"], false), (batch.Imperfections.Single().LoadCaseId, batch.Imperfections.Single().BothSigns));

            var linear = FemAnalysisParams.Parse(list[2].ParamsJson);
            Assert.Equal([FemAnalysisAuthoring.SingleCase(lc["Пост"]), FemAnalysisAuthoring.SingleCase(lc["Снег"])],
                linear.Stages.Select(s => s.LoadExpressionJson));

            Assert.Equal(6, FemAnalysisParams.Parse(list[3].ParamsJson).Buckling!.Modes);
            Assert.Equal(2, FemLoadExpression.Parse(list[4].LoadExpressionJson).Terms.Single().Coefficient);

            var os = FemAnalysisParams.Parse(list[5].ParamsJson);
            Assert.Equal(("Native", 2000.0), (os.MaterialSource, os.SteelHardeningModulusMpa!.Value));
            Assert.Equal("LoadControl", os.Stages[0].PathControl!.Mode);
            var pc = os.Stages[1].PathControl!;
            Assert.Equal(("DisplacementControl", 3, -0.05), (pc.Mode, pc.ControlDof!.Value, pc.TargetDisplacement!.Value));
            Assert.Equal("3", db.GetFemNodes(schema.Id).Single(n => n.Id == pc.ControlNodeId).NodeTag);
            paramsBefore = string.Join("|", list.Select(a => a.ParamsJson + a.LoadExpressionJson));
        }

        string e1 = PathOf("e1.json");
        Assert.Equal(CliExitCode.Ok, (await Run("export", "model", project, "--to", e1)).Code);
        var out1 = Export(e1);
        Assert.Equal(6, out1.Count);
        Assert.Equal("РСН2", out1[0]!["stages"]![1]!["load"]!["combination"]!.GetValue<string>());
        Assert.Equal("3", out1[5]!["stages"]![1]!["pathControl"]!["node"]!.GetValue<string>());

        // Повторный apply выгрузки постановок в тот же проект: постановки не перезаписываются (схему документ не трогает —
        // её перезапись выдала бы загружениям новые Id).
        string only = PathOf("only.json");
        Assert.Equal(CliExitCode.Ok, (await Run("export", "model", project, "--to", only, "--only", "femAnalyses")).Code);
        var again = await Run("apply", project, only);
        Assert.True(again.Code == CliExitCode.Ok, again.Err + again.Out);
        var (db2, schema2) = Open(project);
        using (db2)
            Assert.Equal(paramsBefore, string.Join("|", db2.GetFemAnalyses(schema2.Id).Select(a => a.ParamsJson + a.LoadExpressionJson)));

        // Кругооборот в новый проект — та же выгрузка.
        string second = PathOf("b.db");
        Assert.Equal(CliExitCode.Ok, (await Run("new", second)).Code);
        var r2 = await Run("apply", second, e1);
        Assert.True(r2.Code == CliExitCode.Ok, r2.Err + r2.Out);
        string e2 = PathOf("e2.json");
        Assert.Equal(CliExitCode.Ok, (await Run("export", "model", second, "--to", e2)).Code);
        Assert.Equal(out1.ToJsonString(), Export(e2).ToJsonString());
    }

    [Fact]
    public async Task SchemaSave_RemapsPathControlNodes()
    {
        string project = PathOf("n.db");
        Assert.Equal(CliExitCode.Ok, (await Run("new", project)).Code);
        Assert.Equal(CliExitCode.Ok, (await Run("apply", project, Doc("seed.json", Full()))).Code);
        // Схема изменена (новый узел) — запись выдаёт узлам новые Id; узел пути должен остаться узлом 3.
        var r = await Run("apply", project, Doc("more.json", Full(nodes: ", { \"tag\": 5, \"x\": 3, \"y\": 0, \"z\": 6 }", analyses: "")));
        Assert.True(r.Code == CliExitCode.Ok, r.Err + r.Out);
        var (db, schema) = Open(project);
        using (db)
        {
            var os = FemAnalysisParams.Parse(db.GetFemAnalyses(schema.Id).Single(a => a.Tag == "OS нелин").ParamsJson);
            int nodeId = os.Stages[1].PathControl!.ControlNodeId!.Value;
            Assert.Equal("3", db.GetFemNodes(schema.Id).Single(n => n.Id == nodeId).NodeTag);
        }
    }

    [Fact]
    public async Task Apply_Errors_WithPaths_NothingWritten()
    {
        string project = PathOf("e.db");
        Assert.Equal(CliExitCode.Ok, (await Run("new", project)).Code);
        var bad = await Run("apply", project, Doc("bad.json", Full(analyses: """
            "femAnalyses": [
              { "schema": "Рама", "tag": "А", "kind": "csfea", "stages": [ { "load": { "combination": "Нет" } } ] },
              { "schema": "Рама", "tag": "Б", "kind": "csfea", "stages": [ { "load": { "loadCase": "Пост" }, "step": 0 } ] },
              { "schema": "Рама", "tag": "В", "kind": "openseesNonlinear",
                "stages": [ { "load": { "loadCase": "Пост" }, "pathControl": { "mode": "displacement", "node": 9, "dof": "uz",
                  "initial": 0.01, "min": 0.1, "max": 0.2, "target": 1, "maxSteps": 10 } } ] },
              { "schema": "Рама", "tag": "Г", "kind": "что-то" },
              { "schema": "Нет", "tag": "Д", "kind": "openseesLinear", "load": { "loadCase": "Пост" } },
              { "schema": "Рама", "tag": "Б", "kind": "openseesLinear", "load": { "loadCase": "Пост" } } ]
            """)));
        Assert.Equal(CliExitCode.Usage, bad.Code);
        string all = bad.Err + bad.Out;
        foreach (var path in new[] { "femAnalyses[0].stages[0].load.combination", "femAnalyses[1].stages[0].step",
                     "femAnalyses[2].stages[0].pathControl.node", "femAnalyses[3].kind", "femAnalyses[4].schema", "femAnalyses[5].tag" })
            Assert.Contains(path, all);
        using var db = new DatabaseService(project);
        db.LoadAll();
        Assert.Empty(db.FemSchemas);   // при ошибках не записано ничего, даже схема
    }

    // ── Служба ─────────────────────────────────────────────────────────────────────────────

    static FemSchema TestSchema()
    {
        var s = new FemSchema { Id = 1, Tag = "С" };
        s.LoadCases.Add(new FemLoadCase { Id = 10, Tag = "Пост" });
        s.Analyses.Add(new FemAnalysis { Id = 5, SchemaId = 1, Tag = "Линейный", Kind = "linear" });
        return s;
    }

    [Fact]
    public void Service_Tag_DuplicateAndEmpty_FreeTag()
    {
        var s = TestSchema();
        Assert.NotNull(FemAnalysisAuthoring.TagError(s, " линейный "));
        Assert.Null(FemAnalysisAuthoring.TagError(s, "Линейный", selfId: 5));
        Assert.NotNull(FemAnalysisAuthoring.TagError(s, "  "));
        Assert.Equal("Линейный (2)", FemAnalysisAuthoring.FreeTag(s, "Линейный"));
        Assert.Equal("Линейный", FemAnalysisAuthoring.FreeTag(s, "Линейный", selfId: 5));
    }

    [Fact]
    public void Service_Validate_Paths_LoadModes_PathControl()
    {
        var s = TestSchema();
        string Json(FemAnalysisParams p) => p.ToJson();
        var allLoad = new FemLoadExpression { Mode = FemLoadExpressionMode.All }.ToJson();
        Assert.Empty(FemAnalysisAuthoring.Validate(s, new FemAnalysis { Tag = "А", Kind = "linear", LoadExpressionJson = allLoad }));
        var missing = FemAnalysisAuthoring.Validate(s, new FemAnalysis { Tag = "А", Kind = "linear", LoadExpressionJson = FemAnalysisAuthoring.SingleCase(99) });
        Assert.Equal("load", Assert.Single(missing).Path);

        var p = new FemAnalysisParams
        {
            Csfea = new FemCsfeaParams { Omega0 = 1.5 },
            Stages = [new FemAnalysisStage { Tag = "1", LoadExpressionJson = FemAnalysisAuthoring.SingleCase(10), LoadFactorStep = 2, MaxLoadFactor = 1 }],
        };
        var e = FemAnalysisAuthoring.Validate(s, new FemAnalysis { Tag = "Б", Kind = FemCsfeaRunner.AnalysisKind, ParamsJson = Json(p) });
        Assert.Equal(["stages[0].step", "csfea.iterations.omega0"], e.Select(x => x.Path).Order().Reverse());

        var dc = new FemAnalysisPathControl
        {
            Mode = "DisplacementControl", ControlNodeId = 7, ControlDof = 3, InitialIncrement = 0.01, MinIncrement = 0.001,
            MaxIncrement = 0.1, TargetDisplacement = -0.1, MaxSteps = 10,
        };
        var ok = new List<FieldError>();
        FemAnalysisAuthoring.PathControl(dc, "p", "1", ok, [7]);
        Assert.Empty(ok);
        dc.MinIncrement = 0.05;   // больше начального
        var wrong = new List<FieldError>();
        FemAnalysisAuthoring.PathControl(dc, "p", "1", wrong, [8]);
        Assert.Equal(["p.initial", "p.node"], wrong.Select(x => x.Path).Order());
    }

    [Fact]
    public void Service_ApplyEdit_RenameKeepsResult_InputChangeResets()
    {
        var a = new FemAnalysis { Id = 1, Tag = "А", Kind = "linear", LoadExpressionJson = FemAnalysisAuthoring.SingleCase(10), ResultId = 42, Status = "done" };
        var renamed = new FemAnalysis { Tag = "Б", Kind = "linear", LoadExpressionJson = a.LoadExpressionJson, ParamsJson = a.ParamsJson };
        Assert.Equal((true, false), FemAnalysisAuthoring.ApplyEdit(a, renamed));
        Assert.Equal(42, a.ResultId);
        var same = new FemAnalysis { Tag = "Б", Kind = "linear", LoadExpressionJson = a.LoadExpressionJson, ParamsJson = a.ParamsJson };
        Assert.Equal((false, false), FemAnalysisAuthoring.ApplyEdit(a, same));
        var other = new FemAnalysis { Tag = "Б", Kind = "linear", LoadExpressionJson = FemAnalysisAuthoring.SingleCase(11), ParamsJson = a.ParamsJson };
        Assert.Equal((true, true), FemAnalysisAuthoring.ApplyEdit(a, other));
        Assert.Null(a.ResultId);
    }
}
