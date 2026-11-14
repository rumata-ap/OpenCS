using System.IO;
using System.Text.Json;
using CScore;
using OpenCS.Cli;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Консоль opencs: сборка проекта без UI — новый проект, списки, сечения, наборы усилий, задачи.</summary>
[Collection("Loc language")]
public sealed class CliAuthoringTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_cli_author_{Guid.NewGuid():N}");

    public CliAuthoringTests() => Directory.CreateDirectory(_dir);

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

    static DatabaseService Load(string path)
    {
        var db = new DatabaseService(path);
        db.LoadAll();
        return db;
    }

    [Fact]
    public async Task New_CreatesEmptyProject_ExistingNeedsForce()
    {
        string project = PathOf("папка/новый.db");

        var (code, output, err) = await Run("new", project);
        Assert.True(code == CliExitCode.Ok, err);
        Assert.Contains(Path.GetFullPath(project), output);
        Assert.Equal(DatabaseService.SchemaVersion, ProjectFile.ReadSchemaVersion(project));

        Assert.Equal(CliExitCode.Usage, (await Run("new", project)).Code);
        Assert.Equal(CliExitCode.Ok, (await Run("new", project, "--force")).Code);
    }

    [Fact]
    public async Task ListSectionsMaterialsForceSets()
    {
        string project = PathOf("проект.db");
        TestProjectDb.Create(project);
        using (var db = Load(project))
        {
            var set = new ForceSet { Num = 1, Tag = "РСУ", Kind = "bar" };
            foreach (int e in new[] { 1, 2, 3, 7 })
                set.Items.Add(new LoadItem { Label = $"Эл. {e}", N = -e, SourceElementNum = e });
            db.SaveForceSet(set);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var sections = await Run("list", "sections", project);
        Assert.Equal(CliExitCode.Ok, sections.Code);
        Assert.Contains("Балка", sections.Out);
        Assert.Contains("B25", sections.Out);

        var materials = await Run("list", "materials", project, "--json");
        using (var json = JsonDocument.Parse(materials.Out))
            Assert.Equal(2, json.RootElement.GetArrayLength());

        var sets = await Run("list", "forcesets", project);
        Assert.Equal(CliExitCode.Ok, sets.Code);
        Assert.Contains("РСУ", sets.Out);
        Assert.Contains("1-3, 7", sets.Out);
    }

    [Fact]
    public async Task ListProfiles_FindsAllStandardsOfName()
    {
        var (code, output, err) = await Run("list", "profiles", "30k1", "--json");

        Assert.True(code == CliExitCode.Ok, err);
        using var json = JsonDocument.Parse(output);
        var rows = json.RootElement.EnumerateArray().ToList();
        Assert.Contains(rows, r => r.GetProperty("standard").GetString()!.Contains("57837")
                                   && r.GetProperty("profile").GetString() == "30К1");
        Assert.All(rows, r => Assert.Equal("ibeam", r.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task AddSteelSection_SteelByThickness_SameSteelReused()
    {
        string project = PathOf("рама.db");
        await Run("new", project);

        var col = await Run("add", "steel-section", project, "--profile", "30К1", "--standard", "57837",
            "--steel", "С255", "--tag", "Колонна 30К1", "--json");
        Assert.True(col.Code == CliExitCode.Ok, col.Err);
        using (var json = JsonDocument.Parse(col.Out))
        {
            Assert.Equal("Колонна 30К1", json.RootElement.GetProperty("tag").GetString());
            var material = json.RootElement.GetProperty("material");
            Assert.Equal("С255 (10-20 мм), фасонный прокат", material.GetProperty("tag").GetString());
            Assert.True(material.GetProperty("created").GetBoolean());
        }
        // 35Ш1 есть в двух подтипах ГОСТ Р 57837 (исходный 19 и «изменение № 1» 79) — по номеру стандарта неоднозначно.
        var ambiguous = await Run("add", "steel-section", project, "--profile", "35Ш1", "--standard", "57837", "--steel", "С255");
        Assert.Equal(CliExitCode.Usage, ambiguous.Code);
        Assert.Contains("79", ambiguous.Err);
        var beam = await Run("add", "steel-section", project, "--profile", "35Ш1", "--standard", "19", "--steel", "C255");
        Assert.True(beam.Code == CliExitCode.Ok, beam.Err);
        Assert.Contains(Loc.S("CliMaterialExisting"), beam.Out);

        using var db = Load(project);
        Assert.Equal(2, db.CrossSections.Count);
        Assert.Single(db.Materials);
        Assert.Equal(["Колонна 30К1", "35Ш1"], db.CrossSections.OrderBy(s => s.Num).Select(s => s.Tag));
        new OpenCS.Services.ParametricSteelSectionProjectService(db).ApplyBindings(db.CrossSections);
        Assert.All(db.CrossSections, s => Assert.NotNull(s.ParametricSteel));
    }

    [Fact]
    public async Task AddSteelSection_AmbiguousOrMissing_Usage_OutLeavesSource()
    {
        string project = PathOf("п.db");
        await Run("new", project);
        byte[] before = File.ReadAllBytes(project);

        var ambiguous = await Run("add", "steel-section", project, "--profile", "30Б1", "--steel", "С255");
        Assert.Equal(CliExitCode.Usage, ambiguous.Code);
        Assert.Contains("--standard", ambiguous.Err);
        Assert.Equal(CliExitCode.Usage, (await Run("add", "steel-section", project, "--profile", "99Ъ9", "--steel", "С255")).Code);
        Assert.Equal(CliExitCode.Usage, (await Run("add", "steel-section", project, "--profile", "30К1",
            "--standard", "57837", "--steel", "С999")).Code);

        string outPath = PathOf("п2.db");
        var ok = await Run("add", "steel-section", project, "--profile", "30К1", "--standard", "57837", "--steel", "С255",
            "--out", outPath);
        Assert.True(ok.Code == CliExitCode.Ok, ok.Err);
        Assert.Equal(before, File.ReadAllBytes(project));
        using var db = Load(outPath);
        Assert.Single(db.CrossSections);
    }

    /// <summary>Таблица «РСУ в стержнях» ЛИРА-САПР 10 рамы Гоши (14.11.2026), как в docx: оси ЛИРЫ, «НC» с латинской C.</summary>
    const string GoshaRsuCsv = """
        Номер;НC;Cт.;Гр.;Крит.;N (кН);My (кН*м);Qz (кН)
        1;1;1;А;2;-176;-48.313;24.202
        1;2;1;А;2;-176;-12.01;24.202
        1;3;1;А;1;-78;13.439;13.389
        1;3;1;А;6;-176;24.294;24.202
        1;4;1;А;1;-176;60.597;24.202
        1;5;1;А;1;-176;96.901;24.202
        2;1;1;А;1;-176;48.313;-24.202
        2;2;1;А;2;-176;12.01;-24.202
        2;3;1;А;2;-176;-24.294;-24.202
        2;3;1;А;5;-78;-13.439;-13.389
        2;4;1;А;2;-176;-60.597;-24.202
        2;5;1;А;2;-176;-96.901;-24.202
        3;1;1;А;2;-24.202;-96.902;141
        3;2;1;А;1;-24.202;61.723;70.5
        3;3;1;А;1;-24.202;114.6;
        3;4;1;А;1;-24.202;61.723;-70.5
        3;5;1;А;2;-24.202;-96.902;-141
        """;

    string GoshaCsv()
    {
        string path = PathOf("РСУ в стержнях.csv");
        File.WriteAllText(path, GoshaRsuCsv);
        return path;
    }

    [Fact]
    public async Task ImportForces_LiraTable_AndSplitByElements()
    {
        string project = PathOf("рама.db");
        await Run("new", project);

        var imported = await Run("import", "forces", project, GoshaCsv(), "--tag", "A: РСУ");
        Assert.True(imported.Code == CliExitCode.Ok, imported.Err);
        Assert.Contains("My (кН*м) → Mx (−)", imported.Out);

        var columns = await Run("split", "forces", project, "--set", "A: РСУ", "--elements", "1,2", "--tag", "колонны", "--json");
        Assert.True(columns.Code == CliExitCode.Ok, columns.Err);
        using (var json = JsonDocument.Parse(columns.Out))
            Assert.Equal(12, json.RootElement.GetProperty("rows").GetInt32());
        Assert.Equal(CliExitCode.Ok, (await Run("split", "forces", project, "--set", "1", "--elements", "3")).Code);
        var onlyBeam = await Run("import", "forces", project, GoshaCsv(), "--elements", "3", "--axes", "lira");
        Assert.Equal(CliExitCode.Ok, onlyBeam.Code);

        using var db = Load(project);
        Assert.Equal(["A: РСУ", "колонны", "A: РСУ — КЭ 3", "РСУ в стержнях"], db.ForceSets.OrderBy(f => f.Num).Select(f => f.Tag));
        var all = db.ForceSets.Single(f => f.Tag == "A: РСУ");
        Assert.Equal(17, all.Items.Count);
        var first = all.Items.OrderBy(i => i.Num).First();
        Assert.Equal((-176.0, 48.313, 24.202), (first.N, first.Mx, first.Vy));
        Assert.Equal((1, 1), (first.SourceElementNum, first.SourceSectionNum));
        Assert.Equal(5, db.ForceSets.Single(f => f.Tag == "A: РСУ — КЭ 3").Items.Count);
        Assert.Equal(5, db.ForceSets.Single(f => f.Tag == "РСУ в стержнях").Items.Count);
    }

    [Fact]
    public async Task ImportAndSplit_Errors_Usage_ProjectUnchanged()
    {
        string project = PathOf("п.db");
        await Run("new", project);
        await Run("import", "forces", project, GoshaCsv());
        byte[] before = File.ReadAllBytes(project);
        string other = PathOf("усилия.pdf");
        File.WriteAllText(other, "");

        Assert.Equal(CliExitCode.Usage, (await Run("import", "forces", project, other)).Code);
        Assert.Equal(CliExitCode.Usage, (await Run("import", "forces", project, PathOf("нет.csv"))).Code);
        Assert.Equal(CliExitCode.Usage, (await Run("import", "forces", project, GoshaCsv(), "--axes", "opencs")).Code);
        Assert.Equal(CliExitCode.Usage, (await Run("split", "forces", project, "--set", "нет", "--elements", "1")).Code);
        Assert.Equal(CliExitCode.Usage, (await Run("split", "forces", project, "--set", "1", "--elements", "9")).Code);
        Assert.Equal(CliExitCode.Usage, (await Run("split", "forces", project, "--set", "1")).Code);
        string outPath = PathOf("п2.db");
        Assert.Equal(CliExitCode.Usage, (await Run("split", "forces", project, "--set", "1", "--elements", "9", "--out", outPath)).Code);
        Assert.False(File.Exists(outPath));
        Assert.Equal(before, File.ReadAllBytes(project));
    }

    [Theory]
    [InlineData("1,2,5-8", "1, 2, 5-8")]
    [InlineData("3", "3")]
    [InlineData("4 , 1;2", "1, 2, 4")]
    public void ElementList_ParseAndFormat(string text, string formatted) =>
        Assert.Equal(formatted, ElementList.Format(ElementList.Parse(text)));

    [Fact]
    public void ElementList_Invalid_Throws() =>
        Assert.Throws<CliException>(() => ElementList.Parse("1,а"));
}
