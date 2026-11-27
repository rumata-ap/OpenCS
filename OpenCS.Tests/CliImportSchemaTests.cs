using System.IO;
using System.Text;
using System.Text.Json;
using OpenCS.Cli;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Раздел «Импорт», 9в: <c>opencs import schema</c> — текст ЛИРЫ, текст SCAD, .lir, CSV ЛИРЫ; отказ COM-форматов вне
/// Windows; ошибки аргументов. Запись — та же служба, что у меню (<see cref="Services.FemSchemaImport"/>).
/// </summary>
[Collection("Loc language")]
public sealed class CliImportSchemaTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_cli_import_{Guid.NewGuid():N}");

    public CliImportSchemaTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        CliApp.ComAvailable = OperatingSystem.IsWindows;
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

    async Task<string> NewProject()
    {
        string db = PathOf("p.db");
        Assert.Equal(0, (await Run("new", db)).Code);
        return db;
    }

    static string WallText => Path.Combine(UiInventory.RepoRoot, "CScore.Tests", "Import", "Fixtures", "lira-text-wall.txt");

    [Fact]
    public async Task LiraText_ImportsSchemaWithBoundaryAndLoads_Json()
    {
        string db = await NewProject();
        var (code, output, err) = await Run("import", "schema", db, WallText, "--json");
        Assert.True(code == 0, err);
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        Assert.Equal("ТЕСТ_СТЕНА", root.GetProperty("schema").GetProperty("tag").GetString());
        Assert.Equal(425, root.GetProperty("nodes").GetInt32());
        Assert.Equal(384, root.GetProperty("shells").GetInt32());
        Assert.Contains(root.GetProperty("boundary").GetProperty("rows").EnumerateArray(), r => r.GetProperty("transferred").GetInt32() == 80);
        var cases = root.GetProperty("loadCases").EnumerateArray().ToList();
        Assert.Equal(2, cases.Count);
        Assert.Equal(-5000, cases[0].GetProperty("fzKn").GetDouble(), 0);
        Assert.True(root.GetProperty("normativeLoadValues").GetBoolean()); // по умолчанию в проекте — нормативные

        var (_, list, _) = await Run("list", "schemas", db, "--json");
        using var schemas = JsonDocument.Parse(list);
        var s = Assert.Single(schemas.RootElement.EnumerateArray());
        Assert.Equal(425, s.GetProperty("meshNodes").GetInt32());
    }

    [Fact]
    public async Task LiraText_TagOption_AndTextSummary()
    {
        Loc.SetLanguage(Loc.Russian);
        string db = await NewProject();
        var (code, output, err) = await Run("import", "schema", db, WallText, "--tag", "Стена А");
        Assert.True(code == 0, err);
        Assert.Contains("Схема #1 «Стена А»: узлов 425, стержней 0, пластин 384", output);
        Assert.Contains("  Связи: 80 / 0", output);
        Assert.Contains("«ПОСТОЯННОЕ»", output);
        Assert.Contains("нормативные", err); // предупреждение о величинах нагрузок — в журнал (stderr)
    }

    [Fact]
    public async Task ScadText_AutoDetectedByContent()
    {
        string db = await NewProject();
        string txt = PathOf("плита.txt");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        File.WriteAllText(txt,
            "(0;Version=21;SubVersion=1/1;\"T\";/)" +
            "(1/44 2 1 2 3 4 /)" +
            "(3/2 GE 1.8e+06 0.2 0.22 RO 2.5 Name \"Плита\"/)" +
            "(4/0 0 0/1 0 0/1 1 0/0 1 0/)", Encoding.GetEncoding(1251));
        var (code, output, err) = await Run("import", "schema", db, txt, "--json");
        Assert.True(code == 0, err);
        using var doc = JsonDocument.Parse(output);
        Assert.Equal("scad", doc.RootElement.GetProperty("schema").GetProperty("sourceType").GetString());
        Assert.Equal("плита", doc.RootElement.GetProperty("schema").GetProperty("tag").GetString());
        Assert.Equal(4, doc.RootElement.GetProperty("nodes").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("shells").GetInt32());
    }

    [Fact]
    public async Task Lir_FromSamples_ReadsMeshAndSaysWhatIsMissing()
    {
        string samples = Environment.GetEnvironmentVariable("OPENCES_LIR_SAMPLES_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "lir-samples");
        string lir = Path.Combine(samples, "Ферма_Митасова.lir");
        if (!File.Exists(lir)) return; // образцов на этом ПК нет
        Loc.SetLanguage(Loc.Russian);
        string db = await NewProject();
        var (code, output, err) = await Run("import", "schema", db, lir);
        Assert.True(code == 0, err);
        Assert.Contains("«Ферма_Митасова»", output);
        Assert.Contains("формат разобран не полностью", err);
        Assert.Contains("Ферма_Митасова.txt", err);
    }

    [Fact]
    public async Task ComFormats_RefusedOutsideWindows_WithAlternatives()
    {
        Loc.SetLanguage(Loc.Russian);
        string db = await NewProject();
        string spr = PathOf("проект.SPR");
        File.WriteAllBytes(spr, [1, 2, 3]);
        CliApp.ComAvailable = () => false;

        var (code, _, err) = await Run("import", "schema", db, spr);
        Assert.Equal(CliExitCode.Usage, code);
        Assert.Contains("только в Windows", err);
        Assert.Contains("текстовый файл SCAD", err);

        (code, _, err) = await Run("import", "schema", db, "--lira-api");
        Assert.Equal(CliExitCode.Usage, code);
        Assert.Contains("текстовый файл ЛИРЫ", err);
    }

    [Fact]
    public async Task ArgumentErrors_AreUsageAndProjectUnchanged()
    {
        Loc.SetLanguage(Loc.Russian);
        string db = await NewProject();
        Assert.Equal(CliExitCode.Usage, (await Run("import", "schema", db)).Code);
        Assert.Equal(CliExitCode.Usage, (await Run("import", "schema", db, WallText, "--lira-api")).Code);
        string dwg = PathOf("x.dwg");
        File.WriteAllText(dwg, "");
        var (code, _, err) = await Run("import", "schema", db, dwg);
        Assert.Equal(CliExitCode.Usage, code);
        Assert.Contains("--format", err);
        string csv = PathOf("nodes.csv");
        File.WriteAllText(csv, "");
        (code, _, err) = await Run("import", "schema", db, csv);
        Assert.Equal(CliExitCode.Usage, code);
        Assert.Contains("--elements-file", err);

        var (_, list, _) = await Run("list", "schemas", db, "--json");
        Assert.Empty(JsonDocument.Parse(list).RootElement.EnumerateArray());
    }
}
