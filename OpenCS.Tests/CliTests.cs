using System.IO;
using System.Text.Json;
using OpenCS.Cli;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Срез 0.4: консоль opencs в процессе — команды, коды возврата, работа на копии файла проекта.
/// Консоль задаёт язык строк глобально, поэтому тесты идут в коллекции без параллельности с LocTests.
/// </summary>
[Collection("Loc language")]
public sealed class CliTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_cli_{Guid.NewGuid():N}");
    readonly string _project;
    readonly int _taskId;

    public CliTests()
    {
        Directory.CreateDirectory(_dir);
        _project = Path.Combine(_dir, "Проект.db");
        (_, _taskId) = TestProjectDb.Create(_project);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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
        return (code, stdout.ToString(), stderr.ToString());
    }

    byte[] ProjectBytes() => File.ReadAllBytes(_project);

    static int TaskResultCount(string path, int taskId)
    {
        using var db = new DatabaseService(path);
        db.LoadAll();
        return db.CalcResults.Count(r => r.TaskId == taskId);
    }

    [Fact]
    public async Task Info_Json_CountsObjects_AndLeavesProjectFileUntouched()
    {
        byte[] before = ProjectBytes();

        var (code, output, _) = await Run("info", _project, "--json");

        Assert.Equal(CliExitCode.Ok, code);
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        Assert.Equal(DatabaseService.SchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(2, root.GetProperty("materials").GetInt32());
        Assert.Equal(1, root.GetProperty("crossSections").GetInt32());
        Assert.Equal(1, root.GetProperty("calcTasks").GetInt32());
        Assert.Equal(before, ProjectBytes());
        Assert.Empty(Directory.GetDirectories(Path.GetTempPath(), "opencs-cli-*")
            .Where(d => Directory.GetFiles(d, "Проект.db").Length > 0));
    }

    [Fact]
    public async Task ListTasks_ShowsTaskWithSection()
    {
        var (code, output, _) = await Run("list", "tasks", _project);

        Assert.Equal(CliExitCode.Ok, code);
        Assert.Contains("Прогиб", output);
        Assert.Contains("sp63_deflection", output);
        Assert.Contains("Балка", output);
    }

    [Fact]
    public async Task RunTask_WithOut_SavesResultThere_SourceUntouched()
    {
        byte[] before = ProjectBytes();
        string outPath = Path.Combine(_dir, "с результатами.db");

        var (code, output, err) = await Run("run", "task", _project, _taskId.ToString(), "--out", outPath, "-q");

        Assert.True(code == CliExitCode.Ok, err);
        Assert.Contains("Прогиб", output);
        Assert.Equal(before, ProjectBytes());
        Assert.Equal(1, TaskResultCount(outPath, _taskId));
        Assert.Contains(outPath, err);
    }

    [Fact]
    public async Task RunTask_WithoutOut_ResultsDiscarded()
    {
        byte[] before = ProjectBytes();

        var (code, _, err) = await Run("run", "task", _project, "--all");

        Assert.Equal(CliExitCode.Ok, code);
        Assert.Contains(Loc.S("CliResultsDiscarded"), err);
        Assert.Equal(before, ProjectBytes());
    }

    [Fact]
    public async Task RunTask_InPlace_WritesProjectFile()
    {
        var (code, _, err) = await Run("run", "task", _project, "--all", "--in-place");

        Assert.True(code == CliExitCode.Ok, err);
        Assert.Equal(1, TaskResultCount(_project, _taskId));
    }

    [Fact]
    public async Task RunTask_InPlace_FileInUse_Refused()
    {
        using (new DatabaseService(_project))
        {
            var (code, _, err) = await Run("run", "task", _project, "--all", "--in-place");
            Assert.Equal(CliExitCode.Usage, code);
            Assert.Contains(string.Format(Loc.S("CliErrInUse"), _project), err);
        }
    }

    [Fact]
    public async Task RunTask_Json_WithData()
    {
        var (code, output, _) = await Run("run", "task", _project, _taskId.ToString(), "--json", "--with-data", "-q");

        Assert.Equal(CliExitCode.Ok, code);
        using var json = JsonDocument.Parse(output);
        var item = Assert.Single(json.RootElement.EnumerateArray().ToList());
        Assert.Equal("task", item.GetProperty("kind").GetString());
        Assert.Equal(_taskId, item.GetProperty("id").GetInt32());
        Assert.True(item.GetProperty("resultId").GetInt32() > 0);
        Assert.NotEqual(JsonValueKind.Undefined, item.GetProperty("data").ValueKind);
        Assert.NotEmpty(item.GetProperty("messages").EnumerateArray());
    }

    [Fact]
    public async Task ExportResult_OfTask_MatchesSavedDataJson()
    {
        string outPath = Path.Combine(_dir, "out.db");
        await Run("run", "task", _project, "--all", "--out", outPath, "-q");
        string file = Path.Combine(_dir, "result.json");

        var (code, _, err) = await Run("export", "result", outPath, "--task", _taskId.ToString(), "-o", file);

        Assert.True(code == CliExitCode.Ok, err);
        using var exported = JsonDocument.Parse(File.ReadAllText(file));
        using var db = new DatabaseService(outPath);
        db.LoadAll();
        var saved = db.CalcResults.Single(r => r.TaskId == _taskId);
        Assert.Equal(saved.Id, exported.RootElement.GetProperty("id").GetInt32());
        using var data = JsonDocument.Parse(saved.DataJson);
        Assert.True(JsonElement.DeepEquals(data.RootElement, exported.RootElement.GetProperty("data")));
    }

    [Fact]
    public async Task UsageErrors_ExitCode2()
    {
        string notDb = Path.Combine(_dir, "текст.db");
        File.WriteAllText(notDb, "не база");
        string existing = Path.Combine(_dir, "есть.db");
        File.WriteAllText(existing, "");

        Assert.Equal(CliExitCode.Usage, (await Run("run", "task", _project, "999")).Code);
        Assert.Equal(CliExitCode.Usage, (await Run("run", "task", _project)).Code);
        Assert.Equal(CliExitCode.Usage, (await Run("run", "task", _project, "1", "--all")).Code);
        Assert.Equal(CliExitCode.Usage, (await Run("info", Path.Combine(_dir, "нет.db"))).Code);
        Assert.Equal(CliExitCode.Usage, (await Run("info", notDb)).Code);
        Assert.Equal(CliExitCode.Usage, (await Run("run", "task", _project, "--all", "--out", existing)).Code);
        Assert.Equal(CliExitCode.Usage, (await Run("run", "task", _project, "--all", "--out", _project)).Code);
        Assert.Equal(CliExitCode.Usage, (await Run("info", _project, "--lang", "de")).Code);
        Assert.Equal(CliExitCode.Usage, (await Run("export", "result", _project)).Code);
    }

    [Fact]
    public async Task LangEn_MessagesInEnglish()
    {
        var (code, output, _) = await Run("--lang", "en", "info", _project);

        Assert.Equal(CliExitCode.Ok, code);
        Assert.Contains("Cross sections", output);
        Assert.DoesNotContain("Сечения", output);
    }
}
