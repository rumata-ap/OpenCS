using System.IO;
using System.Text.Json.Nodes;
using OpenCS.Cli;
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
}
