using System.Text.Json.Nodes;
using CScore.Fem.Fragment.Masks;
using Microsoft.Data.Sqlite;
using OpenCS.Cli;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// <c>opencs fragment masks --from</c> (аудит GUI + CLI, раздел «Фрагменты»): снятие масок из готового результата CSfea
/// родителя — то же, что окно «Маски из родителя…»; <c>list schemas</c> показывает маски; <c>--delete</c> удаляет.
/// Проект — фикстура <see cref="StoreyFragmentMaskServiceTests.WriteProject"/>.
/// </summary>
[Collection("Loc language")]
public sealed class CliFragmentMasksTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_cli_masks_{Guid.NewGuid():N}");
    public CliFragmentMasksTests()
    {
        Directory.CreateDirectory(_dir);
        Loc.SetLanguage("ru-RU");
    }
    public void Dispose()
    {
        Loc.SetLanguage(null);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    static async Task<(int Code, string Out, string Err)> Cli(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = await CliApp.RunAsync(args, stdout, stderr);
        SqliteConnection.ClearAllPools();
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public async Task From_ListSchemas_Delete()
    {
        string path = Path.Combine(_dir, "p.db");
        string fragment = StoreyFragmentMaskServiceTests.WriteProject(path);

        var r = await Cli("fragment", "masks", path, fragment, "--from", "Линейный");
        Assert.True(r.Code == CliExitCode.Ok, r.Err + r.Out);
        using (var db = new DatabaseService(path))
        {
            db.LoadAll();
            var set = db.GetFragmentMasks(db.FemSchemas.Single(s => s.Tag == fragment).Id);
            Assert.NotNull(set);
            Assert.Equal(FragmentMaskSet.SourceCsfeaLinear, set!.SourceKind);
            Assert.NotEmpty(set.Items);
        }
        SqliteConnection.ClearAllPools();

        var listed = await Cli("--json", "list", "schemas", path);
        Assert.True(listed.Code == CliExitCode.Ok, listed.Err);
        var row = JsonNode.Parse(listed.Out)!.AsArray().Single(x => (string)x!["tag"]! == fragment)!;
        Assert.Equal("Каркас", (string)row["parent"]!);
        Assert.Equal("unchanged", (string)row["parentState"]!);
        Assert.False(string.IsNullOrEmpty((string?)row["masks"]));

        var deleted = await Cli("fragment", "masks", path, fragment, "--delete");
        Assert.True(deleted.Code == CliExitCode.Ok, deleted.Err);
        using (var db = new DatabaseService(path))
        {
            db.LoadAll();
            Assert.Null(db.GetFragmentMasks(db.FemSchemas.Single(s => s.Tag == fragment).Id, items: false));
        }
    }
}
