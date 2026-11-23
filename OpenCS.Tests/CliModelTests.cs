using System.IO;
using System.Text.Json.Nodes;
using OpenCS.Cli;
using OpenCS.Services;
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
        Assert.Contains("materials[2].custom", r.Err);
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

    const string DiagramsSeed = """
        { "format": "opencs-model", "version": 1,
          "diagrams": [
            { "tag": "Ручная C", "materialType": "concrete", "calcType": "C",
              "points": [[-0.0035, -14.5], [-0.002, -14.5], [0, 0], [0.0001, 1.05], [0.00015, 1.05]],
              "characteristic": [-0.002] },
            { "tag": "Ручная N", "materialType": "concrete", "calcType": "N", "type": "l2",
              "points": [[-0.0035, -18.5], [-0.002, -18.5], [0.0001, 1.35], [0.00015, 1.35]] } ],
          "materials": [
            { "tag": "Свой бетон", "type": "custom", "description": "по испытаниям",
              "custom": { "baseType": "concrete",
                          "diagrams": { "C": "Ручная C", "CL": "Ручная C", "N": "Ручная N", "NL": "Ручная N" } } } ] }
        """;

    static (List<CScore.Diagramm> Diagrams, List<CScore.Material> Materials) Pool(string project)
    {
        using var db = new DatabaseService(project);
        db.LoadAll();
        var r = (db.Diagrams.ToList(), db.Materials.ToList());
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        return r;
    }

    [Fact]
    public async Task Diagrams_CustomMaterial_Apply_KPaInBase_LinksById()
    {
        string project = await NewProject();
        var r = await Run("apply", project, Doc("d.json", DiagramsSeed));
        Assert.True(r.Code == CliExitCode.Ok, r.Err);
        var (diagrams, materials) = Pool(project);
        Assert.Equal(2, diagrams.Count);
        var c = diagrams.Single(d => d.Tag == "Ручная C");
        Assert.Equal(-14500, c.Ic.Y[0], 6);                    // МПа документа → кПа базы
        Assert.Equal(CScore.DiagrammType.L2, diagrams.Single(d => d.Tag == "Ручная N").Type);
        var m = Assert.Single(materials);
        Assert.Equal(CScore.MatType.Custom, m.Type);
        Assert.Equal(CScore.MatType.Concrete, m.BaseType);
        Assert.Equal(c.Id, m.CustomDiagramIds[CScore.CalcType.CL]);

        // Обновление диаграммы на месте: тот же Id, ссылка материала цела.
        var u = await Run("apply", project, Doc("u.json", """
            { "format": "opencs-model", "version": 1, "diagrams": [
              { "tag": "Ручная C", "materialType": "concrete", "points": [[-0.003, -20], [0.0001, 1]] } ] }
            """));
        Assert.True(u.Code == CliExitCode.Ok, u.Err);
        var (after, mats) = Pool(project);
        var c2 = after.Single(d => d.Tag == "Ручная C");
        Assert.Equal(c.Id, c2.Id);
        Assert.Equal(-20000, c2.Ic.Y[0], 6);
        Assert.Equal(c.Id, mats.Single().CustomDiagramIds[CScore.CalcType.C]);
    }

    [Fact]
    public async Task Diagrams_Errors_WithPaths_NothingWritten()
    {
        string project = await NewProject();
        var r = await Run("apply", project, Doc("bad.json", """
            { "format": "opencs-model", "version": 1,
              "diagrams": [
                { "tag": "", "materialType": "concrete", "points": [[-0.001, -10], [0.001, 1]] },
                { "tag": "D2", "materialType": "concrete", "points": [[-0.001, -10], [-0.001, -12], [0.001, 1]] },
                { "tag": "D3", "materialType": "wood", "points": [[-0.001, -10], [0.001, 1]] },
                { "tag": "D4", "materialType": "steel", "points": [[0.001, 1]] },
                { "tag": "D5", "materialType": "steel", "points": [[-0.001, -10], ["x", 1]] } ],
              "materials": [
                { "tag": "M", "type": "custom", "custom": { "baseType": "concrete",
                  "diagrams": { "C": "нет такой", "CL": "D2", "N": "D2" } } } ] }
            """));
        Assert.Equal(CliExitCode.Usage, r.Code);
        Assert.Contains("diagrams[0].tag", r.Err);
        Assert.Contains("diagrams[1].points[", r.Err);
        Assert.Contains("diagrams[2].materialType", r.Err);
        Assert.Contains("diagrams[3].points", r.Err);
        Assert.Contains("diagrams[4]", r.Err);
        Assert.Contains("materials[0].custom.diagrams.NL", r.Err);   // не задана
        Assert.Contains("materials[0].custom.diagrams.C", r.Err);    // нет такой
        var (diagrams, materials) = Pool(project);
        Assert.Empty(diagrams);
        Assert.Empty(materials);
    }

    [Fact]
    public async Task Diagrams_DryRun_WritesNothing()
    {
        string project = await NewProject();
        var r = await Run("apply", project, Doc("d.json", DiagramsSeed), "--dry-run");
        Assert.True(r.Code == CliExitCode.Ok, r.Err);
        Assert.Contains("Ручная C", r.Out);
        var (diagrams, materials) = Pool(project);
        Assert.Empty(diagrams);
        Assert.Empty(materials);
    }

    [Fact]
    public async Task Diagrams_RoundTrip_ExportApplyExport_Same()
    {
        string first = await NewProject("a.db");
        var seed = await Run("apply", first, Doc("seed.json", DiagramsSeed));
        Assert.True(seed.Code == CliExitCode.Ok, seed.Err);

        string exported = PathOf("e1.json");
        var ex = await Run("export", "model", first, "--to", exported);
        Assert.True(ex.Code == CliExitCode.Ok, ex.Err);
        Assert.DoesNotContain("custom", ex.Err);   // custom больше не пропускается

        string second = await NewProject("b.db");
        var apply = await Run("apply", second, exported);
        Assert.True(apply.Code == CliExitCode.Ok, apply.Err);
        string exported2 = PathOf("e2.json");
        Assert.Equal(CliExitCode.Ok, (await Run("export", "model", second, "--to", exported2)).Code);

        var a = JsonNode.Parse(File.ReadAllText(exported))!;
        var b = JsonNode.Parse(File.ReadAllText(exported2))!;
        Assert.Equal(2, a["diagrams"]!.AsArray().Count);
        Assert.Single(a["materials"]!.AsArray());
        Assert.Equal(-14.5, (double)a["diagrams"]![0]!["points"]![0]![1]!, 9);
        AssertJsonClose(a, b, "$");
    }

    [Fact]
    public async Task Schema_Diagrams_IsJson()
    {
        var r = await Run("schema", "diagrams");
        Assert.True(r.Code == CliExitCode.Ok, r.Err);
        Assert.NotNull(JsonNode.Parse(r.Out)!["properties"]!["points"]);
    }

    [Fact]
    public async Task Schema_Materials_IsJson()
    {
        var r = await Run("schema", "materials");
        Assert.True(r.Code == CliExitCode.Ok, r.Err);
        Assert.NotNull(JsonNode.Parse(r.Out)!["properties"]);
    }

    const string ContoursSeed = """
        { "format": "opencs-model", "version": 1,
          "contours": [
            { "tag": "Колонна 400×600", "points": [[-200, -300], [200, -300], [200, 300], [-200, 300]] },
            { "tag": "Отверстие", "set": "Плита", "type": "hole", "points": [[0, 0], [100, 0], [100, 100], [0, 100], [0, 0]] } ],
          "circles": [
            { "tag": "Ø25", "x": -150, "y": -250, "d": 25 },
            { "tag": "Ø25", "x": 150, "y": -250, "d": 25 } ] }
        """;

    static (List<CScore.Contour> Contours, List<CScore.CircleP> Circles) Geometry(string project)
    {
        using var db = new DatabaseService(project);
        db.LoadAll();
        var r = (db.Contours.ToList(), db.Circles.ToList());
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        return r;
    }

    [Fact]
    public async Task Contours_Circles_Apply_MetresInBase_UpdateInPlace()
    {
        string project = await NewProject();
        var r = await Run("apply", project, Doc("c.json", ContoursSeed));
        Assert.True(r.Code == CliExitCode.Ok, r.Err);
        var (contours, circles) = Geometry(project);
        Assert.Equal(2, contours.Count);
        var col = contours.Single(c => c.Tag == "Колонна 400×600");
        Assert.True(col.IsClosed);
        Assert.Equal(5, col.Points.Count);
        Assert.Equal(0.24, Math.Abs(new CScore.GeoProps(col).A), 9);    // мм документа → м базы
        var hole = contours.Single(c => c.Tag == "Отверстие");
        Assert.Equal(CScore.ContourType.Hole, hole.Type);
        Assert.Equal("Плита", hole.GeometrySet);
        Assert.Equal(5, hole.Points.Count);                              // замыкающая из документа не удвоена
        Assert.Equal(2, circles.Count);
        Assert.All(circles, c => Assert.Equal(0.0125, c.Radius, 12));

        // Обновление на месте: тот же Id, новые вершины; круг по центру — новый диаметр.
        var u = await Run("apply", project, Doc("u.json", """
            { "format": "opencs-model", "version": 1,
              "contours": [ { "tag": "Колонна 400×600", "points": [[-200, -400], [200, -400], [200, 400], [-200, 400]] } ],
              "circles": [ { "tag": "Ø32", "x": 150, "y": -250, "d": 32 } ] }
            """));
        Assert.True(u.Code == CliExitCode.Ok, u.Err);
        var (after, circ) = Geometry(project);
        var col2 = after.Single(c => c.Tag == "Колонна 400×600");
        Assert.Equal(col.Id, col2.Id);
        Assert.Equal(0.32, Math.Abs(new CScore.GeoProps(col2).A), 9);
        Assert.Equal(2, circ.Count);
        Assert.Equal(0.016, circ.Single(c => c.Tag == "Ø32").Radius, 12);
    }

    [Fact]
    public async Task Contours_Errors_WithPaths_NothingWritten()
    {
        string project = await NewProject();
        var r = await Run("apply", project, Doc("bad.json", """
            { "format": "opencs-model", "version": 1,
              "contours": [
                { "tag": "", "points": [[0, 0], [100, 0], [0, 100]] },
                { "tag": "Бабочка", "points": [[0, 0], [100, 100], [100, 0], [0, 100]] },
                { "tag": "Две", "points": [[0, 0], [100, 0]] },
                { "tag": "Тип", "type": "ring", "points": [[0, 0], [100, 0], [0, 100]] },
                { "tag": "Строка", "points": [[0, 0], ["x", 0], [0, 100]] },
                { "tag": "Повтор", "points": [[0, 0], [100, 0], [0, 100]] },
                { "tag": "Повтор", "points": [[0, 0], [100, 0], [0, 100]] } ],
              "circles": [ { "x": 0, "y": 0, "d": 0 }, { "x": 0, "y": 0 } ] }
            """));
        Assert.Equal(CliExitCode.Usage, r.Code);
        Assert.Contains("contours[0].tag", r.Err);
        Assert.Contains("contours[1].points[", r.Err);
        Assert.Contains("contours[2].points", r.Err);
        Assert.Contains("contours[3].type", r.Err);
        Assert.Contains("contours[4]", r.Err);
        Assert.Contains("contours[6].tag", r.Err);
        Assert.Contains("circles[0].d", r.Err);
        Assert.Contains("circles[1].d", r.Err);
        var (contours, circles) = Geometry(project);
        Assert.Empty(contours);
        Assert.Empty(circles);
    }

    [Fact]
    public async Task Contours_DryRun_WritesNothing_RoundTripSame()
    {
        string first = await NewProject("a.db");
        var dry = await Run("apply", first, Doc("seed.json", ContoursSeed), "--dry-run");
        Assert.True(dry.Code == CliExitCode.Ok, dry.Err);
        Assert.Empty(Geometry(first).Contours);

        Assert.Equal(CliExitCode.Ok, (await Run("apply", first, PathOf("seed.json"))).Code);
        string exported = PathOf("e1.json");
        var ex = await Run("export", "model", first, "--to", exported);
        Assert.True(ex.Code == CliExitCode.Ok, ex.Err);

        string second = await NewProject("b.db");
        var apply = await Run("apply", second, exported);
        Assert.True(apply.Code == CliExitCode.Ok, apply.Err);
        string exported2 = PathOf("e2.json");
        Assert.Equal(CliExitCode.Ok, (await Run("export", "model", second, "--to", exported2)).Code);

        var a = JsonNode.Parse(File.ReadAllText(exported))!;
        var b = JsonNode.Parse(File.ReadAllText(exported2))!;
        Assert.Equal(2, a["contours"]!.AsArray().Count);
        Assert.Equal(4, a["contours"]![1]!["points"]!.AsArray().Count);  // без замыкающей
        Assert.Equal(-200.0, (double)a["contours"]![0]!["points"]![0]![0]!, 9);
        Assert.Equal(2, a["circles"]!.AsArray().Count);
        AssertJsonClose(a, b, "$");
    }

    [Theory]
    [InlineData("contours")]
    [InlineData("circles")]
    [InlineData("sections")]
    [InlineData("plateSections")]
    [InlineData("forceSets")]
    public async Task Schema_Geometry_IsJson(string kind)
    {
        var r = await Run("schema", kind);
        Assert.True(r.Code == CliExitCode.Ok, r.Err);
        Assert.NotNull(JsonNode.Parse(r.Out)!["properties"]);
    }

    /// <summary>JSON совпадает; числа — с относительным допуском 1e-12 (пересчёт кПа ↔ МПа).</summary>
    const string SectionsSeed = """
        { "format": "opencs-model", "version": 1,
          "materials": [
            { "tag": "B25", "catalog": { "concrete": "B25" } },
            { "tag": "A500", "catalog": { "rebar": "A500" } },
            { "tag": "A240", "catalog": { "rebar": "A240" } } ],
          "contours": [ { "tag": "Колонна 400×600", "points": [[-200, -300], [200, -300], [200, 300], [-200, 300]] } ],
          "sections": [
            { "tag": "Ст-1",
              "staged": { "parts": [
                { "tag": "Сборная", "section": "Б-1" },
                { "tag": "Монолит", "areas": [
                  { "tag": "Плита", "category": "region", "material": "B25", "diagram": "sp63",
                    "polygon": { "outer": [[-600, 300], [600, 300], [600, 460], [-600, 460]] } } ] } ] } },
            { "tag": "Б-1", "description": "колонна",
              "areas": [
                { "tag": "Бетон", "category": "region", "material": "B25", "diagram": "sp63", "contour": "Колонна 400×600",
                  "mesh": { "nx": 10, "ny": 15 } },
                { "tag": "Низ", "category": "rebarGroup", "material": "A500", "diagram": "l2", "host": "Бетон",
                  "bars": [[-150, -250, 25], [150, -250, 25]], "prestress": { "sigma": 400, "gamma": 0.9 } },
                { "tag": "Верх", "category": "rebarGroup", "material": "A500", "diagram": "l2", "host": "Бетон",
                  "bars": [ { "x": -150, "y": 250, "d": 16, "area": 200 }, [150, 250, 16] ] },
                { "tag": "Хомуты", "category": "stirrups", "material": "A240",
                  "stirrups": [ { "spacing": 150, "offset": 30, "elements": [
                    { "d": 10, "points": [[-170, -270], [170, -270], [170, 270], [-170, 270]] },
                    { "d": 8, "closed": false, "points": [[0, -270], [0, 270]] } ] } ] } ] } ] }
        """;

    static (List<CScore.CrossSection> Sections, List<CScore.MaterialArea> Areas, List<CScore.CircleP> Circles) Sections(string project)
    {
        using var db = new DatabaseService(project);
        db.LoadAll();
        var r = (db.CrossSections.ToList(), db.MaterialAreas.ToList(), db.Circles.ToList());
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        return r;
    }

    [Fact]
    public async Task Sections_Apply_AreasBarsStirrupsStaged_MetresInBase()
    {
        string project = await NewProject();
        var r = await Run("apply", project, Doc("s.json", SectionsSeed));
        Assert.True(r.Code == CliExitCode.Ok, r.Err);
        var (sections, areas, circles) = Sections(project);
        Assert.Equal(2, sections.Count);
        var b1 = sections.Single(s => s.Tag == "Б-1");
        Assert.Equal("колонна", b1.Description);
        Assert.Equal(["Бетон", "Низ", "Верх", "Хомуты"], b1.Areas.Select(a => a.Tag));
        var concrete = b1.Areas[0];
        Assert.Equal(0.24, Math.Abs(new CScore.GeoProps(concrete.Hull!).A), 9);
        Assert.NotNull(concrete.PoolContourId);
        Assert.Equal((10, 15), (concrete.NX, concrete.NY));
        var low = b1.Areas[1];
        Assert.Equal(concrete.Id, low.HostAreaId);
        Assert.Equal(400, low.SigSp);
        Assert.Equal(0.9, low.GammaSp);
        Assert.Equal(Math.PI * 0.0125 * 0.0125, low.Fibers[0].Area, 12);
        Assert.Equal(200e-6, b1.Areas[2].Fibers[0].Area, 12);          // площадь из документа, не из d
        var st = Assert.Single(b1.Areas[3].Stirrups);
        Assert.Equal(0.15, st.SpacingM, 12);
        Assert.Equal(2, st.Elements.Count);
        Assert.True(st.Elements[0].IsClosed);
        Assert.False(st.Elements[1].IsClosed);
        Assert.Equal(4, circles.Count);                                 // круги групп стержней
        var staged = Assert.IsType<CScore.StagedSection>(sections.Single(s => s.Tag == "Ст-1"));
        Assert.Equal(b1.Id, staged.Parts[0].SourceSectionId);
        Assert.Equal("Плита", Assert.Single(staged.Parts[1].Areas).Tag);
        Assert.Equal(5, areas.Count);
    }

    [Fact]
    public async Task Sections_UpdateInPlace_KeepsIds_RemovedAreaDeleted()
    {
        string project = await NewProject();
        Assert.Equal(CliExitCode.Ok, (await Run("apply", project, Doc("s.json", SectionsSeed))).Code);
        var (before, _, _) = Sections(project);
        var b1 = before.Single(s => s.Tag == "Б-1");
        int concreteId = b1.Areas[0].Id;
        var u = await Run("apply", project, Doc("u.json", """
            { "format": "opencs-model", "version": 1,
              "sections": [ { "tag": "Б-1", "areas": [
                { "tag": "Бетон", "category": "region", "material": "B25", "diagram": "sp63",
                  "polygon": { "outer": [[-200, -400], [200, -400], [200, 400], [-200, 400]] } },
                { "tag": "Низ", "category": "rebarGroup", "material": "A500", "diagram": "l2", "host": "Бетон",
                  "bars": [[-150, -350, 32], [0, -350, 32], [150, -350, 32]] } ] } ] }
            """));
        Assert.True(u.Code == CliExitCode.Ok, u.Err);
        var (after, areas, circles) = Sections(project);
        var b1u = after.Single(s => s.Tag == "Б-1");
        Assert.Equal(b1.Id, b1u.Id);
        Assert.Equal(concreteId, b1u.Areas[0].Id);
        Assert.Null(b1u.Areas[0].PoolContourId);
        Assert.Equal(0.32, Math.Abs(new CScore.GeoProps(b1u.Areas[0].Hull!).A), 9);
        Assert.Equal(3, b1u.Areas[1].Fibers.Count);
        Assert.Equal(0, b1u.Areas[1].SigSp);
        // «Верх» и «Хомуты» убраны из сечения и больше нигде не используются — удалены вместе с кругами.
        Assert.DoesNotContain(areas, a => a.Tag is "Верх" or "Хомуты");
        Assert.Equal(3, circles.Count);
        Assert.Equal(b1u.Id, Assert.IsType<CScore.StagedSection>(after.Single(s => s.Tag == "Ст-1")).Parts[0].SourceSectionId);
    }

    [Fact]
    public async Task Sections_Errors_WithPaths_NothingWritten()
    {
        string project = await NewProject();
        var r = await Run("apply", project, Doc("bad.json", """
            { "format": "opencs-model", "version": 1,
              "materials": [ { "tag": "B25", "catalog": { "concrete": "B25" } }, { "tag": "A500", "catalog": { "rebar": "A500" } } ],
              "sections": [
                { "tag": "С1", "areas": [ { "tag": "Б", "category": "region", "material": "Нет", "polygon": { "outer": [[0, 0], [100, 0], [0, 100]] } } ] },
                { "tag": "С2", "areas": [
                  { "tag": "Б", "category": "region", "material": "B25", "polygon": { "outer": [[0, 0], [400, 0], [400, 400], [0, 400]] } },
                  { "tag": "Арм", "category": "rebarGroup", "material": "B25", "host": "Б", "bars": [[50, 50, 20]] },
                  { "tag": "Арм2", "category": "rebarGroup", "material": "A500", "host": "Нет", "bars": [[50, 50, 0]] },
                  { "tag": "Вне", "category": "rebarGroup", "material": "A500", "host": "Б", "bars": [[900, 50, 20]] } ] },
                { "tag": "С3", "areas": [ { "tag": "Б", "category": "region", "material": "B25", "contour": "Нет такого" } ] },
                { "tag": "С4", "staged": { "parts": [ { "tag": "Ч", "section": "Нет" } ] } },
                { "tag": "С5" },
                { "tag": "С6", "areas": [ { "tag": "Арм", "category": "rebarGroup", "material": "A500", "bars": [["x", 1, 2]] } ] } ] }
            """));
        Assert.Equal(CliExitCode.Usage, r.Code);
        Assert.Contains("sections[0].areas[0].material", r.Err);
        Assert.Contains("sections[1].areas[2].host", r.Err);
        Assert.Contains("sections[5].areas[0].bars[0]", r.Err);
        Assert.Contains("sections[2].areas[0].contour", r.Err);
        Assert.Contains("sections[3].staged.parts[0].section", r.Err);
        Assert.Contains("sections[4]", r.Err);
        Assert.Empty(Sections(project).Sections);
        Assert.Empty(Pool(project).Materials);
    }

    [Fact]
    public async Task Sections_ServiceErrors_MaterialKindBarOutsideZeroDiameter()
    {
        string project = await NewProject();
        var r = await Run("apply", project, Doc("bad.json", """
            { "format": "opencs-model", "version": 1,
              "materials": [ { "tag": "B25", "catalog": { "concrete": "B25" } }, { "tag": "A500", "catalog": { "rebar": "A500" } } ],
              "sections": [ { "tag": "С", "areas": [
                  { "tag": "Б", "category": "region", "material": "B25", "polygon": { "outer": [[0, 0], [400, 0], [400, 400], [0, 400]] } },
                  { "tag": "Арм", "category": "rebarGroup", "material": "A500", "host": "Б", "bars": [[50, 50, 0], [900, 50, 20]] },
                  { "tag": "Бет", "category": "rebarGroup", "material": "B25", "host": "Б", "bars": [[50, 50, 20]] } ] } ] }
            """));
        Assert.Equal(CliExitCode.Usage, r.Code);
        Assert.Contains("sections[0].areas[1].bars[0].d", r.Err);
        Assert.Contains("sections[0].areas[1].bars[1]", r.Err);
        Assert.Contains("sections[0].areas[2].material", r.Err);
        Assert.Empty(Sections(project).Sections);
    }

    [Fact]
    public async Task Sections_DryRun_WritesNothing_RoundTripSame()
    {
        string first = await NewProject("a.db");
        var dry = await Run("apply", first, Doc("seed.json", SectionsSeed), "--dry-run");
        Assert.True(dry.Code == CliExitCode.Ok, dry.Err);
        Assert.Empty(Sections(first).Sections);

        Assert.Equal(CliExitCode.Ok, (await Run("apply", first, PathOf("seed.json"))).Code);
        string exported = PathOf("e1.json");
        var ex = await Run("export", "model", first, "--to", exported);
        Assert.True(ex.Code == CliExitCode.Ok, ex.Err);

        string second = await NewProject("b.db");
        var apply = await Run("apply", second, exported);
        Assert.True(apply.Code == CliExitCode.Ok, apply.Err);
        string exported2 = PathOf("e2.json");
        Assert.Equal(CliExitCode.Ok, (await Run("export", "model", second, "--to", exported2)).Code);

        var a = JsonNode.Parse(File.ReadAllText(exported))!;
        var b = JsonNode.Parse(File.ReadAllText(exported2))!;
        Assert.Equal(2, a["sections"]!.AsArray().Count);
        Assert.Equal("Б-1", (string?)a["sections"]![0]!["tag"]);                // произвольные — раньше стадийных
        Assert.Equal("Колонна 400×600", (string?)a["sections"]![0]!["areas"]![0]!["contour"]);
        Assert.Empty(a["circles"]!.AsArray());                                  // круги групп стержней не выгружаются
        AssertJsonClose(a, b, "$");

        // Повторное применение выгрузки к тому же проекту — только обновления, области не размножаются.
        var again = await Run("apply", first, exported, "--json");
        Assert.True(again.Code == CliExitCode.Ok, again.Err);
        Assert.DoesNotContain("create", again.Out);
        Assert.Equal(5, Sections(first).Areas.Count);
    }

    const string ParametricSeed = """
        { "format": "opencs-model", "version": 1,
          "materials": [
            { "tag": "B25", "catalog": { "concrete": "B25" } },
            { "tag": "A500", "catalog": { "rebar": "A500" } },
            { "tag": "A240", "catalog": { "rebar": "A240" } },
            { "tag": "С255", "catalog": { "steel": "С255" } } ],
          "sections": [
            { "tag": "Ригель", "description": "прямоугольник",
              "parametric": { "kind": "rc", "shape": "rectangle", "b": 300, "h": 500, "concrete": "B25", "rebar": "A500",
                "bottom": { "count": 3, "d": 20, "coord": -210 }, "top": { "count": 2, "d": 16, "coord": 210 },
                "stirrups": [ { "zone": "body", "direction": "vertical", "count": 2, "d": 8, "spacing": 150, "cover": 30, "material": "A240" } ],
                "extraBars": [ [-100, 0, 12] ] } },
            { "tag": "Тавр",
              "parametric": { "kind": "rc", "shape": "tee", "b": 800, "h": 600, "bw": 300, "hf": 150, "concrete": "B25", "rebar": "A500",
                "bottom": { "count": 2, "d": 25, "coord": -280 } } },
            { "tag": "Балка",
              "parametric": { "kind": "steel", "profile": "iBeam", "fabrication": "welded", "h": 400, "b": 200, "tf": 12, "tw": 8, "steel": "С255" } },
            { "tag": "Стойка",
              "parametric": { "kind": "steel", "profile": "pipe", "fabrication": "rolled", "h": 159, "tw": 6, "steel": "С255",
                "catalog": { "group": "Трубы", "standard": "ГОСТ 10704", "name": "159x6", "it": 12000000 } } } ] }
        """;

    [Fact]
    public async Task ParametricSections_Apply_Generated_RoundTripSame_UpdateKeepsIds()
    {
        string first = await NewProject("a.db");
        var dry = await Run("apply", first, Doc("seed.json", ParametricSeed), "--dry-run");
        Assert.True(dry.Code == CliExitCode.Ok, dry.Err);
        Assert.Empty(Sections(first).Sections);

        var r = await Run("apply", first, PathOf("seed.json"));
        Assert.True(r.Code == CliExitCode.Ok, r.Err);
        Assert.Equal(4, Sections(first).Sections.Count);
        using (var db = new DatabaseService(first))
        {
            db.LoadAll();
            var rc = new ParametricRcSectionProjectService(db);
            var steel = new ParametricSteelSectionProjectService(db);
            var beam = db.CrossSections.Single(s => s.Tag == "Ригель");
            Assert.Equal("прямоугольник", beam.Description);
            Assert.Equal(ParametricRcDefinitionLoadStatus.Supported, rc.GetState(beam).LoadStatus);
            Assert.True(rc.TryGetDefinition(beam, out var d));
            Assert.Equal(0.3, d.WidthM, 12);
            Assert.Equal(-0.21, d.LowerRebar!.CoordinateM, 12);
            Assert.Equal(db.Materials.Single(m => m.Tag == "A240").Id, Assert.Single(d.StirrupCuts).MaterialId);
            Assert.Contains(beam.Areas, a => a.Category == CScore.AreaCategory.Stirrups);
            var pipe = db.CrossSections.Single(s => s.Tag == "Стойка");
            Assert.True(steel.TryGetDefinition(pipe, out var p));
            Assert.Equal(12e-6, p.Catalog!.It, 15);
            Assert.Equal(ParametricSteelDefinitionLoadStatus.Supported,
                steel.GetState(db.CrossSections.Single(s => s.Tag == "Балка")).LoadStatus);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        string exported = PathOf("e1.json");
        var ex = await Run("export", "model", first, "--to", exported);
        Assert.True(ex.Code == CliExitCode.Ok, ex.Err);
        var a = JsonNode.Parse(File.ReadAllText(exported))!;
        Assert.Equal(4, a["sections"]!.AsArray().Count);
        Assert.All(a["sections"]!.AsArray(), s => Assert.Null(s!["areas"]));   // области строит служба
        Assert.Equal("A240", (string?)a["sections"]![0]!["parametric"]!["stirrups"]![0]!["material"]);

        string second = await NewProject("b.db");
        var apply = await Run("apply", second, exported);
        Assert.True(apply.Code == CliExitCode.Ok, apply.Err);
        string exported2 = PathOf("e2.json");
        Assert.Equal(CliExitCode.Ok, (await Run("export", "model", second, "--to", exported2)).Code);
        AssertJsonClose(a, JsonNode.Parse(File.ReadAllText(exported2))!, "$");

        // Повторное применение к тому же проекту — обновление на месте: Id сечений те же.
        var ids = Sections(first).Sections.ToDictionary(s => s.Tag, s => s.Id);
        var again = await Run("apply", first, exported, "--json");
        Assert.True(again.Code == CliExitCode.Ok, again.Err);
        Assert.DoesNotContain("create", again.Out);
        Assert.Equal(ids, Sections(first).Sections.ToDictionary(s => s.Tag, s => s.Id));
    }

    [Fact]
    public async Task ParametricSections_Errors_WithPaths_NothingWritten()
    {
        string project = await NewProject();
        Assert.Equal(CliExitCode.Ok, (await Run("apply", project, Doc("s.json", SectionsSeed))).Code);
        int before = Sections(project).Sections.Count;
        var r = await Run("apply", project, Doc("bad.json", """
            { "format": "opencs-model", "version": 1,
              "sections": [
                { "tag": "П1", "parametric": { "kind": "rc", "shape": "rectangle", "b": 300, "h": 500, "concrete": "Нет", "rebar": "A500" } },
                { "tag": "П2", "parametric": { "kind": "rc", "shape": "rectangle", "b": 300, "h": 500, "concrete": "A500", "rebar": "B25",
                  "stirrups": [ { "count": 2, "d": 8, "spacing": 150, "cover": 30, "material": "B25" } ] } },
                { "tag": "П3", "parametric": { "kind": "rc", "shape": "tee", "b": 300, "h": 500, "bw": 400, "hf": 100, "concrete": "B25", "rebar": "A500" } },
                { "tag": "П4", "parametric": { "kind": "steel", "profile": "pipe", "h": 159, "tw": 6, "steel": "B25" } },
                { "tag": "П5", "parametric": { "kind": "wood" } },
                { "tag": "П6", "areas": [], "parametric": { "kind": "steel" } },
                { "tag": "Б-1", "parametric": { "kind": "rc", "shape": "rectangle", "b": 300, "h": 500, "concrete": "B25", "rebar": "A500" } } ] }
            """));
        Assert.Equal(CliExitCode.Usage, r.Code);
        Assert.Contains("sections[0].parametric.concrete", r.Err);
        Assert.Contains("sections[1].parametric.concrete", r.Err);
        Assert.Contains("sections[1].parametric.rebar", r.Err);
        Assert.Contains("sections[1].parametric.stirrups[0].material", r.Err);
        Assert.Contains("sections[2].parametric:", r.Err);                          // геометрия — от генератора
        Assert.Contains("sections[3].parametric.steel", r.Err);
        Assert.Contains("sections[4].parametric.kind", r.Err);
        Assert.Contains("sections[5]", r.Err);
        Assert.Contains("sections[6]", r.Err);                                      // произвольное «Б-1» не становится параметрическим
        Assert.Equal(before, Sections(project).Sections.Count);
    }

    const string PlateSeed = """
        { "format": "opencs-model", "version": 1,
          "materials": [
            { "tag": "B25", "catalog": { "concrete": "B25" } },
            { "tag": "A500", "catalog": { "rebar": "A500" } },
            { "tag": "A240", "catalog": { "rebar": "A240" } } ],
          "plateSections": [
            { "tag": "Плита 200", "h": 200, "concrete": "B25", "rebar": "A500", "diagram": "sp63", "nLayers": 12,
              "tension": true, "softening": "vecchioCollins", "epsC2": 0.0021,
              "rebarLayers": [
                { "name": "Низ", "mode": "spacing", "x": { "d": 12, "step": 200, "z": -70 }, "y": { "d": 12, "step": 200, "z": -58 } },
                { "name": "Верх", "mode": "count", "x": { "d": 10, "count": 5, "z": 75 }, "material": "A240" },
                { "name": "Доп", "mode": "area", "y": { "area": 393, "z": 60 }, "face": "minus", "angle": 30 } ] },
            { "tag": "Стена 160", "h": 160, "concrete": "B25", "rebar": "A500", "model": "char1dAxial" } ] }
        """;

    static List<CScore.PlateSection> Plates(string project)
    {
        using var db = new DatabaseService(project);
        db.LoadAll();
        var r = db.PlateSections.ToList();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        return r;
    }

    [Fact]
    public async Task PlateSections_Apply_MetresInBase_DryRun_RoundTripSame_UpdateKeepsId()
    {
        string first = await NewProject("a.db");
        var dry = await Run("apply", first, Doc("seed.json", PlateSeed), "--dry-run");
        Assert.True(dry.Code == CliExitCode.Ok, dry.Err);
        Assert.Empty(Plates(first));

        var r = await Run("apply", first, PathOf("seed.json"));
        Assert.True(r.Code == CliExitCode.Ok, r.Err);
        var plates = Plates(first);
        Assert.Equal(2, plates.Count);
        var p = plates.Single(x => x.Tag == "Плита 200");
        Assert.Equal(0.2, p.H, 12);
        Assert.Equal(12, p.NLayers);
        Assert.Equal(CScore.DiagrammType.SP63, p.ConcreteDiagramType);
        Assert.True(p.TensionConcrete);
        Assert.Equal("vecchio_collins", p.SofteningModel);
        Assert.Equal(3, p.RebarLayers.Count);
        Assert.Equal(-0.07, p.RebarLayers[0].Zsx, 12);
        Assert.Equal(Math.PI * 0.012 * 0.012 / 4 / 0.2, p.RebarLayers[0].Asx, 12);
        Assert.Equal(Math.PI * 0.01 * 0.01 / 4 * 5, p.RebarLayers[1].Asx, 12);
        Assert.Equal(0, p.RebarLayers[1].Asy);
        Assert.NotEqual(0, p.RebarLayers[1].MaterialId);
        Assert.Equal(393e-6, p.RebarLayers[2].Asy, 12);
        Assert.Equal(CScore.PlateRebar.RebarFace.MinusN, p.RebarLayers[2].Face);
        Assert.Equal("char1d_axial", plates.Single(x => x.Tag == "Стена 160").PlateModel);

        string exported = PathOf("e1.json");
        var ex = await Run("export", "model", first, "--to", exported);
        Assert.True(ex.Code == CliExitCode.Ok, ex.Err);
        string second = await NewProject("b.db");
        var apply = await Run("apply", second, exported);
        Assert.True(apply.Code == CliExitCode.Ok, apply.Err);
        string exported2 = PathOf("e2.json");
        Assert.Equal(CliExitCode.Ok, (await Run("export", "model", second, "--to", exported2)).Code);
        var a = JsonNode.Parse(File.ReadAllText(exported))!;
        var b = JsonNode.Parse(File.ReadAllText(exported2))!;
        Assert.Equal(2, a["plateSections"]!.AsArray().Count);
        Assert.Null(a["plateSections"]![0]!["rebarLayers"]![1]!["y"]);       // направление без арматуры не пишется
        AssertJsonClose(a, b, "$");

        var u = await Run("apply", first, Doc("u.json", """
            { "format": "opencs-model", "version": 1,
              "plateSections": [ { "tag": "Плита 200", "h": 250, "concrete": "B25", "rebar": "A500" } ] }
            """), "--json");
        Assert.True(u.Code == CliExitCode.Ok, u.Err);
        Assert.DoesNotContain("create", u.Out);
        var p2 = Plates(first).Single(x => x.Tag == "Плита 200");
        Assert.Equal(p.Id, p2.Id);
        Assert.Equal(0.25, p2.H, 12);
        Assert.Empty(p2.RebarLayers);
    }

    [Fact]
    public async Task PlateSections_Errors_WithPaths_NothingWritten()
    {
        string project = await NewProject();
        var r = await Run("apply", project, Doc("bad.json", """
            { "format": "opencs-model", "version": 1,
              "materials": [ { "tag": "B25", "catalog": { "concrete": "B25" } }, { "tag": "A500", "catalog": { "rebar": "A500" } } ],
              "plateSections": [
                { "tag": "П1", "h": 0, "concrete": "A500", "rebar": "B25", "nLayers": 0 },
                { "tag": "П2", "h": 200, "concrete": "B25", "rebar": "Нет",
                  "rebarLayers": [ { "mode": "spacing", "x": { "d": 12, "step": 200, "z": -70 } }, { "mode": "wrong" } ] },
                { "tag": "П3", "h": 200, "concrete": "B25", "rebar": "A500",
                  "rebarLayers": [ { "mode": "count", "y": { "d": 10, "z": 50 } } ] },
                { "tag": "П4", "h": 200, "concrete": "B25", "rebar": "A500",
                  "rebarLayers": [ { "x": { "d": 12, "step": 200, "z": 0 }, "material": "B25" } ] },
                { "tag": "П4", "h": 200, "concrete": "B25", "rebar": "A500" },
                { "h": 200 } ] }
            """));
        Assert.Equal(CliExitCode.Usage, r.Code);
        Assert.Contains("plateSections[0].h", r.Err);
        Assert.Contains("plateSections[0].concrete", r.Err);
        Assert.Contains("plateSections[0].rebar", r.Err);
        Assert.Contains("plateSections[0].nLayers", r.Err);
        Assert.Contains("plateSections[1].rebarLayers[1].mode", r.Err);
        Assert.Contains("plateSections[2].rebarLayers[0].y.count", r.Err);
        Assert.Contains("plateSections[3].rebarLayers[0].material", r.Err);
        Assert.Contains("plateSections[4].tag", r.Err);
        Assert.Contains("plateSections[5].tag", r.Err);
        Assert.Empty(Plates(project));

        // Ссылки разрешены — видны ошибки службы: шаг 0, z вне толщины, неизвестный материал.
        var r2 = await Run("apply", project, Doc("bad2.json", """
            { "format": "opencs-model", "version": 1,
              "materials": [ { "tag": "B25", "catalog": { "concrete": "B25" } }, { "tag": "A500", "catalog": { "rebar": "A500" } } ],
              "plateSections": [
                { "tag": "П2", "h": 200, "concrete": "B25", "rebar": "A500",
                  "rebarLayers": [ { "x": { "d": 12, "step": 0, "z": -70 } }, { "x": { "d": 12, "step": 200, "z": -120 } } ] },
                { "tag": "П5", "h": 200, "concrete": "B25", "rebar": "Нет" } ] }
            """));
        Assert.Equal(CliExitCode.Usage, r2.Code);
        Assert.Contains("plateSections[0].rebarLayers[0].x.step", r2.Err);
        Assert.Contains("plateSections[0].rebarLayers[1].x.z", r2.Err);
        Assert.Contains("plateSections[1].rebar", r2.Err);
        Assert.Empty(Plates(project));
    }

    [Fact]
    public async Task PlateSections_GeneratedByRegion_NotExported_ApplyRefused()
    {
        string project = await NewProject();
        Assert.Equal(CliExitCode.Ok, (await Run("apply", project, Doc("seed.json", PlateSeed))).Code);
        using (var db = new DatabaseService(project))
        {
            db.LoadAll();
            var wall = db.PlateSections.Single(p => p.Tag == "Стена 160");
            wall.GeneratedForRegionId = 1;
            db.SavePlateSection(wall);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        string exported = PathOf("e.json");
        var ex = await Run("export", "model", project, "--to", exported);
        Assert.True(ex.Code == CliExitCode.Ok, ex.Err);
        Assert.Contains("Стена 160", ex.Err);                                // предупреждение о пропуске
        var a = JsonNode.Parse(File.ReadAllText(exported))!;
        Assert.Equal(["Плита 200"], a["plateSections"]!.AsArray().Select(n => (string?)n!["tag"]));

        var r = await Run("apply", project, PathOf("seed.json"));
        Assert.Equal(CliExitCode.Usage, r.Code);
        Assert.Contains("plateSections[1].tag", r.Err);
    }

    const string ForceSeed = """
        { "format": "opencs-model", "version": 1,
          "forceSets": [
            { "tag": "G: Вес", "kind": "bar", "description": "колонна К1",
              "columns": ["label", "N", "Mx", "My", "Vx", "Vy", "T"],
              "rows": [ ["1", -1200, 35.5, 0, 0, 12, 0], ["2", -1180, -20, 1.5, 0, 10, 0.25] ] },
            { "tag": "Q(γf=1,4): Ветер [ветер]", "kind": "shell", "secondOrder": true, "imperfections": "Крен X",
              "columns": ["label", "Mx", "My", "Mxy", "sigmaX", "tauXY", "elem", "sect"],
              "rows": [ ["э.15 с1", 12.3, 4.1, 0.5, 100, null, 15, 1], ["э.16 с1", 1, 2, 3, null, -20, 16, null] ] },
            { "tag": "РСУ из таблицы", "kind": "bar", "source": "table",
              "columns": ["label", "N", "Mx", "elem", "sect"],
              "rows": [ ["э.1", -10, 2, 1, 1], ["э.2", -11, 3, 2, 1] ] } ] }
        """;

    static List<CScore.ForceSet> ForceSets(string project)
    {
        using var db = new DatabaseService(project);
        db.LoadAll();
        var r = db.ForceSets.ToList();
        foreach (var fs in r) fs.EnsureRows();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        return r;
    }

    [Fact]
    public async Task ForceSets_Apply_DryRun_RoundTripSame_UpdateKeepsIds()
    {
        string first = await NewProject("a.db");
        var dry = await Run("apply", first, Doc("seed.json", ForceSeed), "--dry-run");
        Assert.True(dry.Code == CliExitCode.Ok, dry.Err);
        Assert.Empty(ForceSets(first));

        var r = await Run("apply", first, PathOf("seed.json"));
        Assert.True(r.Code == CliExitCode.Ok, r.Err);
        var sets = ForceSets(first);
        Assert.Equal(3, sets.Count);
        var g = sets.Single(s => s.Tag == "G: Вес");
        Assert.Equal("колонна К1", g.Description);
        Assert.Equal(0.25, g.Items[1].T);
        var w = sets.Single(s => s.Kind == "shell");
        Assert.True(w.SecondOrder);
        Assert.Equal("Крен X", w.Imperfections);
        Assert.Equal(100, w.ShellItems[0].SigmaX);
        Assert.Null(w.ShellItems[0].TauXY);
        Assert.Null(w.ShellItems[0].SigmaY);
        Assert.Equal(0, w.ShellItems[0].Nx);
        Assert.Equal(16, w.ShellItems[1].SourceElementNum);
        Assert.Null(w.ShellItems[1].SourceSectionNum);
        Assert.Equal("table", sets.Single(s => s.Tag == "РСУ из таблицы").SourceType);

        string exported = PathOf("e1.json");
        var ex = await Run("export", "model", first, "--to", exported);
        Assert.True(ex.Code == CliExitCode.Ok, ex.Err);
        string second = await NewProject("b.db");
        Assert.True((await Run("apply", second, exported)).Code == CliExitCode.Ok);
        string exported2 = PathOf("e2.json");
        Assert.Equal(CliExitCode.Ok, (await Run("export", "model", second, "--to", exported2)).Code);
        var a = JsonNode.Parse(File.ReadAllText(exported))!;
        var b = JsonNode.Parse(File.ReadAllText(exported2))!;
        Assert.Equal(3, a["forceSets"]!.AsArray().Count);
        Assert.Equal(["label", "N", "Mx", "My", "Vx", "Vy", "T"],
                     a["forceSets"]![0]!["columns"]!.AsArray().Select(n => (string?)n));
        AssertJsonClose(a, b, "$");

        // Задача ссылается на вторую строку — обновление сохраняет Id набора и строк по позиции.
        using (var db = new DatabaseService(first))
        {
            db.LoadAll();
            var set = db.ForceSets.Single(s => s.Tag == "G: Вес");
            set.EnsureRows();
            db.SaveCalcTask(new CScore.CalcTask { Num = 1, Tag = "НДС", Kind = "strain_state", ForceSetId = set.Id, ForceItemId = set.Items[1].Id });
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var u = await Run("apply", first, Doc("u.json", """
            { "format": "opencs-model", "version": 1,
              "forceSets": [ { "tag": "G: Вес", "kind": "bar", "columns": ["label", "N"],
                               "rows": [ ["1", -1], ["2", -2], ["3", -3] ] } ] }
            """), "--json");
        Assert.True(u.Code == CliExitCode.Ok, u.Err);
        var g2 = ForceSets(first).Single(s => s.Tag == "G: Вес");
        Assert.Equal(g.Id, g2.Id);
        Assert.Equal(g.Items[1].Id, g2.Items[1].Id);
        Assert.Equal(3, g2.Items.Count);
        Assert.Equal(0, g2.Items[0].Mx);

        // Убрать строку, на которую ссылается задача, нельзя.
        var cut = await Run("apply", first, Doc("cut.json", """
            { "format": "opencs-model", "version": 1,
              "forceSets": [ { "tag": "G: Вес", "kind": "bar", "columns": ["label", "N"], "rows": [ ["1", -1] ] } ] }
            """));
        Assert.Equal(CliExitCode.Usage, cut.Code);
        Assert.Contains("forceSets[0].rows", cut.Err);
        Assert.Equal(3, ForceSets(first).Single(s => s.Tag == "G: Вес").Items.Count);
    }

    [Fact]
    public async Task ForceSets_Errors_WithPaths_NothingWritten()
    {
        string project = await NewProject();
        Assert.Equal(CliExitCode.Ok, (await Run("apply", project, Doc("seed.json", ForceSeed))).Code);
        var r = await Run("apply", project, Doc("bad.json", """
            { "format": "opencs-model", "version": 1,
              "forceSets": [
                { "tag": "A", "kind": "bar", "columns": ["label", "Nx"], "rows": [ ["1", 1] ] },
                { "tag": "B", "kind": "bar", "columns": ["label", "N"], "rows": [ ["1", "x"], ["2"] ] },
                { "tag": "C", "kind": "bar", "columns": ["label", "N"], "rows": [] },
                { "tag": "G: Вес", "kind": "shell", "columns": ["label", "Mx"], "rows": [ ["1", 1] ] },
                { "tag": "D", "kind": "beam", "columns": ["label"], "rows": [ ["1"] ] },
                { "tag": "E", "kind": "bar", "columns": ["N"], "rows": [ [1] ] },
                { "tag": "F", "kind": "bar", "columns": ["label", "N"], "rows": [ ["1", null] ] },
                { "tag": "Q(γf=0): Снег", "kind": "bar", "columns": ["label", "N"], "rows": [ ["1", 1] ] },
                { "tag": "H", "kind": "bar", "columns": ["label", "N"], "rows": [ ["1", 1] ] },
                { "tag": "H", "kind": "bar", "columns": ["label", "N"], "rows": [ ["1", 1] ] } ] }
            """));
        Assert.Equal(CliExitCode.Usage, r.Code);
        Assert.Contains("forceSets[0].columns[1]", r.Err);
        Assert.Contains("forceSets[1].rows[0][1]", r.Err);
        Assert.Contains("forceSets[1].rows[1]", r.Err);
        Assert.Contains("forceSets[2].rows", r.Err);
        Assert.Contains("forceSets[3].kind", r.Err);
        Assert.Contains("forceSets[4].kind", r.Err);
        Assert.Contains("forceSets[5].columns", r.Err);
        Assert.Contains("forceSets[6].rows[0][1]", r.Err);
        Assert.Contains("forceSets[7].gammaF", r.Err);
        Assert.Contains("forceSets[9].tag", r.Err);
        Assert.DoesNotContain("forceSets[8]", r.Err);
        Assert.Equal(3, ForceSets(project).Count);
    }

    [Fact]
    public async Task ForceSets_LinkedToSchema_NotExported_ApplyRefused()
    {
        string project = await NewProject();
        Assert.Equal(CliExitCode.Ok, (await Run("apply", project, Doc("seed.json", ForceSeed))).Code);
        using (var db = new DatabaseService(project))
        {
            db.LoadAll();
            var set = db.ForceSets.Single(s => s.Tag == "РСУ из таблицы");
            set.SourceSchemaId = 1;
            db.SaveForceSet(set);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        string exported = PathOf("e.json");
        var ex = await Run("export", "model", project, "--to", exported);
        Assert.True(ex.Code == CliExitCode.Ok, ex.Err);
        Assert.Contains(": 1", ex.Err);                                     // предупреждение с числом
        var a = JsonNode.Parse(File.ReadAllText(exported))!;
        Assert.DoesNotContain("РСУ из таблицы", a["forceSets"]!.AsArray().Select(n => (string?)n!["tag"]));

        var r = await Run("apply", project, PathOf("seed.json"));
        Assert.Equal(CliExitCode.Usage, r.Code);
        Assert.Contains("forceSets[2].tag", r.Err);
    }

    [Fact]
    public async Task CombineSp20_CreatesSets_ErrorsUsage()
    {
        string project = await NewProject();
        Assert.Equal(CliExitCode.Ok, (await Run("apply", project, Doc("seed.json", """
            { "format": "opencs-model", "version": 1,
              "forceSets": [
                { "tag": "G: Вес", "kind": "bar", "columns": ["label", "N", "Mx"], "rows": [ ["1", -100, 10], ["2", -90, -5] ] },
                { "tag": "Q: Снег", "kind": "bar", "columns": ["label", "N", "Mx"], "rows": [ ["1", -10, 2], ["2", -8, -3] ] },
                { "tag": "Q: Короткий", "kind": "bar", "columns": ["label", "N"], "rows": [ ["1", -1] ] } ] }
            """))).Code);
        var r = await Run("combine", "sp20", project, "--sets", "G: Вес; Q: Снег", "--mode", "fundamental", "--group", "N",
                          "--envelope", "--name", "К", "--json");
        Assert.True(r.Code == CliExitCode.Ok, r.Err);
        Assert.Contains("К (основное (N))", r.Out);
        var sets = ForceSets(project);
        Assert.Equal(4, sets.Count);
        Assert.NotEmpty(sets.Single(s => s.Tag == "К (основное (N))").Items);

        var bad = await Run("combine", "sp20", project, "--sets", "G: Вес;Q: Короткий");
        Assert.Equal(CliExitCode.Usage, bad.Code);   // разное число строк
        var none = await Run("combine", "sp20", project, "--sets", "Нет такого");
        Assert.Equal(CliExitCode.Usage, none.Code);
        Assert.Equal(4, ForceSets(project).Count);
    }

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
