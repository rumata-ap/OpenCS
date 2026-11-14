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
