using System.IO;
using System.Text.Json.Nodes;
using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using OpenCS.Cli;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Раздел «Фрагменты»: вид <c>femFragments</c> документа opencs-model (рецепт фрагмента и ГУ разрезов, сборка на этапе
/// проверки, сверка рецепта, пропуск при родителе из того же документа) и команда <c>opencs fragment masks</c>.
/// Родитель — схема «Каркас» фикстуры <see cref="StoreyFragmentDbFixture"/> (колонна C1, плиты S1 на z = 3 и S2 на z = 9).
/// </summary>
[Collection("Loc language")]
public sealed class CliFemFragmentsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_cli_fragments_{Guid.NewGuid():N}");
    public CliFemFragmentsTests()
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

    string Doc(string name, string body)
    {
        File.WriteAllText(PathOf(name), "{ \"format\": \"opencs-model\", \"version\": 1, " + body + " }");
        return PathOf(name);
    }

    /// <summary>Новый проект с родителем «Каркас».</summary>
    async Task<string> Project(string name)
    {
        string project = PathOf(name);
        Assert.Equal(CliExitCode.Ok, (await Run("new", project)).Code);
        using (var db = new DatabaseService(project)) StoreyFragmentDbFixture.CreateParent(db);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        return project;
    }

    static List<FemSchema> Fragments(string project)
    {
        using var db = new DatabaseService(project);
        db.LoadAll();
        return db.FemSchemas.Where(s => s.ParentSchemaId != null).OrderBy(s => s.Id).ToList();
    }

    async Task<JsonArray> ExportFragments(string project, string name)
    {
        var r = await Run("export", "model", project, "--to", PathOf(name), "--only", "femFragments");
        Assert.True(r.Code == CliExitCode.Ok, r.Err + r.Out);
        return (JsonNode.Parse(File.ReadAllText(PathOf(name)))!["femFragments"] as JsonArray)!;
    }

    const string StoreyAndPrism = """
        "femFragments": [
          { "tag": "Этаж", "parent": "Каркас", "storey": { "z1": 1.5, "z2": 4.5 } },
          { "tag": "Колонна", "parent": "Каркас", "prism": { "members": ["C1"] }, "keepParentMesh": false } ]
        """;

    [Fact]
    public async Task Apply_StoreyAndPrism_Builds_ExportRoundTrip_ReapplyUnchanged()
    {
        string project = await Project("a.db");
        var dry = await Run("apply", project, Doc("dry.json", StoreyAndPrism), "--dry-run");
        Assert.True(dry.Code == CliExitCode.Ok, dry.Err + dry.Out);
        Assert.Empty(Fragments(project));

        var r = await Run("apply", project, Doc("doc.json", StoreyAndPrism));
        Assert.True(r.Code == CliExitCode.Ok, r.Err + r.Out);
        var fragments = Fragments(project);
        Assert.Equal(["Этаж", "Колонна"], fragments.Select(s => s.Tag));
        Assert.Equal((1.5, 4.5), (fragments[0].FragmentZ1!.Value, fragments[0].FragmentZ2!.Value));
        Assert.Null(fragments[0].FragmentPrismJson);
        Assert.False(fragments[1].FragmentKeepParentMesh);
        Assert.NotNull(FragmentPrismCodec.Decode(fragments[1].FragmentPrismJson));

        var exported = await ExportFragments(project, "out.json");
        Assert.Equal(2, exported.Count);
        Assert.Equal(1.5, (double)exported[0]!["storey"]!["z1"]!);
        Assert.Equal("C1", (string)exported[1]!["prism"]!["members"]![0]!);
        Assert.NotNull(exported[1]!["prism"]!["floorFrom"]);
        Assert.False((bool)exported[1]!["keepParentMesh"]!);

        // Повторный apply выгрузки — тот же рецепт: ничего не меняется и не пересобирается.
        string again = Doc("again.json", "\"femFragments\": " + exported.ToJsonString());
        var second = await Run("apply", project, again);
        Assert.True(second.Code == CliExitCode.Ok, second.Err + second.Out);
        Assert.Equal(fragments.Select(s => s.Id), Fragments(project).Select(s => s.Id));

        // Кругооборот в другой проект с тем же родителем.
        string other = await Project("b.db");
        var third = await Run("apply", other, again);
        Assert.True(third.Code == CliExitCode.Ok, third.Err + third.Out);
        Assert.Equal(exported.ToJsonString(), (await ExportFragments(other, "out2.json")).ToJsonString());
    }

    [Fact]
    public async Task Apply_FloorKey_BandAsInWindow()
    {
        string project = await Project("a.db");
        var r = await Run("apply", project, Doc("doc.json", """
            "femFragments": [ { "tag": "Этаж +3", "parent": "Каркас", "storey": { "floor": 3 } } ]
            """));
        Assert.True(r.Code == CliExitCode.Ok, r.Err + r.Out);
        var f = Assert.Single(Fragments(project));
        using (var db = new DatabaseService(project))
        {
            db.LoadAll();
            var context = StoreyFloorService.Read(db, db.FemSchemas.Single(s => s.Tag == "Каркас").Id);
            var band = StoreyFloorLevels.Band(context.Levels, StoreyFloorLevels.LevelAt(context.Levels, 3)!.Value, context.ZMin, context.ZMax);
            Assert.Equal(band.Z1, f.FragmentZ1!.Value, 9);
            Assert.Equal(band.Z2, f.FragmentZ2!.Value, 9);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var missing = await Run("apply", project, Doc("bad.json", """
            "femFragments": [ { "tag": "Нет", "parent": "Каркас", "storey": { "floor": 5 } } ]
            """));
        Assert.Equal(CliExitCode.Usage, missing.Code);
        Assert.Contains("femFragments[0].storey.floor", missing.Err);
    }

    [Fact]
    public async Task Apply_Errors_WithPath_NothingWritten()
    {
        string project = await Project("a.db");
        Assert.Equal(CliExitCode.Ok, (await Run("apply", project, Doc("doc.json", StoreyAndPrism))).Code);
        int before = Fragments(project).Count;

        // Другой рецепт у собранного фрагмента; отказ построителя (z₁ по плите S1); имя занято не фрагментом;
        // неизвестный родитель; обе формы сразу; КонЭ нет в родителе.
        var r = await Run("apply", project, Doc("bad.json", """
            "femFragments": [
              { "tag": "Этаж", "parent": "Каркас", "storey": { "z1": 1.5, "z2": 5 } },
              { "tag": "Отказ", "parent": "Каркас", "storey": { "z1": 3, "z2": 6 } },
              { "tag": "Каркас", "parent": "Этаж", "storey": { "z1": 1.5, "z2": 4.5 } },
              { "tag": "Сирота", "parent": "Нет такой", "storey": { "z1": 1, "z2": 2 } },
              { "tag": "Обе", "parent": "Каркас", "storey": { "z1": 1, "z2": 2 }, "prism": { "members": ["C1"] } },
              { "tag": "Колонна 2", "parent": "Каркас", "prism": { "members": ["C9"] } } ]
            """));
        Assert.Equal(CliExitCode.Usage, r.Code);
        Assert.Contains("femFragments[0]: ", r.Err);
        Assert.Contains(string.Format(Loc.S("CliFragErrRecipeChanged"), "Этаж", "storey: 1.5…4.5 → 1.5…5"), r.Err);
        Assert.Contains("femFragments[1]: " + string.Format(Loc.S("CliFragErrRefused"), "").TrimEnd(), r.Err);
        Assert.Contains("femFragments[2].tag", r.Err);
        Assert.Contains("femFragments[3].parent", r.Err);
        Assert.Contains("femFragments[4]: " + Loc.S("CliFragErrStoreyOrPrism"), r.Err);
        Assert.Contains("femFragments[5].prism.members[0]", r.Err);
        Assert.Equal(before, Fragments(project).Count);
    }

    [Fact]
    public async Task Apply_ParentInSameDocument_SkippedWithWarning_AnalysesToo()
    {
        string project = await Project("a.db");
        var r = await Run("apply", project, Doc("doc.json", """
            "femSchemas": [ { "tag": "Рама", "nodes": [ { "tag": 1, "x": 0, "y": 0, "z": 0, "fix": ["x","y","z","ux","uy","uz"] },
                                                         { "tag": 2, "x": 0, "y": 0, "z": 3 } ] } ],
            "femFragments": [ { "tag": "Рама · этаж", "parent": "Рама", "storey": { "z1": 1, "z2": 2 } } ],
            "femAnalyses": [ { "schema": "Рама · этаж", "tag": "Лин", "kind": "csfeaLinear", "loadCases": "all" } ]
            """));
        Assert.True(r.Code == CliExitCode.Ok, r.Err + r.Out);
        Assert.Contains(string.Format(Loc.S("CliFragSkippedParentInDoc"), "Рама · этаж", "Рама"), r.Err);
        Assert.Contains(string.Format(Loc.S("CliFragAnalysisSkipped"), "Рама · этаж", "Лин"), r.Err);
        Assert.Empty(Fragments(project));
    }

    [Fact]
    public async Task Apply_AnalysisOfNewFragment_SameDocument()
    {
        string project = await Project("a.db");
        var r = await Run("apply", project, Doc("doc.json", """
            "femFragments": [ { "tag": "Этаж", "parent": "Каркас", "storey": { "z1": 1.5, "z2": 4.5 } } ],
            "femAnalyses": [ { "schema": "Этаж", "tag": "Лин", "kind": "csfeaLinear", "loadCases": ["Пост.", "Полезная"] } ]
            """));
        Assert.True(r.Code == CliExitCode.Ok, r.Err + r.Out);
        using var db = new DatabaseService(project);
        db.LoadAll();
        var fragment = db.FemSchemas.Single(s => s.Tag == "Этаж");
        Assert.Contains(db.GetFemAnalyses(fragment.Id), a => a.Tag == "Лин");
    }

    [Fact]
    public async Task Apply_CutBc_PresetAndOverrides_ExportAndErrors()
    {
        string project = await Project("a.db");
        // Сначала — без ГУ: имя КонЭ разреза колонны во фрагменте берётся из собранной схемы.
        Assert.Equal(CliExitCode.Ok, (await Run("apply", project, Doc("plain.json", """
            "femFragments": [ { "tag": "Этаж", "parent": "Каркас", "storey": { "z1": 1.5, "z2": 4.5 } } ]
            """))).Code);
        string column;
        using (var db = new DatabaseService(project))
        {
            db.LoadAll();
            column = db.GetFragmentCutsWithIds(db.FemSchemas.Single(s => s.Tag == "Этаж").Id)
                .First(c => c.Cut.Side == FragmentCutSide.Bottom && c.Cut.Kind == FragmentCutKind.End).Cut.MemberTag;
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var r = await Run("apply", project, Doc("doc.json", """
            "femFragments": [ { "tag": "Этаж", "parent": "Каркас", "storey": { "z1": 1.5, "z2": 4.5 },
              "cutBc": { "preset": "static",
                         "overrides": [ { "member": "COLUMN", "side": "bottom", "modes": { "ux": "free", "rz": "fixed" } } ] } } ]
            """.Replace("COLUMN", column)));
        Assert.True(r.Code == CliExitCode.Ok, r.Err + r.Out);
        var f = Assert.Single(Fragments(project));
        Assert.Equal(nameof(FragmentBcPreset.Static), f.FragmentBcPreset);
        using (var db = new DatabaseService(project))
        {
            db.LoadAll();
            var cuts = db.GetFragmentCutsWithIds(f.Id);
            var bc = Assert.Single(db.GetFragmentCutBc(f.Id));
            var cut = cuts.Single(c => c.Id == bc.Key).Cut;
            Assert.Equal((column, FragmentCutSide.Bottom), (cut.MemberTag, cut.Side));
            Assert.Equal(FragmentDofMode.Free, FragmentCutBc.Decode(bc.Value[0]));
            Assert.Equal(FragmentCutBc.ByPreset, bc.Value[1]);
            Assert.Equal(FragmentDofMode.Fixed, FragmentCutBc.Decode(bc.Value[5]));
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var exported = await ExportFragments(project, "out.json");
        var bcJson = exported[0]!["cutBc"]!;
        Assert.Equal("static", (string)bcJson["preset"]!);
        Assert.Null(bcJson["lateralPreset"]);
        var ov = bcJson["overrides"]![0]!;
        Assert.Equal((column, "bottom", "free", "fixed"), ((string)ov["member"]!, (string)ov["side"]!, (string)ov["modes"]!["ux"]!, (string)ov["modes"]!["rz"]!));
        Assert.Null(ov["modes"]!["uy"]);

        // Пустой список правок снимает их; пресет вернуть по умолчанию; неизвестный разрез и режим — ошибки с путём.
        var reset = await Run("apply", project, Doc("reset.json", """
            "femFragments": [ { "tag": "Этаж", "parent": "Каркас", "storey": { "z1": 1.5, "z2": 4.5 },
              "cutBc": { "preset": "kinematic", "overrides": [] } } ]
            """));
        Assert.True(reset.Code == CliExitCode.Ok, reset.Err + reset.Out);
        var after = Assert.Single(Fragments(project));
        Assert.Null(after.FragmentBcPreset);
        using (var db = new DatabaseService(project)) Assert.Empty(db.GetFragmentCutBc(after.Id));

        var bad = await Run("apply", project, Doc("bad.json", """
            "femFragments": [ { "tag": "Этаж", "parent": "Каркас", "storey": { "z1": 1.5, "z2": 4.5 },
              "cutBc": { "preset": "hinge",
                         "overrides": [ { "member": "Z9", "side": "top", "modes": { "ux": "free" } },
                                        { "member": "COLUMN", "side": "bottom", "modes": { "ux": "loose" } } ] } } ]
            """.Replace("COLUMN", column)));
        Assert.Equal(CliExitCode.Usage, bad.Code);
        Assert.Contains("femFragments[0].cutBc.preset", bad.Err);
        Assert.Contains("femFragments[0].cutBc.overrides[1].modes.ux", bad.Err);
    }

    [Fact]
    public async Task FragmentMasks_ListSources_Errors_Delete()
    {
        string project = await Project("a.db");
        Assert.Equal(CliExitCode.Ok, (await Run("apply", project, Doc("doc.json", StoreyAndPrism))).Code);

        var list = await Run("fragment", "masks", project, "Этаж");
        Assert.Equal(CliExitCode.Usage, list.Code);
        Assert.Contains(Loc.S("CliFragMasksSources"), list.Out);
        Assert.Contains("--all", list.Out);

        var notFragment = await Run("fragment", "masks", project, "Каркас", "--delete");
        Assert.Equal(CliExitCode.Usage, notFragment.Code);
        Assert.Contains(string.Format(Loc.S("CliFragMasksNotFragment"), "Каркас"), notFragment.Err);

        var two = await Run("fragment", "masks", project, "Этаж", "--all", "--delete");
        Assert.Equal(CliExitCode.Usage, two.Code);

        var unknown = await Run("fragment", "masks", project, "Этаж", "--from", "Нет такой");
        Assert.Equal(CliExitCode.Usage, unknown.Code);

        var none = await Run("fragment", "masks", project, "Этаж", "--delete");
        Assert.True(none.Code == CliExitCode.Ok, none.Err + none.Out);
        Assert.Contains(string.Format(Loc.S("StoreyFragmentMasksNoneToDelete"), "Этаж"), none.Out);
    }

    [Fact]
    public async Task Export_SkipsMaskSupplyAnalyses_KeepsFragmentAnalyses()
    {
        string project = await Project("a.db");
        Assert.Equal(CliExitCode.Ok, (await Run("apply", project, Doc("doc.json", """
            "femFragments": [ { "tag": "Этаж", "parent": "Каркас", "storey": { "z1": 1.5, "z2": 4.5 } } ],
            "femAnalyses": [ { "schema": "Этаж", "tag": "Лин", "kind": "csfeaLinear", "loadCases": "all" } ]
            """))).Code);
        using (var db = new DatabaseService(project))
        {
            db.LoadAll();
            var parent = db.FemSchemas.Single(s => s.Tag == "Каркас");
            db.SaveFemAnalysis(new FemAnalysis { SchemaId = parent.Id, Tag = StoreyFragmentMaskSupply.AnalysisTag, Kind = FemCsfeaRunner.LinearAnalysisKind });
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var export = await Run("export", "model", project, "--to", PathOf("m.json"));
        Assert.True(export.Code == CliExitCode.Ok, export.Err);
        var doc = JsonNode.Parse(File.ReadAllText(PathOf("m.json")))!;
        var analyses = doc["femAnalyses"]!.AsArray();
        Assert.DoesNotContain(analyses, a => (string)a!["tag"]! == StoreyFragmentMaskSupply.AnalysisTag);
        Assert.Contains(analyses, a => (string)a!["schema"]! == "Этаж" && (string)a!["tag"]! == "Лин");
        // Фрагмент — в своём виде, не в femSchemas; предупреждения о пропуске нет только у родных схем.
        Assert.Single(doc["femFragments"]!.AsArray());
        Assert.DoesNotContain(doc["femSchemas"]!.AsArray(), s => (string)s!["tag"]! == "Этаж");
    }
}
