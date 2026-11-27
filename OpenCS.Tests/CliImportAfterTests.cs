using System.IO;
using System.Text.Json;
using CScore.Fem;
using CScore.Import;
using OpenCS.Cli;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Раздел «Импорт», 9г: команды после импорта — <c>import boundary</c>, <c>import loads</c>, <c>convert blocks</c>,
/// <c>import forces --schema --target</c> (привязка), <c>--api</c> вне Windows — отказ.
/// </summary>
[Collection("Loc language")]
public sealed class CliImportAfterTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_cli_import_after_{Guid.NewGuid():N}");

    public CliImportAfterTests() => Directory.CreateDirectory(_dir);

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

    static string WallText => Path.Combine(UiInventory.RepoRoot, "CScore.Tests", "Import", "Fixtures", "lira-text-wall.txt");

    async Task<string> WallProject()
    {
        string db = PathOf("wall.db");
        Assert.Equal(0, (await Run("new", db)).Code);
        var (code, _, err) = await Run("import", "schema", db, WallText);
        Assert.True(code == 0, err);
        return db;
    }

    static int Count(string db, string sql)
    {
        using var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    [Fact]
    public async Task ImportLoads_Again_ReplacesTransferred_NoDuplicates()
    {
        string db = await WallProject();
        int cases = Count(db, "SELECT COUNT(*) FROM fem_load_cases");
        int loads = Count(db, "SELECT COUNT(*) FROM fem_element_loads") + Count(db, "SELECT COUNT(*) FROM fem_mesh_node_loads");

        var (code, output, err) = await Run("import", "loads", db, "--schema", "ТЕСТ_СТЕНА", "--json");
        Assert.True(code == 0, err);
        using var doc = JsonDocument.Parse(output);
        Assert.Equal(2, doc.RootElement.GetProperty("loadCases").GetArrayLength());
        Assert.Equal(cases, Count(db, "SELECT COUNT(*) FROM fem_load_cases"));
        Assert.Equal(loads, Count(db, "SELECT COUNT(*) FROM fem_element_loads") + Count(db, "SELECT COUNT(*) FROM fem_mesh_node_loads"));
    }

    [Fact]
    public async Task ImportBoundary_UsesRememberedTextFile()
    {
        string db = await WallProject();
        int supports = Count(db, "SELECT COUNT(*) FROM fem_mesh_node_supports");
        var (code, output, err) = await Run("import", "boundary", db, "--schema", "1", "--json");
        Assert.True(code == 0, err);
        using var doc = JsonDocument.Parse(output);
        Assert.Contains(doc.RootElement.GetProperty("boundary").GetProperty("rows").EnumerateArray(),
            r => r.GetProperty("transferred").GetInt32() == 80);
        Assert.Equal(supports, Count(db, "SELECT COUNT(*) FROM fem_mesh_node_supports"));
    }

    [Fact]
    public async Task ImportBoundary_ForeignTextFile_RefusedWithMismatch()
    {
        Loc.SetLanguage(Loc.Russian);
        string db = await WallProject();
        string other = PathOf("other.txt");
        File.WriteAllText(other, File.ReadAllText(WallText).Replace("44 1 5 81 1 80 /", "44 1 5 81 1 999 /"));
        var (code, _, err) = await Run("import", "boundary", db, "--schema", "1", other);
        Assert.NotEqual(0, code);
        Assert.False(string.IsNullOrWhiteSpace(err));
    }

    [Fact]
    public async Task ConvertBlocks_CreatesMembers_FilterByType()
    {
        string db = PathOf("blocks.db");
        using (var d = new DatabaseService(db))
        {
            var schema = new FemSchema { Tag = "Схема", SourceType = "lira" };
            d.SaveFemSchema(schema);
            FemMeshNode N(string tag, double x, double y, double z) =>
                new() { NodeTag = tag, X = x, Y = y, Z = z, Origin = FemMember.MeshSourceImported };
            FemElement E(string tag, string type, params int[] nodes) => new()
            {
                ElemTag = tag, ElemType = type, Origin = FemMember.MeshSourceImported,
                NodeIdsJson = JsonSerializer.Serialize(nodes.Length == 4 ? new[] { nodes[0], nodes[1], nodes[3], nodes[2] } : nodes),
                SectionTag = type == "beam" ? "К 40" : "Пл 200", ThicknessM = type == "shell" ? 0.2 : null,
            };
            d.SaveFemMeshSnapshot(schema.Id,
                [N("1", 0, 0, 0), N("2", 0, 0, 1.5), N("3", 0, 0, 3), N("4", 2, 0, 3), N("5", 2, 2, 3), N("6", 0, 2, 3)],
                [E("1", "beam", 1, 2), E("2", "beam", 2, 3), E("3", "shell", 3, 4, 5, 6)]);
            d.SaveFemSchemaConstructiveBlocks(schema.Id,
            [
                new LiraConstructiveBlockRecord(1, "КОЛОННА", "1 этаж", "К-1", "", [1, 2]),
                new LiraConstructiveBlockRecord(2, "ПЛИТА", "1 этаж", "", "", [3]),
            ]);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var (code, output, err) = await Run("convert", "blocks", db, "--schema", "Схема", "--type", "колонна", "--json");
        Assert.True(code == 0, err);
        using (var doc = JsonDocument.Parse(output))
            Assert.Single(doc.RootElement.GetProperty("blocks").EnumerateArray());
        Assert.Equal(1, Count(db, "SELECT COUNT(*) FROM fem_members"));

        (code, _, err) = await Run("convert", "blocks", db, "--schema", "Схема");
        Assert.True(code == 0, err);
        Assert.Equal(2, Count(db, "SELECT COUNT(*) FROM fem_members")); // повтор колонны — замена, не дубликат

        (code, _, _) = await Run("convert", "blocks", db, "--schema", "Схема", "--type", "Балка");
        Assert.Equal(CliExitCode.Usage, code);
    }

    [Fact]
    public async Task ImportForces_TableWithTarget_LinkedToGroupAndSchema()
    {
        string db = await WallProject();
        // Группа КЭ схемы — создана импортом (по жёсткости); её имя — цель.
        string group;
        using (var d = new DatabaseService(db)) { d.LoadAll(); group = d.FemSchemas[0].MemberGroups[0].Tag; }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        string csv = PathOf("forces.csv");
        File.WriteAllText(csv, "Номер;НC;N (кН);My (кН*м);Qz (кН)\n1;1;-176;-48.313;24.202\n2;1;-120;12;6\n");

        var (code, output, err) = await Run("import", "forces", db, csv, "--schema", "1", "--target", group, "--json");
        Assert.True(code == 0, err);
        int setId = JsonDocument.Parse(output).RootElement.GetProperty("sets")[0].GetProperty("id").GetInt32();
        using var d2 = new DatabaseService(db);
        d2.LoadAll();
        var set = d2.ForceSets.Single(s => s.Id == setId);
        Assert.Equal(d2.FemSchemas[0].Id, set.SourceSchemaId);
        Assert.Equal(d2.FemSchemas[0].MemberGroups[0].Id, set.SourceMemberId);
    }

    [Fact]
    public async Task ImportForces_TargetErrors_AndApiOutsideWindows()
    {
        Loc.SetLanguage(Loc.Russian);
        string db = await WallProject();
        string csv = PathOf("forces.csv");
        File.WriteAllText(csv, "Номер;НC;N (кН)\n1;1;-176\n");
        var (code, _, err) = await Run("import", "forces", db, csv, "--target", "Нет");
        Assert.Equal(CliExitCode.Usage, code);
        Assert.Contains("--schema", err);
        (code, _, err) = await Run("import", "forces", db, csv, "--schema", "1", "--target", "Нет такой");
        Assert.Equal(CliExitCode.Usage, code);
        Assert.Contains("«Нет такой»", err);

        CliApp.ComAvailable = () => false;
        (code, _, err) = await Run("import", "forces", db, "--api", "lira", "--schema", "1");
        Assert.Equal(CliExitCode.Usage, code);
        Assert.Contains("только в Windows", err);
        (code, _, err) = await Run("import", "forces", db, "--api", "scad", "--schema", "1", "--kind", "rsn");
        Assert.Equal(CliExitCode.Usage, code);
    }
}
