using System.IO;
using System.Text.Json.Nodes;
using CScore;
using CScore.Fem;
using CScore.Fem.Fragment.Masks;
using OpenCS.Cli;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Подраздел 7в: продавливание по МКЭ — вид <c>punching</c> раздела <c>femChecks</c> (источник по имени постановки или
/// снимок, параметры в единицах окна, отличия узлов, кругооборот, неизменённое не перезаписывается, смена входа удаляет
/// результат, ошибки с путями) и проверка продавливания службой <see cref="FemCheckAuthoring"/>.
/// </summary>
[Collection("Loc language")]
public sealed class CliFemPunchingTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_cli_punch_{Guid.NewGuid():N}");
    public CliFemPunchingTests()
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

    /// <summary>Колонна под плитой: узлы 1–4, стержень К1, плита П1, линейная постановка и расчётные длины.</summary>
    const string Base = """
        "materials": [
          { "tag": "B25", "catalog": { "concrete": "B25" } },
          { "tag": "A500", "catalog": { "rebar": "A500" } } ],
        "sections": [ { "tag": "К-400", "areas": [
          { "tag": "Бетон", "category": "region", "material": "B25", "diagram": "sp63",
            "polygon": { "outer": [[-200, -200], [200, -200], [200, 200], [-200, 200]] } } ] } ],
        "plateSections": [ { "tag": "Плита 200", "h": 200, "concrete": "B25", "rebar": "A500" } ],
        "femSchemas": [
          { "tag": "Каркас",
            "nodes": [
              { "tag": 1, "x": 3, "y": 3, "z": 0, "fix": ["x", "y", "z", "ux", "uy", "uz"] },
              { "tag": 2, "x": 3, "y": 3, "z": 3 },
              { "tag": 3, "x": 0, "y": 0, "z": 3 },
              { "tag": 4, "x": 6, "y": 6, "z": 3 } ],
            "members": [
              { "tag": "К1", "nodes": [1, 2], "section": "К-400" },
              { "tag": "П1", "type": "shell", "plateSection": "Плита 200",
                "region": { "outline": [[0, 0, 3], [6, 0, 3], [6, 6, 3], [0, 6, 3]] } } ],
            "groups": [ { "tag": "Колонны", "memberType": "column", "members": ["К1"] } ],
            "loadCases": [ { "tag": "Пост", "type": "permanent", "loads": [ { "member": "К1", "q": [0, 0, -5] } ] } ],
            "combinations": [ { "tag": "РСН1", "type": "C", "terms": [ { "loadCase": "Пост", "k": 1.1 } ] } ] } ],
        "femAnalyses": [
          { "schema": "Каркас", "tag": "Линейный", "kind": "csfeaLinear", "loadCases": ["Пост"] },
          { "schema": "Каркас", "tag": "Длины", "kind": "csfeaBuckling", "combinations": [ { "combination": "РСН1" } ] } ]
        """;

    const string Punching = """
        "femChecks": [
          { "schema": "Каркас", "tag": "Продавливание", "kind": "punching",
            "source": { "analysis": "Линейный" },
            "punching": {
              "method": "contour", "momentsAreMloc": false, "concreteFromSchema": false, "concrete": "B25", "rbtMpa": null,
              "defaultAxMm": 30, "defaultAyMm": 40,
              "sw": { "material": "A500", "dMm": 10, "layout": "uniform", "count": 3, "stepMm": 50,
                      "rows": { "firstMm": 60, "stepMm": 70, "count": 4 } },
              "options": { "wb": "stress_side", "eccentricityX": "auto", "wallFigure": "amd2", "holeInfluence": 5 },
              "angleDeg": 15, "edgeInfluence": 5, "wallRatio": 3, "liraContour": false,
              "pile": { "shape": "circle", "sizeMm": 400 },
              "nodes": [
                { "node": "3", "axMm": 45, "capital": { "bxMm": 800, "byMm": 900, "hcMm": 150 }, "sw": { "dMm": 12 } },
                { "node": "2", "include": false } ] } } ]
        """;

    static string Full(string checks = Punching) =>
        "{ \"format\": \"opencs-model\", \"version\": 1, " + Base + (checks.Length > 0 ? ", " + checks : "") + " }";

    static string OnlyChecks(string checks) => "{ \"format\": \"opencs-model\", \"version\": 1, " + checks + " }";

    static DatabaseService Open(string project)
    {
        var db = new DatabaseService(project);
        db.LoadAll();
        return db;
    }

    static JsonArray Export(string file) => JsonNode.Parse(File.ReadAllText(file))!["femChecks"]!.AsArray();

    static void FakeResult(DatabaseService db, FemCheck check)
    {
        var r = new CalcResult { TaskKind = "fem_check", TaskTag = check.Tag, Created = "2026-11-26", Status = "ok", DataJson = "{}" };
        db.SaveCalcResultRaw(r, check.Id);
        check.ResultId = r.Id;
        db.SaveFemCheck(check);
    }

    async Task<string> Seed(string name = "a.db")
    {
        string project = PathOf(name);
        Assert.Equal(CliExitCode.Ok, (await Run("new", project)).Code);
        var r = await Run("apply", project, Doc(name + ".json", Full()));
        Assert.True(r.Code == CliExitCode.Ok, r.Err + r.Out);
        return project;
    }

    [Fact]
    public async Task Apply_Punching_UnitsAndRefs_RoundTripSame_ReapplyKeepsResult()
    {
        string project = PathOf("a.db");
        Assert.Equal(CliExitCode.Ok, (await Run("new", project)).Code);
        var dry = await Run("apply", project, Doc("dry.json", Full()), "--dry-run");
        Assert.True(dry.Code == CliExitCode.Ok, dry.Err + dry.Out);
        using (var db0 = Open(project)) Assert.Empty(db0.FemChecks);
        Assert.True((await Run("apply", project, Doc("seed.json", Full()))).Code == CliExitCode.Ok);

        string paramsBefore;
        using (var db = Open(project))
        {
            var schema = db.FemSchemas.Single();
            var c = db.FemChecks.Single();
            Assert.Equal((PunchingFemCheckParams.NormCode, schema.Id, 0, (int?)null, "[]"), (c.NormCode, c.SchemaId, c.MemberId, c.ElementId, c.ForceSetIdsJson));
            var p = PunchingFemCheckParams.Parse(c.ParamsJson);
            var lin = db.GetFemAnalyses(schema.Id).Single(a => a.Tag == "Линейный");
            Assert.Equal((FragmentMaskSet.SourceCsfeaLinear, lin.Id.ToString()), (p.SourceKind, p.SourceRef));
            Assert.Equal(("contour", false, false), (p.Method, p.MomentsAreMloc, p.ConcreteFromSchema));
            Assert.Equal(db.Materials.Single(m => m.Tag == "B25").Id, p.ConcreteMaterialId);
            Assert.Null(p.RbtOverride);
            Assert.Equal((0.03, 0.04), (p.DefaultAx, p.DefaultAy));
            var r = p.Reinforcement;
            Assert.Equal((true, db.Materials.Single(m => m.Tag == "A500").Id, 0.01, 3, 0.05), (r.Enabled, r.MaterialId, r.Diameter, r.Count, r.Step));
            Assert.Equal((0.06, 0.07, 4), (r.FirstRow!.Value, r.RowStep, r.RowCount));
            Assert.Equal(("stress_side", "auto", "loading", "amd2", 5.0), (p.Options.Wb, p.Options.EccentricityX, p.Options.EccentricityY, p.Options.WallFigure, p.Options.HoleInfluence));
            Assert.Equal((15.0, 5.0, 3.0, "circle", 0.4), (p.AngleDeg, p.EdgeInfluence, p.WallRatio, p.PileShape, p.PileSize));
            // Узлы — по порядку номеров; своя арматура узла 3 — общая с диаметром узла.
            Assert.Equal(["2", "3"], p.Overrides.Select(o => o.Node));
            Assert.True(p.Overrides[0].Excluded);
            var n3 = p.Overrides[1];
            Assert.Equal((0.045, "manual", 0.8, 0.9, 0.15), (n3.Ax!.Value, n3.Capital, n3.CapitalManual!.Bx, n3.CapitalManual.By, n3.CapitalManual.Hc));
            Assert.Equal((0.012, 3, 0.05, 4, 0.06), (n3.Reinforcement!.Diameter, n3.Reinforcement.Count, n3.Reinforcement.Step, n3.Reinforcement.RowCount, n3.Reinforcement.FirstRow!.Value));
            FakeResult(db, c);
            paramsBefore = c.ParamsJson!;
        }

        string e1 = PathOf("e1.json");
        Assert.Equal(CliExitCode.Ok, (await Run("export", "model", project, "--to", e1)).Code);
        var out1 = Export(e1);
        var item = Assert.Single(out1)!;
        Assert.Equal("Линейный", item["source"]!["analysis"]!.GetValue<string>());
        Assert.Equal(30.0, item["punching"]!["defaultAxMm"]!.GetValue<double>());
        Assert.Equal("A500", item["punching"]!["sw"]!["material"]!.GetValue<string>());
        Assert.Equal(12.0, item["punching"]!["nodes"]![1]!["sw"]!["dMm"]!.GetValue<double>());
        Assert.False(item["punching"]!["nodes"]![0]!["include"]!.GetValue<bool>());

        // Повторный apply выгрузки: ничего не перезаписано, результат на месте.
        string only = PathOf("only.json");
        Assert.Equal(CliExitCode.Ok, (await Run("export", "model", project, "--to", only, "--only", "femChecks")).Code);
        var again = await Run("apply", project, only);
        Assert.True(again.Code == CliExitCode.Ok, again.Err + again.Out);
        using (var db = Open(project))
        {
            var c = db.FemChecks.Single();
            Assert.Equal(paramsBefore, c.ParamsJson);
            Assert.NotNull(c.ResultId);
            Assert.NotNull(db.GetCalcResultHeaderByFemCheck(c.Id));
        }

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
    public async Task Apply_Punching_ChangedInputResets_NodesOmittedKept_EmptyArrayClears_SwNullOff()
    {
        string project = await Seed();
        using (var db = Open(project)) FakeResult(db, db.FemChecks.Single());

        var r = await Run("apply", project, Doc("angle.json", OnlyChecks("""
            "femChecks": [ { "schema": "Каркас", "tag": "Продавливание", "kind": "punching", "punching": { "angleDeg": 20 } } ]
            """)));
        Assert.True(r.Code == CliExitCode.Ok, r.Err + r.Out);
        using (var db = Open(project))
        {
            var c = db.FemChecks.Single();
            Assert.Null(c.ResultId);
            Assert.Null(db.GetCalcResultHeaderByFemCheck(c.Id));
            var p = PunchingFemCheckParams.Parse(c.ParamsJson);
            Assert.Equal(20.0, p.AngleDeg);
            Assert.Equal(2, p.Overrides.Count);                       // без nodes — узлы не тронуты
            Assert.Equal("contour", p.Method);
        }

        r = await Run("apply", project, Doc("clear.json", OnlyChecks("""
            "femChecks": [ { "schema": "Каркас", "tag": "Продавливание", "kind": "punching",
                             "punching": { "sw": null, "nodes": [ { "node": "4" } ] } } ]
            """)));
        Assert.True(r.Code == CliExitCode.Ok, r.Err + r.Out);
        using (var db = Open(project))
        {
            var p = PunchingFemCheckParams.Parse(db.FemChecks.Single().ParamsJson);
            Assert.Empty(p.Overrides);                                 // узел без отличий — не переопределение
            Assert.False(p.Reinforcement.Enabled);
            Assert.Equal(0.01, p.Reinforcement.Diameter);              // значения остаются для окна
        }
    }

    [Fact]
    public async Task Apply_Punching_Errors_WithPaths_NothingWritten()
    {
        string project = await Seed();
        var r = await Run("apply", project, Doc("bad.json", OnlyChecks("""
            "femChecks": [
              { "schema": "Каркас", "tag": "Без источника", "kind": "punching" },
              { "schema": "Каркас", "tag": "Снимок", "kind": "punching", "source": { "snapshot": "lira" } },
              { "schema": "Каркас", "tag": "Длины", "kind": "punching", "source": { "analysis": "Длины" } },
              { "schema": "Каркас", "tag": "Продавливание", "kind": "steel", "target": { "group": "Колонны" } },
              { "schema": "Каркас", "tag": "Числа", "kind": "punching", "source": { "analysis": "Линейный" },
                "punching": { "sw": { "dMm": 0 }, "pile": { "sizeMm": 0 }, "angleDeg": 95, "defaultAxMm": -1 } },
              { "schema": "Каркас", "tag": "Узлы", "kind": "punching", "source": { "analysis": "Линейный" },
                "punching": { "nodes": [ { "node": "999", "axMm": 40 }, { "node": "3", "capital": "manual" } ] } } ]
            """)));
        Assert.Equal(CliExitCode.Usage, r.Code);
        foreach (var path in new[]
                 {
                     "femChecks[0].source", "femChecks[1].source", "femChecks[2].source.analysis", "femChecks[3].kind",
                     "femChecks[4].punching.sw", "femChecks[4].punching.pile.sizeMm", "femChecks[4].punching.angleDeg",
                     "femChecks[4].punching.defaultAxMm", "femChecks[5].punching.nodes[0].node", "femChecks[5].punching.nodes[1].capital",
                 })
            Assert.Contains(path, r.Err + r.Out);
        using var db = Open(project);
        Assert.Single(db.FemChecks);
    }

    // ── Служба ──────────────────────────────────────────────────────────────────────────────

    static (FemCheckRefs Refs, FemCheck Check) ServiceCase(PunchingFemCheckParams p, IReadOnlyCollection<string>? snapshots = null)
    {
        var schema = new FemSchema { Id = 1, Tag = "Каркас" };
        var analyses = new List<FemAnalysis>
        {
            new() { Id = 5, SchemaId = 1, Tag = "Линейный", Kind = FemCsfeaRunner.LinearAnalysisKind },
            new() { Id = 6, SchemaId = 1, Tag = "Секущий", Kind = FemCsfeaRunner.AnalysisKind },
        };
        var materials = new List<Material>
        {
            new() { Id = 10, Tag = "B25", Type = MatType.Concrete },
            new() { Id = 11, Tag = "A500", Type = MatType.ReSteelU },
        };
        var refs = new FemCheckRefs(schema, [], [], [], analyses, materials, snapshots ?? []);
        var c = new FemCheck { SchemaId = 1, Tag = "Продавливание", NormCode = PunchingFemCheckParams.NormCode, ParamsJson = p.ToJson() };
        return (refs, c);
    }

    static List<string> Paths(PunchingFemCheckParams p, IReadOnlyCollection<string>? snapshots = null)
    {
        var (refs, c) = ServiceCase(p, snapshots);
        return FemCheckAuthoring.Validate(refs, c).Select(e => e.Path).ToList();
    }

    [Fact]
    public void Service_ValidatePunching_SourceMaterialsNumbersNodes()
    {
        var ok = new PunchingFemCheckParams { SourceRef = "5", ConcreteMaterialId = 10 };
        Assert.Empty(Paths(ok));
        Assert.Empty(Paths(ok with { SourceKind = FragmentMaskSet.SourceCsfeaSecant, SourceRef = "6" }));
        Assert.Equal(["source"], Paths(ok with { SourceKind = FragmentMaskSet.SourceCsfeaSecant, SourceRef = "5" }));  // вид не тот
        Assert.Equal(["source"], Paths(ok with { SourceRef = "" }));
        Assert.Equal(["source"], Paths(ok with { SourceKind = FragmentMaskSet.SourceLira, SourceRef = "" }));
        Assert.Empty(Paths(ok with { SourceKind = FragmentMaskSet.SourceLira, SourceRef = "" }, [FragmentMaskSet.SourceLira]));

        // Бетон: из схемы без своего — можно; свой удалённый — ошибка; Rbt вручную — материал не нужен.
        Assert.Empty(Paths(ok with { ConcreteMaterialId = 0 }));
        Assert.Equal(["punching.concrete"], Paths(ok with { ConcreteFromSchema = false, ConcreteMaterialId = 0 }));
        Assert.Empty(Paths(ok with { ConcreteFromSchema = false, ConcreteMaterialId = 0, RbtOverride = 1050 }));
        Assert.Equal(["punching.rbtMpa"], Paths(ok with { RbtOverride = 0 }));

        Assert.Equal(["punching.sw.material"], Paths(ok with { Reinforcement = new() { Enabled = true } }));
        Assert.Empty(Paths(ok with { Reinforcement = new() { Enabled = true, RswOverride = 170000 } }));
        Assert.Equal(["punching.sw"], Paths(ok with { Reinforcement = new() { Enabled = true, MaterialId = 11, Diameter = 0 } }));
        Assert.Empty(Paths(ok with { Reinforcement = new() { Enabled = false, Diameter = 0 } }));   // выключенная не проверяется

        Assert.Equal(new[] { "punching.angleDeg", "punching.defaultAxMm", "punching.options.wb", "punching.pile.sizeMm" },
            Paths(ok with { DefaultAx = -0.01, PileSize = 0, AngleDeg = 90, Options = new() { Wb = "max" } }).Order(StringComparer.Ordinal));

        var nodes = Paths(ok with
        {
            Overrides =
            [
                new() { Node = "3", Ax = 0 },
                new() { Node = "3" },
                new() { Node = "4", Capital = "manual" },
                new() { Node = "5", Reinforcement = new() { Enabled = true, MaterialId = 11, Count = 0 } },
            ],
        });
        Assert.Equal(["punching.nodes[0].axMm", "punching.nodes[1].node", "punching.nodes[2].capital", "punching.nodes[3].sw"], nodes);
    }

    [Fact]
    public void Service_ValidatePunching_NodeMessagesNameNode_TagRules()
    {
        var (refs, c) = ServiceCase(new PunchingFemCheckParams
        {
            SourceRef = "5", ConcreteMaterialId = 10, Overrides = [new() { Node = "17", PileSize = -1 }],
        });
        var e = FemCheckAuthoring.Validate(refs, c);
        Assert.StartsWith("Узел 17: ", Assert.Single(e).Message);

        c.Tag = "  ";
        Assert.Contains(FemCheckAuthoring.Validate(refs, c), x => x.Path == "tag");
        var other = new FemCheck { Id = 9, SchemaId = 1, Tag = "Сталь", NormCode = FemCheckAuthoring.Steel };
        c.Tag = "сталь";
        Assert.Contains(FemCheckAuthoring.Validate(refs with { Checks = [other] }, c), x => x.Path == "tag");
    }

    [Fact]
    public void Service_ApplyEdit_Punching_SameContentKeepsText_ParamChangeIsInput_RenameNot()
    {
        var p = new PunchingFemCheckParams { SourceRef = "5", AngleDeg = 10 };
        var saved = new FemCheck { Id = 3, SchemaId = 1, Tag = "П", NormCode = PunchingFemCheckParams.NormCode, ParamsJson = "{\"sourceRef\":\"5\"}", ResultId = 7 };

        // Старый текст без полей по умолчанию — то же содержание: не изменение.
        var same = FemCheckAuthoring.Copy(saved);
        same.ParamsJson = p.ToJson();
        Assert.Equal((false, false), FemCheckAuthoring.ApplyEdit(saved, same));
        Assert.Equal("{\"sourceRef\":\"5\"}", saved.ParamsJson);

        var renamed = FemCheckAuthoring.Copy(saved);
        renamed.Tag = "П-1";
        Assert.Equal((true, false), FemCheckAuthoring.ApplyEdit(saved, renamed));

        var changed = FemCheckAuthoring.Copy(saved);
        changed.ParamsJson = (p with { AngleDeg = 12 }).ToJson();
        Assert.Equal((true, true), FemCheckAuthoring.ApplyEdit(saved, changed));
    }
}
