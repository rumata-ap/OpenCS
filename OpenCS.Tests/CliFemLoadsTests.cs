using System.IO;
using System.Text.Json.Nodes;
using CScore.Fem;
using CScore.Fem.Editing;
using OpenCS.Cli;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Подраздел 6в: загружения, нагрузки, комбинации и несовершенства — вид femSchemas документа opencs-model и служба
/// <see cref="FemLoadAuthoring"/> (удаление загружения, запись схемы с «авто»-слагаемыми и пакетами CSfea).
/// </summary>
[Collection("Loc language")]
public sealed class CliFemLoadsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_cli_femloads_{Guid.NewGuid():N}");
    public CliFemLoadsTests()
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

    async Task<string> Seeded(string name = "p.db")
    {
        string project = PathOf(name);
        Assert.Equal(CliExitCode.Ok, (await Run("new", project)).Code);
        var r = await Run("apply", project, Doc("seed-" + name + ".json", Seed));
        Assert.True(r.Code == CliExitCode.Ok, r.Err + r.Out);
        return project;
    }

    /// <summary>Рама (колонны К1, К2, ригель Р1) и плита П1; загружения всех видов, несовершенство, комбинации.</summary>
    const string Seed = """
        { "format": "opencs-model", "version": 1,
          "materials": [
            { "tag": "B25", "catalog": { "concrete": "B25" } },
            { "tag": "A500", "catalog": { "rebar": "A500" } } ],
          "sections": [ { "tag": "Б-1", "areas": [
            { "tag": "Бетон", "category": "region", "material": "B25", "diagram": "sp63",
              "polygon": { "outer": [[-200, -300], [200, -300], [200, 300], [-200, 300]] } } ] } ],
          "plateSections": [ { "tag": "Плита 200", "h": 200, "concrete": "B25", "rebar": "A500" } ],
          "femSchemas": [
            { "tag": "Рама",
              "nodes": [
                { "tag": 1, "x": 0, "y": 0, "z": 0, "fix": ["x", "y", "z", "ux", "uy", "uz"] },
                { "tag": 2, "x": 0, "y": 0, "z": 3 },
                { "tag": 3, "x": 6, "y": 0, "z": 3 },
                { "tag": 4, "x": 6, "y": 0, "z": 0, "fix": ["x", "y"] } ],
              "members": [
                { "tag": "К1", "nodes": [1, 2], "section": "Б-1" },
                { "tag": "Р1", "nodes": [2, 3], "section": "Б-1" },
                { "tag": "К2", "nodes": [3, 4], "section": "Б-1" },
                { "tag": "П1", "type": "shell", "plateSection": "Плита 200",
                  "region": { "outline": [[0, 0, 3], [6, 0, 3], [6, 4, 3], [0, 4, 3]] } } ],
              "groups": [ { "tag": "Плиты", "memberType": "plate", "members": ["П1"] } ],
              "loadCases": [
                { "tag": "Пост", "type": "permanent", "gammaF": 1.1, "gammaFReduced": 0.9, "selfWeight": 1,
                  "loads": [
                    { "node": 2, "f": [0, 0, -10], "m": [0, 1.5, 0] },
                    { "member": "Р1", "q": [0, 0, -5] },
                    { "member": "Р1", "q": [0, 0, -5], "q2": [0, 0, -8], "a": 0.5, "b": 1, "axes": "global" },
                    { "member": "К1", "point": [2, 0, 0], "m": [0, 0, 0.5], "a": 1.5 },
                    { "plates": ["П1"], "p": -3 },
                    { "group": "Плиты", "p": -1.5, "direction": "normal" } ] },
                { "tag": "Осадка", "type": "long_term", "loads": [ { "node": 4, "displacement": { "z": -5, "uy": 0.2 } } ] },
                { "tag": "Снег", "type": "short_term", "group": "S", "gammaF": 1.4, "longTermShare": 0.5,
                  "loads": [ { "plates": ["П1"], "p": -1.8 } ] },
                { "tag": "Несоверш. X", "type": "imperfection",
                  "imperfections": [ { "tag": "Крен X", "kind": "sway", "comment": "по EN",
                                       "params": { "scope": "all", "angleDeg": 0, "phiMode": "en1993", "m": 2 } } ] } ],
              "combinations": [
                { "tag": "РСН1", "type": "C", "comment": "основное",
                  "terms": [ { "loadCase": "Пост", "role": "permanent" }, { "loadCase": "Снег", "role": "leading" },
                             { "loadCase": "Осадка", "k": 1.2 } ] },
                { "tag": "Все", "all": true } ] } ] }
        """;

    static (DatabaseService Db, FemSchema Schema) Open(string project)
    {
        var db = new DatabaseService(project);
        db.LoadAll();
        return (db, db.FemSchemas.Single(s => s.Tag == "Рама"));
    }

    [Fact]
    public async Task Apply_LoadsCombinationsImperfections_UnitsAndRoles_DryRunNothing_RoundTripSame()
    {
        string project = PathOf("a.db");
        Assert.Equal(CliExitCode.Ok, (await Run("new", project)).Code);
        var dry = await Run("apply", project, Doc("seed.json", Seed), "--dry-run");
        Assert.True(dry.Code == CliExitCode.Ok, dry.Err + dry.Out);
        using (var db0 = new DatabaseService(project)) { db0.LoadAll(); Assert.Empty(db0.FemSchemas); }
        var r = await Run("apply", project, PathOf("seed.json"));
        Assert.True(r.Code == CliExitCode.Ok, r.Err + r.Out);

        var (db, schema) = Open(project);
        using (db)
        {
            var session = FemSchemaAuthoring.OpenSession(db, schema);
            var post = session.LoadCases.Single(c => c.Tag == "Пост");
            Assert.Equal((1.1, 0.9, 1.0), (post.GammaFUnfav!.Value, post.GammaFFav!.Value, post.SelfWeightFactor!.Value));
            var snow = session.LoadCases.Single(c => c.Tag == "Снег");
            Assert.Equal(("S", 0.5), (snow.Sp20Group, snow.LongTermShare!.Value));

            var node = Assert.Single(session.NodeLoads);
            Assert.Equal((-10000.0, 1500.0), (node.Fz, node.My));
            var bars = session.MemberLoads.Where(l => l.LoadCaseId == post.Id).ToList();
            Assert.Equal(3, bars.Count);
            var trap = bars.Single(l => l.DistributionType == "trapezoidal");
            Assert.Equal(("global", 0.5, 1.0, -5000.0, -8000.0), (trap.CoordinateSystem, trap.StartOffsetM, trap.EndOffsetM, trap.QzStart, trap.QzEnd));
            var point = bars.Single(l => l.DistributionType == "point");
            Assert.Equal((2000.0, 500.0, 1.5), (point.QxStart, point.Mz, point.StartOffsetM));
            var kin = session.KinematicLoads.OrderBy(k => k.Dof).ToList();
            Assert.Equal([(3, -0.005), (5, 0.0002)], kin.Select(k => (k.Dof, Math.Round(k.Value, 9))).ToList());
            var area = session.ElementLoads.Where(l => l.LoadCaseId == post.Id).ToList();
            Assert.Equal(-3000, area.Single(l => l.TargetKind == FemLoadTargetKinds.Members).Values[0]);
            var byGroup = area.Single(l => l.TargetKind == FemLoadTargetKinds.Group);
            Assert.Equal(("local", "z", -1500.0), (byGroup.CoordinateSystem, byGroup.Axis, byGroup.Values[0]));
            Assert.Equal(session.MemberGroups.Single(g => g.Tag == "Плиты").Id, byGroup.GroupId);

            // Роли и режим «авто» пережили запись схемы (раньше перенос Id загружений их стирал).
            var rsn = session.LoadDefinitions.Single(d => d.Tag == "РСН1").GetExpression();
            Assert.Equal(["permanent", "leading", null], rsn.Terms.Select(t => t.Role).ToList());
            Assert.True(rsn.Terms[0].IsAuto && rsn.Terms[1].IsAuto && !rsn.Terms[2].IsAuto);
            Assert.Equal(1.1, rsn.Terms[0].Coefficient, 9);
            Assert.Equal(1.4, rsn.Terms[1].Coefficient, 9);
            Assert.Equal(FemLoadExpressionMode.All, session.LoadDefinitions.Single(d => d.Tag == "Все").GetExpression().Mode);

            var imp = Assert.Single(db.GetFemImperfections(schema.Id));
            Assert.Equal(("Крен X", "по EN", session.LoadCases.Single(c => c.Tag == "Несоверш. X").Id), (imp.Tag, imp.Comment, imp.LoadCaseId));
            Assert.Equal(2, CScore.Fem.Imperfections.FemSwayParams.Parse(imp.ParamsJson).M);
        }

        // Кругооборот: выгрузка → новый проект → выгрузка — то же.
        string e1 = PathOf("e1.json"), e2 = PathOf("e2.json");
        Assert.Equal(CliExitCode.Ok, (await Run("export", "model", project, "--to", e1)).Code);
        string second = PathOf("b.db");
        Assert.Equal(CliExitCode.Ok, (await Run("new", second)).Code);
        var apply2 = await Run("apply", second, e1);
        Assert.True(apply2.Code == CliExitCode.Ok, apply2.Err + apply2.Out);
        Assert.Equal(CliExitCode.Ok, (await Run("export", "model", second, "--to", e2)).Code);
        var a = JsonNode.Parse(File.ReadAllText(e1))!["femSchemas"]![0]!;
        var b = JsonNode.Parse(File.ReadAllText(e2))!["femSchemas"]![0]!;
        Assert.Equal(a["loadCases"]!.ToJsonString(), b["loadCases"]!.ToJsonString());
        Assert.Equal(a["combinations"]!.ToJsonString(), b["combinations"]!.ToJsonString());
        Assert.Equal(4, a["loadCases"]!.AsArray().Count);

        // Тот же документ повторно — без изменений и предупреждений об удалении.
        var again = await Run("apply", project, PathOf("seed.json"));
        Assert.True(again.Code == CliExitCode.Ok, again.Err);
        Assert.DoesNotContain("удалено загружений", again.Out + again.Err);
    }

    [Fact]
    public async Task Apply_LoadErrors_WithPaths_NothingWritten()
    {
        string project = await Seeded();
        var r = await Run("apply", project, Doc("bad.json", """
            { "format": "opencs-model", "version": 1,
              "femSchemas": [ { "tag": "Рама",
                "loadCases": [
                  { "tag": "Пост", "type": "permanent",
                    "loads": [
                      { "node": 9, "f": [0, 0, -1] },
                      { "member": "П1", "q": [0, 0, -1] },
                      { "plates": ["Р1"], "p": -1 },
                      { "member": "Р1", "q": [0, 0, -1], "a": 4, "b": 3 },
                      { "member": "Р1", "point": [0, 0, 1], "a": 7 },
                      { "node": 1, "displacement": { "z": -5 } } ] },
                  { "tag": "Осадка", "type": "long_term" },
                  { "tag": "Снег", "type": "short_term" },
                  { "tag": "Несоверш. X", "type": "imperfection",
                    "imperfections": [ { "tag": "К", "kind": "sway", "params": { "scope": "all", "lean": 1 } } ] } ],
                "combinations": [ { "tag": "РСН1", "terms": [ { "loadCase": "Ветер", "k": 1 }, { "loadCase": "Пост", "k": 1 } ] } ] } ] }
            """));
        Assert.Equal(CliExitCode.Usage, r.Code);
        string text = r.Out + r.Err;
        foreach (var path in new[]
                 {
                     "loadCases[0].loads[0].node", "loadCases[0].loads[1].member",
                     "loadCases[0].loads[2].plates[0]", "loadCases[0].loads[3].a", "loadCases[0].loads[4].a",
                     "loadCases[0].loads[5].displacement.z", "loadCases[3].imperfections[0].params",
                     "combinations[0].terms[0].loadCase",
                 })
            Assert.Contains(path, text);

        // Ошибки разбора (до сборки состава): поле не того вида, две цели, пустое слагаемое.
        var parse = await Run("apply", project, Doc("bad2.json", """
            { "format": "opencs-model", "version": 1,
              "femSchemas": [ { "tag": "Рама",
                "loadCases": [ { "tag": "Пост", "type": "short_term", "gammaFReduced": 0.9,
                                 "loads": [ { "node": 2, "member": "Р1", "f": [0, 0, 1] }, { "member": "Р1", "q": [0, 0, -1], "point": [0, 0, 1] } ] } ],
                "combinations": [ { "tag": "РСН1", "terms": [ { "loadCase": "Пост" } ] } ] } ] }
            """));
        Assert.Equal(CliExitCode.Usage, parse.Code);
        foreach (var path in new[] { "loadCases[0].gammaFReduced", "loadCases[0].loads[0]", "loadCases[0].loads[1]", "combinations[0].terms[0]" })
            Assert.Contains(path, parse.Out + parse.Err);

        // Ничего не записано: нагрузки прежние.
        var (db, schema) = Open(project);
        using (db)
        {
            var session = FemSchemaAuthoring.OpenSession(db, schema);
            Assert.Equal(permanentLoads, session.MemberLoads.Count);
            Assert.Equal(FemSp20Types.Permanent, session.LoadCases.Single(c => c.Tag == "Пост").Sp20Type);
        }
    }

    const int permanentLoads = 3;

    [Fact]
    public async Task Apply_RemoveLoadCase_InCombinationOrAnalysis_Refused_OmittedLoadsAndMeshLoadsKept()
    {
        string project = await Seeded();
        // Нагрузка сеточного уровня (на КЭ по номерам) и постановка с «Осадкой» в формуле.
        var (db, schema) = Open(project);
        using (db)
        {
            var session = FemSchemaAuthoring.OpenSession(db, schema);
            var snow = session.LoadCases.Single(c => c.Tag == "Снег");
            var mesh = new FemElementLoad { SchemaId = schema.Id, LoadCaseId = snow.Id, TargetKind = FemLoadTargetKinds.Elements, LoadKind = FemElementLoadKinds.Uniform };
            mesh.SetTargetTags(["7"]);
            mesh.SetValues([-500]);
            session.ElementLoads.Add(mesh);
            Assert.True(FemSchemaAuthoring.Save(db, session, out var diag), string.Join("; ", diag.Select(x => x.Message)));
            int settle = session.LoadCases.Single(c => c.Tag == "Осадка").Id;
            db.SaveFemAnalysis(new FemAnalysis
            {
                SchemaId = schema.Id, Tag = "Линейный", Kind = "linear",
                LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, Terms = [new FemLoadTerm { LoadCaseId = settle, Coefficient = 1 }] }.ToJson(),
            });
        }

        // Без «Снега» и без combinations: «Снег» входит в РСН1 — ошибка; без «Осадки» — задана в постановке.
        var r = await Run("apply", project, Doc("rm.json", """
            { "format": "opencs-model", "version": 1,
              "femSchemas": [ { "tag": "Рама", "loadCases": [
                { "tag": "Пост", "type": "permanent" }, { "tag": "Несоверш. X", "type": "imperfection" } ] } ] }
            """));
        Assert.Equal(CliExitCode.Usage, r.Code);
        Assert.Contains("«Снег» входит в комбинации «РСН1»", r.Out + r.Err);
        Assert.Contains("«Осадка» задано в постановках: «Линейный»", r.Out + r.Err);

        // С combinations без «Снега» — удаляется; у «Поста» нет loads — нагрузки не тронуты; γf стал по умолчанию.
        var ok = await Run("apply", project, Doc("rm2.json", """
            { "format": "opencs-model", "version": 1,
              "femSchemas": [ { "tag": "Рама",
                "loadCases": [ { "tag": "Пост", "type": "permanent" }, { "tag": "Осадка", "type": "long_term" },
                               { "tag": "Несоверш. X", "type": "imperfection" } ],
                "combinations": [ { "tag": "РСН1", "terms": [ { "loadCase": "Пост", "k": 1 } ] } ] } ] }
            """));
        Assert.True(ok.Code == CliExitCode.Ok, ok.Err + ok.Out);
        Assert.Contains("удалено загружений — 1, комбинаций — 1, элементов несовершенства — 0", ok.Out + ok.Err);
        (db, schema) = Open(project);
        using (db)
        {
            var session = FemSchemaAuthoring.OpenSession(db, schema);
            Assert.DoesNotContain(session.LoadCases, c => c.Tag == "Снег");
            var post = session.LoadCases.Single(c => c.Tag == "Пост");
            Assert.Null(post.GammaFUnfav);
            Assert.Equal(permanentLoads, session.MemberLoads.Count(l => l.LoadCaseId == post.Id));
            Assert.Single(db.GetFemImperfections(schema.Id));
            // Постановка по-прежнему ссылается на «Осадку» — Id перенесён записью схемы.
            var analysis = db.GetFemAnalyses(schema.Id).Single();
            Assert.Contains(session.LoadCases.Single(c => c.Tag == "Осадка").Id, FemAnalysisParams.ReferencedLoadCases(analysis));
        }

        // loads у загружения с сеточной нагрузкой: ручные из документа заменяются, сеточная остаётся.
        string project2 = await Seeded("q.db");
        (db, schema) = Open(project2);
        using (db)
        {
            var session = FemSchemaAuthoring.OpenSession(db, schema);
            var snow = session.LoadCases.Single(c => c.Tag == "Снег");
            var mesh = new FemElementLoad { SchemaId = schema.Id, LoadCaseId = snow.Id, TargetKind = FemLoadTargetKinds.Elements, LoadKind = FemElementLoadKinds.Uniform };
            mesh.SetTargetTags(["7"]);
            mesh.SetValues([-500]);
            session.ElementLoads.Add(mesh);
            Assert.True(FemSchemaAuthoring.Save(db, session, out _));
        }
        var seedNode = JsonNode.Parse(Seed)!;
        seedNode["femSchemas"]![0]!["loadCases"]![2]!["loads"] = JsonNode.Parse("""[ { "plates": ["П1"], "p": -2.4 } ]""");
        var re = await Run("apply", project2, Doc("snow.json", seedNode.ToJsonString()));
        Assert.True(re.Code == CliExitCode.Ok, re.Err + re.Out);
        var export = await Run("export", "model", project2, "--to", PathOf("x.json"));
        Assert.Contains("нагрузок сеточного уровня не выгружено — 1", export.Out + export.Err);
        (db, schema) = Open(project2);
        using (db)
        {
            var session = FemSchemaAuthoring.OpenSession(db, schema);
            var snow = session.LoadCases.Single(c => c.Tag == "Снег");
            var loads = session.ElementLoads.Where(l => l.LoadCaseId == snow.Id).ToList();
            Assert.Equal(2, loads.Count);
            Assert.Contains(loads, l => l.TargetKind == FemLoadTargetKinds.Elements && l.Values[0] == -500);
            Assert.Contains(loads, l => l.TargetKind == FemLoadTargetKinds.Members && l.Values[0] == -2400);
        }
    }

    [Fact]
    public async Task Service_DeleteLoadCase_ImpactQuestionUndo_SaveRemapsBatch()
    {
        string project = await Seeded();
        var (db, schema) = Open(project);
        using (db)
        {
            var session = FemSchemaAuthoring.OpenSession(db, schema);
            var snow = session.LoadCases.Single(c => c.Tag == "Снег");
            var settle = session.LoadCases.Single(c => c.Tag == "Осадка");
            var imp = session.LoadCases.Single(c => c.Tag == "Несоверш. X");

            // Пакет CSfea с «Снегом» в варианте и несовершенством — после записи схемы Id должны перейти.
            var parameters = new FemAnalysisParams
            {
                Batch = new FemCsfeaBatch
                {
                    Items = [new FemCsfeaBatchItem { Tag = "В1", LoadExpressionJson = new FemLoadExpression
                        { Mode = FemLoadExpressionMode.Sum, Terms = [new FemLoadTerm { LoadCaseId = snow.Id, Coefficient = 1, Role = "leading", Mode = FemLoadTerm.AutoMode }] }.ToJson() }],
                    Imperfections = [new FemCsfeaBatchImperfection { LoadCaseId = imp.Id, BothSigns = true }],
                },
            };
            var analysis = new FemAnalysis { SchemaId = schema.Id, Tag = "Пакет", Kind = "csfea_secant", ParamsJson = parameters.ToJson() };
            db.SaveFemAnalysis(analysis);

            // «Осадка»: в комбинации РСН1 (останется непустой), не в постановке — вопрос, не запрет.
            var impact = FemLoadAuthoring.DeleteImpact(db, session, settle);
            Assert.False(impact.Blocked);
            Assert.Equal(["РСН1"], impact.Combinations);
            Assert.Empty(impact.Emptied);
            Assert.Equal(2, impact.Loads);
            string question = FemLoadAuthoring.DeleteQuestion(settle, impact);
            Assert.Contains("«Осадка»", question);
            Assert.Contains("вычеркнуто из комбинаций: «РСН1»", question);

            // «Снег» — в пакете постановки «Пакет»: запрет с именем.
            var blocked = FemLoadAuthoring.DeleteImpact(db, session, snow);
            Assert.True(blocked.Blocked);
            Assert.Contains("«Пакет»", FemLoadAuthoring.BlockedText(snow, blocked));
            Assert.Equal(1, FemLoadAuthoring.DeleteImpact(db, session, imp).Imperfections);

            // Удаление одной командой, отмена возвращает слагаемое комбинации.
            session.Execute(FemLoadAuthoring.DeleteCommand(session, settle));
            Assert.DoesNotContain(settle, session.LoadCases);
            Assert.Equal(2, session.LoadDefinitions.Single(d => d.Tag == "РСН1").GetExpression().Terms.Count);
            session.Undo();
            Assert.Contains(settle, session.LoadCases);
            Assert.Equal(3, session.LoadDefinitions.Single(d => d.Tag == "РСН1").GetExpression().Terms.Count);
            Assert.Single(session.KinematicLoads.Where(k => k.Dof == 3));

            // Запись схемы даёт загружениям новые Id — пакет и несовершенства идут следом, роль сохраняется.
            Assert.True(FemSchemaAuthoring.Save(db, session, out _));
            var saved = FemAnalysisParams.Parse(db.GetFemAnalyses(schema.Id).Single(a => a.Tag == "Пакет").ParamsJson).Batch!;
            var term = FemLoadExpression.Parse(saved.Items[0].LoadExpressionJson).Terms.Single();
            Assert.Equal((snow.Id, "leading", FemLoadTerm.AutoMode), (term.LoadCaseId, term.Role, term.Mode));
            Assert.Equal(imp.Id, saved.Imperfections.Single().LoadCaseId);
            Assert.Equal(imp.Id, db.GetFemImperfections(schema.Id).Single().LoadCaseId);

            // Пустая комбинация после удаления единственного загружения — удаляется вместе с ним.
            var only = new FemLoadDefinition { SchemaId = schema.Id, Tag = "Только осадка" };
            only.SetExpression(new FemLoadExpression { Mode = FemLoadExpressionMode.Single, Terms = [new FemLoadTerm { LoadCaseId = settle.Id, Coefficient = 1 }] });
            session.Execute(new AddLoadDefinitionCommand(only));
            Assert.Equal(["Только осадка"], FemLoadAuthoring.DeleteImpact(db, session, settle).Emptied);
            session.Execute(FemLoadAuthoring.DeleteCommand(session, settle));
            Assert.DoesNotContain(only, session.LoadDefinitions);
        }
    }

    [Fact]
    public async Task Service_MemberLoadOffsets_AndImperfectionSave()
    {
        string project = await Seeded();
        var (db, schema) = Open(project);
        using (db)
        {
            var session = FemSchemaAuthoring.OpenSession(db, schema);
            int bar = session.Members.Single(m => m.ElemTag == "Р1").Id;
            Assert.Equal(6, FemLoadAuthoring.MemberLength(session, bar)!.Value, 9);
            Assert.Null(FemLoadAuthoring.MemberLoadOffsetError(session, new FemMemberLoad { MemberId = bar, StartOffsetM = 2, EndOffsetM = 3.9 }));
            Assert.NotNull(FemLoadAuthoring.MemberLoadOffsetError(session, new FemMemberLoad { MemberId = bar, StartOffsetM = 2, EndOffsetM = 4 }));
            Assert.Null(FemLoadAuthoring.MemberLoadOffsetError(session, new FemMemberLoad { MemberId = bar, DistributionType = "point", StartOffsetM = 6 }));
            Assert.NotNull(FemLoadAuthoring.MemberLoadOffsetError(session, new FemMemberLoad { MemberId = bar, StartOffsetM = -0.1 }));

            var imp = session.LoadCases.Single(c => c.Tag == "Несоверш. X");
            var post = session.LoadCases.Single(c => c.Tag == "Пост");
            var item = new CScore.Fem.Imperfections.FemImperfection
            {
                SchemaId = schema.Id, LoadCaseId = imp.Id, Kind = "bow", Tag = "крен x",
                ParamsJson = new CScore.Fem.Imperfections.FemBowParams { MemberTags = ["К1"], LOverE0 = 300 }.ToJson(),
            };
            Assert.Contains("уже есть", FemLoadAuthoring.SaveImperfection(db, item));          // имя без учёта регистра
            item.Tag = "Погибь К1";
            Assert.Null(FemLoadAuthoring.SaveImperfection(db, item));
            item.Id = 0; item.Tag = "Погибь 2"; item.LoadCaseId = post.Id;
            Assert.NotNull(FemLoadAuthoring.SaveImperfection(db, item));                         // не загружение-несовершенство
            item.LoadCaseId = imp.Id;
            item.ParamsJson = new CScore.Fem.Imperfections.FemBowParams().ToJson();
            Assert.NotNull(FemLoadAuthoring.SaveImperfection(db, item));                         // пустой охват
            Assert.Equal(2, db.GetFemImperfections(schema.Id).Count);
        }
    }
}
