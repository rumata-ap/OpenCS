using System.IO;
using System.Text.Json.Nodes;
using CScore;
using CScore.Fem;
using CScore.Sp16;
using OpenCS.Cli;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Подраздел 7б: проверки по КЭ 1-й и 2-й ГПС — вид <c>femChecks</c> документа opencs-model (сталь, ЖБ стержни, ЖБ
/// пластины по прочности и трещинам; ссылки по тегам, кругооборот, неизменённое не перезаписывается, смена входа
/// удаляет результат) и служба <see cref="FemCheckAuthoring"/>.
/// </summary>
[Collection("Loc language")]
public sealed class CliFemChecksTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_cli_checks_{Guid.NewGuid():N}");
    public CliFemChecksTests()
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

    /// <summary>Рама с плитой: колонны К1, К2 (группа «Колонны»), ригель Р1, плита П1 (группа «Плиты»); наборы стержней и пластин.</summary>
    const string Base = """
        "materials": [
          { "tag": "B25", "catalog": { "concrete": "B25" } },
          { "tag": "A500", "catalog": { "rebar": "A500" } } ],
        "sections": [ { "tag": "Б-1", "areas": [
          { "tag": "Бетон", "category": "region", "material": "B25", "diagram": "sp63",
            "polygon": { "outer": [[-200, -300], [200, -300], [200, 300], [-200, 300]] } } ] } ],
        "plateSections": [ { "tag": "Плита 200", "h": 200, "concrete": "B25", "rebar": "A500" } ],
        "forceSets": [
          { "tag": "Колонны N", "kind": "bar", "columns": ["label", "N", "Mx"], "rows": [ ["1", -100, 5] ] },
          { "tag": "Плита N", "kind": "shell", "columns": ["label", "Mx", "My", "Mxy"], "rows": [ ["1", 1, 2, 0] ] },
          { "tag": "Плита NL", "kind": "shell", "columns": ["label", "Mx", "My", "Mxy"], "rows": [ ["1", 0.5, 1, 0] ] } ],
        "femSchemas": [
          { "tag": "Рама",
            "nodes": [
              { "tag": 1, "x": 0, "y": 0, "z": 0, "fix": ["x", "y", "z", "ux", "uy", "uz"] },
              { "tag": 2, "x": 0, "y": 0, "z": 3 },
              { "tag": 3, "x": 6, "y": 0, "z": 3 },
              { "tag": 4, "x": 6, "y": 0, "z": 0, "fix": ["x", "y", "z", "ux", "uy", "uz"] } ],
            "members": [
              { "tag": "К1", "nodes": [1, 2], "section": "Б-1" },
              { "tag": "Р1", "nodes": [2, 3], "section": "Б-1" },
              { "tag": "К2", "nodes": [3, 4], "section": "Б-1" },
              { "tag": "П1", "type": "shell", "plateSection": "Плита 200",
                "region": { "outline": [[0, 0, 3], [6, 0, 3], [6, 4, 3], [0, 4, 3]] } } ],
            "groups": [ { "tag": "Колонны", "memberType": "column", "members": ["К1", "К2"] },
                        { "tag": "Плиты", "members": ["П1"] } ],
            "loadCases": [ { "tag": "Пост", "type": "permanent", "loads": [ { "member": "Р1", "q": [0, 0, -5] } ] } ],
            "combinations": [ { "tag": "РСН1", "type": "C", "terms": [ { "loadCase": "Пост", "k": 1.1 } ] } ] } ],
        "femAnalyses": [ { "schema": "Рама", "tag": "Длины", "kind": "csfeaBuckling", "combinations": [ { "combination": "РСН1" } ] } ]
        """;

    /// <summary>Четыре вида: сталь (по сетке и «Авто»), ЖБ стержень с η, плита ПС1, плита ПС2 слоистая с набором NL.</summary>
    const string Checks = """
        "femChecks": [
          { "schema": "Рама", "tag": "Колонны / сталь", "kind": "steel", "target": { "group": "Колонны" },
            "forceSets": ["Колонны N"], "calcType": "C",
            "steel": { "mesh": { "muX": 0.7, "muY": 1, "muB": 0 }, "auto": { "analysis": "Длины" } } },
          { "schema": "Рама", "tag": "Ригель / ЖБ", "kind": "rcBar", "target": { "member": "Р1" },
            "rebar": ["section", "selected"],
            "eta": { "iterative": true, "lengthM": 6, "muX": 1.2, "muY": 1, "psi": "manual", "psiX": 0.5, "psiY": 1 } },
          { "schema": "Рама", "tag": "Плиты / ПС1", "kind": "rcPlate", "target": { "group": "Плиты" },
            "plate": { "method": "capri", "strip": "formulas", "capriMode": "constraints" }, "rebar": ["section", "layout"] },
          { "schema": "Рама", "tag": "Плиты / ПС2", "kind": "rcPlateSls", "target": { "group": "Плиты" },
            "forceSets": ["Плита N"], "calcType": "N",
            "plate": { "method": "layered", "acrcLimMm": 0.2, "phi2": 0.8, "nlForceSet": "Плита NL" } } ]
        """;

    static string Full(string checks = Checks) =>
        "{ \"format\": \"opencs-model\", \"version\": 1, " + Base + (checks.Length > 0 ? ", " + checks : "") + " }";

    static string OnlyChecks(string checks) => "{ \"format\": \"opencs-model\", \"version\": 1, " + checks + " }";

    static DatabaseService Open(string project)
    {
        var db = new DatabaseService(project);
        db.LoadAll();
        return db;
    }

    static JsonArray Export(string file) => JsonNode.Parse(File.ReadAllText(file))!["femChecks"]!.AsArray();

    /// <summary>Записанный результат проверки (как после расчёта): строка calc_results со ссылкой на проверку.</summary>
    static void FakeResult(DatabaseService db, FemCheck check)
    {
        var r = new CalcResult { TaskKind = "fem_check", TaskTag = check.Tag, Created = "2026-11-26", Status = "ok", DataJson = "{}" };
        db.SaveCalcResultRaw(r, check.Id);
        check.ResultId = r.Id;
        db.SaveFemCheck(check);
    }

    [Fact]
    public async Task Apply_AllKinds_RefsByTag_DryRunNothing_RoundTripSame_ReapplyKeepsResult()
    {
        string project = PathOf("a.db");
        Assert.Equal(CliExitCode.Ok, (await Run("new", project)).Code);
        var dry = await Run("apply", project, Doc("dry.json", Full()), "--dry-run");
        Assert.True(dry.Code == CliExitCode.Ok, dry.Err + dry.Out);
        using (var db0 = Open(project)) Assert.Empty(db0.FemChecks);

        var r = await Run("apply", project, Doc("seed.json", Full()));
        Assert.True(r.Code == CliExitCode.Ok, r.Err + r.Out);
        string before;
        using (var db = Open(project))
        {
            var schema = db.FemSchemas.Single();
            var list = db.FemChecks.OrderBy(c => c.Id).ToList();
            Assert.Equal(["Колонны / сталь", "Ригель / ЖБ", "Плиты / ПС1", "Плиты / ПС2"], list.Select(c => c.Tag));
            Assert.Equal(["steel_check", "rc_check", "rc_plate_check", "rc_plate_check"], list.Select(c => c.NormCode));
            var cols = schema.MemberGroups.Single(g => g.Tag == "Колонны");
            var plates = schema.MemberGroups.Single(g => g.Tag == "Плиты");
            var sets = db.ForceSets.ToDictionary(f => f.Tag, f => f.Id);

            Assert.Equal((cols.Id, (int?)null, "C"), (list[0].MemberId, list[0].ElementId, list[0].CalcTypeOverride));
            Assert.Equal([sets["Колонны N"]], list[0].GetForceSetIds());
            var steel = SteelFemCheckParams.TryParse(list[0].ParamsJson)!;
            Assert.Equal((0.7, 1.0, 0.0), (steel.MeshLef!.MuX, steel.MeshLef.MuY, steel.MeshLef.MuB));
            Assert.Equal(db.GetFemAnalyses(schema.Id).Single(a => a.Tag == "Длины").Id, steel.AutoLef!.AnalysisId);

            Assert.Equal(db.GetFemMembers(schema.Id).Single(m => m.ElemTag == "Р1").Id, list[1].ElementId);
            Assert.True(list[1].IsAllSets);
            var bar = BarCheckParams.Parse(list[1].ParamsJson);
            Assert.Equal(["section", "selected"], bar.RebarSources);
            Assert.Equal((true, 6.0, 1.2, FemEtaParams.PsiManual, 0.5), (bar.Eta!.Iterative, bar.Eta.LengthM!.Value, bar.Eta.MuX, bar.Eta.PsiMode, bar.Eta.PsiX));

            var uls = PlateCheckParams.Parse(list[2].ParamsJson);
            Assert.Equal(("shell_simpl_capri_uls", "uls", StripModel.Formulas, CapriCheckMode.Constraints),
                (uls.Kind, uls.CheckGroup, uls.StripModel, uls.CapriCheckMode));
            Assert.Equal(["section", "layout"], uls.RebarSources);
            Assert.Null(uls.Eta);

            var sls = PlateCheckParams.Parse(list[3].ParamsJson);
            Assert.Equal(("shell_layered", "sls", 0.2, 0.8, sets["Плита NL"]), (sls.Kind, sls.CheckGroup, sls.AcrcLimMm, sls.Phi2, sls.NlForceSetId));
            Assert.Equal(plates.Id, list[3].MemberId);
            Assert.Equal(2, plates.Checks.Count);                     // проверки разложены по группам

            FakeResult(db, list[0]);
            before = string.Join("|", list.Select(c => c.ParamsJson + c.ForceSetIdsJson + c.MemberId + c.ElementId));
        }

        string e1 = PathOf("e1.json");
        Assert.Equal(CliExitCode.Ok, (await Run("export", "model", project, "--to", e1)).Code);
        var out1 = Export(e1);
        Assert.Equal(4, out1.Count);
        Assert.Equal("Длины", out1[0]!["steel"]!["auto"]!["analysis"]!.GetValue<string>());
        Assert.Equal("Р1", out1[1]!["target"]!["member"]!.GetValue<string>());
        Assert.Equal("all", out1[1]!["forceSets"]!.GetValue<string>());
        Assert.Null(out1[2]!["eta"]);
        Assert.Equal("Плита NL", out1[3]!["plate"]!["nlForceSet"]!.GetValue<string>());

        // Повторный apply выгрузки проверок: ничего не перезаписано, результат проверки на месте.
        string only = PathOf("only.json");
        Assert.Equal(CliExitCode.Ok, (await Run("export", "model", project, "--to", only, "--only", "femChecks")).Code);
        var again = await Run("apply", project, only);
        Assert.True(again.Code == CliExitCode.Ok, again.Err + again.Out);
        using (var db = Open(project))
        {
            var list = db.FemChecks.OrderBy(c => c.Id).ToList();
            Assert.Equal(before, string.Join("|", list.Select(c => c.ParamsJson + c.ForceSetIdsJson + c.MemberId + c.ElementId)));
            Assert.NotNull(list[0].ResultId);
            Assert.NotNull(db.GetCalcResultHeaderByFemCheck(list[0].Id));
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
    public async Task Apply_ChangedInput_ResetsResult_TargetChangePersisted_UnspecifiedKept()
    {
        string project = PathOf("c.db");
        Assert.Equal(CliExitCode.Ok, (await Run("new", project)).Code);
        Assert.Equal(CliExitCode.Ok, (await Run("apply", project, Doc("seed.json", Full()))).Code);
        using (var db = Open(project))
            foreach (var c in db.FemChecks.ToList()) FakeResult(db, c);

        // Сталь — другая цель (элемент К1), ЖБ — только наборы; ПС2 не упомянута.
        var r = await Run("apply", project, Doc("edit.json", OnlyChecks("""
            "femChecks": [
              { "schema": "Рама", "tag": "Колонны / сталь", "kind": "steel", "target": { "member": "К1" } },
              { "schema": "Рама", "tag": "Ригель / ЖБ", "kind": "rcBar", "forceSets": ["Колонны N"] } ]
            """)));
        Assert.True(r.Code == CliExitCode.Ok, r.Err + r.Out);
        using (var db = Open(project))
        {
            var schema = db.FemSchemas.Single();
            var byTag = db.FemChecks.ToDictionary(c => c.Tag);
            var steel = byTag["Колонны / сталь"];
            Assert.Equal((0, db.GetFemMembers(schema.Id).Single(m => m.ElemTag == "К1").Id), (steel.MemberId, steel.ElementId!.Value));
            Assert.Equal(0.7, SteelFemCheckParams.TryParse(steel.ParamsJson)!.MeshLef!.MuX);   // не указано — не меняется
            Assert.Null(steel.ResultId);
            Assert.Null(db.GetCalcResultHeaderByFemCheck(steel.Id));
            Assert.Null(byTag["Ригель / ЖБ"].ResultId);
            Assert.Equal(1.2, BarCheckParams.Parse(byTag["Ригель / ЖБ"].ParamsJson).Eta!.MuX);
            Assert.NotNull(byTag["Плиты / ПС2"].ResultId);                                 // не затронута
            Assert.DoesNotContain(steel, schema.MemberGroups.Single(g => g.Tag == "Колонны").Checks);

            // η выключена null, calcType — обратно «по набору».
        }
        var off = await Run("apply", project, Doc("off.json", OnlyChecks("""
            "femChecks": [ { "schema": "Рама", "tag": "Ригель / ЖБ", "kind": "rcBar", "eta": null, "calcType": null },
                           { "schema": "Рама", "tag": "Колонны / сталь", "kind": "steel", "steel": { "mesh": null } } ]
            """)));
        Assert.True(off.Code == CliExitCode.Ok, off.Err + off.Out);
        using (var db = Open(project))
        {
            var byTag = db.FemChecks.ToDictionary(c => c.Tag);
            Assert.Null(BarCheckParams.Parse(byTag["Ригель / ЖБ"].ParamsJson).Eta);
            var steel = SteelFemCheckParams.TryParse(byTag["Колонны / сталь"].ParamsJson)!;
            Assert.Null(steel.MeshLef);
            Assert.NotNull(steel.AutoLef);
        }
    }

    [Fact]
    public async Task Apply_Errors_WithPaths_NothingWritten()
    {
        string project = PathOf("e.db");
        Assert.Equal(CliExitCode.Ok, (await Run("new", project)).Code);
        var bad = await Run("apply", project, Doc("bad.json", Full("""
            "femChecks": [
              { "schema": "Рама", "tag": "А", "kind": "steel", "target": { "group": "Нет" } },
              { "schema": "Рама", "tag": "Б", "kind": "rcBar", "target": { "member": "Р1" }, "eta": { "muX": 0 } },
              { "schema": "Рама", "tag": "В", "kind": "rcPlateSls", "target": { "group": "Плиты" },
                "forceSets": ["Колонны N"], "calcType": "C", "plate": { "method": "layered", "acrcLimMm": -1, "ltFraction": 2 } },
              { "schema": "Рама", "tag": "Г", "kind": "что-то" },
              { "schema": "Нет", "tag": "Д", "kind": "steel", "target": { "group": "Колонны" } },
              { "schema": "Рама", "tag": "Б", "kind": "steel", "target": { "group": "Колонны" } },
              { "schema": "Рама", "tag": "Е", "kind": "steel", "target": { "group": "Колонны" }, "steel": { "auto": { "analysis": "Нет" } } },
              { "schema": "Рама", "tag": "Ж", "kind": "rcPlate", "target": { "group": "Плиты" }, "rebar": ["чужой"] },
              { "schema": "Рама", "tag": "З", "kind": "steel" },
              { "schema": "Рама", "tag": "И", "kind": "steel", "target": { "group": "Колонны" }, "forceSets": [] } ]
            """)));
        Assert.Equal(CliExitCode.Usage, bad.Code);
        string all = bad.Err + bad.Out;
        foreach (var path in new[] { "femChecks[0].target.group", "femChecks[1].eta.muX", "femChecks[2].forceSets[0]",
                     "femChecks[2].calcType", "femChecks[2].plate.acrcLimMm", "femChecks[2].plate.ltFraction", "femChecks[3].kind",
                     "femChecks[4].schema", "femChecks[5].tag", "femChecks[6].steel.auto.analysis", "femChecks[7].rebar[0]",
                     "femChecks[8].target", "femChecks[9].forceSets" })
            Assert.Contains(path, all);
        using var db = Open(project);
        Assert.Empty(db.FemSchemas);   // при ошибках не записано ничего
    }

    /// <summary>Ручная проверка 7б, H: проверка, переведённая с одного КонЭ на другой, уходит из узла прежнего в дереве.</summary>
    [Fact]
    public async Task Retarget_MovesCheckBetweenMemberTreeNodes()
    {
        string project = PathOf("t.db");
        Assert.Equal(CliExitCode.Ok, (await Run("new", project)).Code);
        Assert.Equal(CliExitCode.Ok, (await Run("apply", project, Doc("seed.json", Full()))).Code);
        using var db = Open(project);
        var schema = db.FemSchemas.Single();
        var tree = new OpenCS.ViewModels.FemSchemaTreeVM(schema, db, []);
        var bars = tree.ElementsSubNode.Bars.Members;
        var check = db.FemChecks.Single(c => c.Tag == "Ригель / ЖБ");
        Assert.Equal([check], bars.Single(i => i.Member.ElemTag == "Р1").Checks);

        var draft = FemCheckAuthoring.Copy(check);
        draft.ElementId = bars.Single(i => i.Member.ElemTag == "К1").Member.Id;
        FemCheckAuthoring.Save(db, check, draft);
        Assert.Empty(bars.Single(i => i.Member.ElemTag == "Р1").Checks);
        Assert.Equal([check], bars.Single(i => i.Member.ElemTag == "К1").Checks);
    }

    // ── Служба ─────────────────────────────────────────────────────────────────────────────

    static FemCheckRefs TestRefs(params FemCheck[] checks)
    {
        var s = new FemSchema { Id = 1, Tag = "С" };
        s.MemberGroups.Add(new FemMemberGroup { Id = 3, SchemaId = 1, Tag = "Колонны" });
        s.Analyses.Add(new FemAnalysis { Id = 5, SchemaId = 1, Tag = "Длины" });
        return new FemCheckRefs(s, checks, [new FemMember { Id = 7, SchemaId = 1, ElemTag = "Р1" }],
            [new ForceSet { Id = 11, Tag = "Стержни", Kind = "bar" }, new ForceSet { Id = 12, Tag = "Пластины", Kind = "shell" }],
            s.Analyses);
    }

    [Fact]
    public void Service_Tag_DuplicateEmpty_FreeTag_AutoTag()
    {
        var other = new FemCheck { Id = 2, SchemaId = 1, Tag = "Колонны / Сталь (СП 16)" };
        var elsewhere = new FemCheck { Id = 3, SchemaId = 9, Tag = "Чужая" };
        FemCheck[] checks = [other, elsewhere];
        Assert.NotNull(FemCheckAuthoring.TagError(checks, 1, " колонны / сталь (сп 16) "));
        Assert.Null(FemCheckAuthoring.TagError(checks, 1, other.Tag, selfId: 2));
        Assert.Null(FemCheckAuthoring.TagError(checks, 1, "Чужая"));                // другая схема
        Assert.NotNull(FemCheckAuthoring.TagError(checks, 1, " "));
        string auto = FemCheckAuthoring.AutoTag("Колонны", FemCheckAuthoring.Steel, null);
        Assert.Equal(other.Tag, auto);
        Assert.Equal(auto + " (2)", FemCheckAuthoring.FreeTag(checks, 1, auto));
        Assert.Equal("Плиты / Wood–Armer — трещины (ПС2)", FemCheckAuthoring.AutoTag("Плиты", FemCheckAuthoring.RcPlate, "shell_simpl_wa_sls", sls: true));
    }

    [Fact]
    public void Service_Validate_Paths()
    {
        var ok = new FemCheck
        {
            SchemaId = 1, MemberId = 3, Tag = "А", NormCode = FemCheckAuthoring.RcBar, ForceSetIdsJson = "[11]",
            ParamsJson = new BarCheckParams { Eta = new FemEtaParams { Enabled = true } }.ToJson(),
        };
        Assert.Empty(FemCheckAuthoring.Validate(TestRefs(), ok));

        var bad = FemCheckAuthoring.Copy(ok);
        bad.MemberId = 99;
        bad.ForceSetIdsJson = "[12,40]";
        bad.CalcTypeOverride = "X";
        bad.ParamsJson = new BarCheckParams
        {
            RebarSources = ["layout"], Eta = new FemEtaParams { Enabled = true, MuX = 0, PsiY = -1, LengthM = 0 },
        }.ToJson();
        Assert.Equal(["calcType", "eta.lengthM", "eta.muX", "eta.psiY", "forceSets[0]", "forceSets[1]", "rebar[0]", "target"],
            FemCheckAuthoring.Validate(TestRefs(), bad).Select(e => e.Path).Order());

        var sls = new FemCheck
        {
            SchemaId = 1, ElementId = 7, Tag = "Б", NormCode = FemCheckAuthoring.RcPlate, CalcTypeOverride = "C",
            ParamsJson = new PlateCheckParams { Kind = "shell_simpl_wa_uls", CheckGroup = "sls", Phi1Mode = "manual", Phi1 = 0, Phi2 = 0 }.ToJson(),
        };
        Assert.Equal(["calcType", "plate.method", "plate.phi1", "plate.phi2"],
            FemCheckAuthoring.Validate(TestRefs(), sls).Select(e => e.Path).Order());

        var steel = new FemCheck
        {
            SchemaId = 1, MemberId = 3, Tag = "В", NormCode = FemCheckAuthoring.Steel,
            ParamsJson = new SteelFemCheckParams { MeshLef = new SteelMeshLef { MuB = -1 }, AutoLef = new SteelAutoLef { AnalysisId = 6 } }.ToJson(),
        };
        Assert.Equal(["steel.analysis", "steel.muB"], FemCheckAuthoring.Validate(TestRefs(), steel).Select(e => e.Path).Order());
    }

    [Fact]
    public void Service_ApplyEdit_RenameKeepsResult_SameContentKeepsText_InputChange()
    {
        // Старая запись без новых полей: тот же смысл другим текстом — не изменение.
        var saved = new FemCheck
        {
            Id = 1, SchemaId = 1, MemberId = 3, Tag = "А", NormCode = FemCheckAuthoring.RcPlate, ResultId = 42,
            ParamsJson = "{\"Kind\":\"shell_layered\",\"CheckGroup\":\"uls\"}",
        };
        var same = FemCheckAuthoring.Copy(saved);
        same.ParamsJson = PlateCheckParams.Parse(saved.ParamsJson).ToJson();
        Assert.Equal((false, false), FemCheckAuthoring.ApplyEdit(saved, same));
        Assert.Equal("{\"Kind\":\"shell_layered\",\"CheckGroup\":\"uls\"}", saved.ParamsJson);

        var renamed = FemCheckAuthoring.Copy(saved);
        renamed.Tag = "Б";
        Assert.Equal((true, false), FemCheckAuthoring.ApplyEdit(saved, renamed));

        var retarget = FemCheckAuthoring.Copy(saved);
        retarget.MemberId = 0;
        retarget.ElementId = 7;
        Assert.Equal((true, true), FemCheckAuthoring.ApplyEdit(saved, retarget));
        Assert.Equal((0, 7), (saved.MemberId, saved.ElementId!.Value));
    }

    [Fact]
    public async Task Service_Save_DbResetsResult_RoutesGroups_DeleteAsks()
    {
        string project = PathOf("s.db");
        Assert.Equal(CliExitCode.Ok, (await Run("new", project)).Code);
        Assert.Equal(CliExitCode.Ok, (await Run("apply", project, Doc("seed.json", Full()))).Code);
        using (var db = Open(project))
        {
            var schema = db.FemSchemas.Single();
            var cols = schema.MemberGroups.Single(g => g.Tag == "Колонны");
            var check = db.FemChecks.Single(c => c.Tag == "Колонны / сталь");
            FakeResult(db, check);

            // Переименование: результат на месте.
            var draft = FemCheckAuthoring.Copy(check);
            draft.Tag = "Сталь К";
            Assert.Equal((true, false), FemCheckAuthoring.Save(db, check, draft));
            Assert.NotNull(db.GetCalcResultHeaderByFemCheck(check.Id));

            // Другая цель — результат удалён, проверка ушла из группы.
            draft = FemCheckAuthoring.Copy(check);
            draft.MemberId = 0;
            draft.ElementId = db.GetFemMembers(schema.Id).Single(m => m.ElemTag == "К2").Id;
            Assert.Equal((true, true), FemCheckAuthoring.Save(db, check, draft));
            Assert.Null(check.ResultId);
            Assert.Null(db.GetCalcResultHeaderByFemCheck(check.Id));
            Assert.DoesNotContain(check, cols.Checks);

            var notifier = new RecordingNotifier { ConfirmAnswer = false };
            Assert.False(FemCheckAuthoring.Delete(db, check, notifier));
            Assert.Contains("Сталь К", notifier.Questions.Single());
            Assert.Contains(check, db.FemChecks);
        }
        using (var db = Open(project))
        {
            var check = db.FemChecks.Single(c => c.Tag == "Сталь К");
            Assert.True(check.TargetsElement);                        // цель записана в базу
            var withResult = db.FemChecks.Single(c => c.Tag == "Ригель / ЖБ");
            FakeResult(db, withResult);
            var notifier = new RecordingNotifier { ConfirmAnswer = true };
            Assert.True(FemCheckAuthoring.Delete(db, withResult, notifier));
            Assert.Contains(Loc.S("FemCheckDeleteResult"), notifier.Questions.Single());
            Assert.DoesNotContain(withResult, db.FemChecks);
        }
    }
}
