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

    /// <summary>Каркас 6×6 м: пять колонн на два этажа, плиты П1 (z = 3) и П2 (z = 6), два загружения; фрагмент этажа +3.</summary>
    const string Frame = """
        { "format": "opencs-model", "version": 1,
          "materials": [ { "tag": "B25", "catalog": { "concrete": "B25" } }, { "tag": "A500", "catalog": { "rebar": "A500" } } ],
          "sections": [ { "tag": "К-400", "areas": [ { "tag": "Бетон", "category": "region", "material": "B25", "diagram": "sp63",
            "polygon": { "outer": [[-200, -200], [200, -200], [200, 200], [-200, 200]] } } ] } ],
          "plateSections": [ { "tag": "Плита 200", "h": 200, "concrete": "B25", "rebar": "A500" } ],
          "femSchemas": [ { "tag": "Каркас", "mesh": { "barStepM": 1, "plateStepM": 1 },
            "nodes": [
              { "tag": 1, "x": 0, "y": 0, "z": 0, "fix": ["x", "y", "z", "ux", "uy", "uz"] },
              { "tag": 2, "x": 6, "y": 0, "z": 0, "fix": ["x", "y", "z", "ux", "uy", "uz"] },
              { "tag": 3, "x": 6, "y": 6, "z": 0, "fix": ["x", "y", "z", "ux", "uy", "uz"] },
              { "tag": 4, "x": 0, "y": 6, "z": 0, "fix": ["x", "y", "z", "ux", "uy", "uz"] },
              { "tag": 5, "x": 3, "y": 3, "z": 0, "fix": ["x", "y", "z", "ux", "uy", "uz"] },
              { "tag": 11, "x": 0, "y": 0, "z": 3 }, { "tag": 12, "x": 6, "y": 0, "z": 3 }, { "tag": 13, "x": 6, "y": 6, "z": 3 },
              { "tag": 14, "x": 0, "y": 6, "z": 3 }, { "tag": 15, "x": 3, "y": 3, "z": 3 },
              { "tag": 21, "x": 0, "y": 0, "z": 6 }, { "tag": 22, "x": 6, "y": 0, "z": 6 }, { "tag": 23, "x": 6, "y": 6, "z": 6 },
              { "tag": 24, "x": 0, "y": 6, "z": 6 }, { "tag": 25, "x": 3, "y": 3, "z": 6 } ],
            "members": [
              { "tag": "К1", "nodes": [1, 11], "section": "К-400" }, { "tag": "К2", "nodes": [2, 12], "section": "К-400" },
              { "tag": "К3", "nodes": [3, 13], "section": "К-400" }, { "tag": "К4", "nodes": [4, 14], "section": "К-400" },
              { "tag": "К5", "nodes": [5, 15], "section": "К-400" }, { "tag": "К11", "nodes": [11, 21], "section": "К-400" },
              { "tag": "К12", "nodes": [12, 22], "section": "К-400" }, { "tag": "К13", "nodes": [13, 23], "section": "К-400" },
              { "tag": "К14", "nodes": [14, 24], "section": "К-400" }, { "tag": "К15", "nodes": [15, 25], "section": "К-400" },
              { "tag": "П1", "type": "shell", "plateSection": "Плита 200", "region": { "outline": [[0, 0, 3], [6, 0, 3], [6, 6, 3], [0, 6, 3]] } },
              { "tag": "П2", "type": "shell", "plateSection": "Плита 200", "region": { "outline": [[0, 0, 6], [6, 0, 6], [6, 6, 6], [0, 6, 6]] } } ],
            "loadCases": [
              { "tag": "Пост", "type": "permanent", "selfWeight": 1, "loads": [ { "plates": ["П1", "П2"], "p": -2 } ] },
              { "tag": "Врем", "type": "short_term", "loads": [ { "plates": ["П1", "П2"], "p": -3 } ] } ] } ],
          "femFragments": [ { "tag": "Этаж +3", "parent": "Каркас", "storey": { "floor": 3 }, "keepParentMesh": false } ]
        }
        """;

    /// <summary>
    /// <c>--all</c> на каркасе с плитами (нужен Python-пакет gmsh: служебный расчёт родителя строит сетку): маски сняты по
    /// всем пяти разрезам колонн и обоим загружениям; сумма Fz служебного расчёта — вес и нагрузки двух этажей.
    /// </summary>
    [Fact]
    public async Task All_FrameWithSlabs_MasksForEveryCutAndLoadCase()
    {
        _ = OpenCS.ExternalTools.PythonRuntime.FindWithModules(["gmsh"], out _)
            ?? throw Xunit.Sdk.SkipException.ForSkip("Python с пакетом gmsh не найден.");
        string path = Path.Combine(_dir, "frame.db");
        string doc = Path.Combine(_dir, "frame.json");
        File.WriteAllText(doc, Frame);
        Assert.Equal(CliExitCode.Ok, (await Cli("new", path)).Code);
        Assert.Equal(CliExitCode.Ok, (await Cli("apply", path, doc)).Code); // фрагмент — после сетки родителя
        var missing = await Cli("run", "mesh", path, "Нет такой", "--in-place");
        Assert.Equal(CliExitCode.Usage, missing.Code);
        var mesh = await Cli("run", "mesh", path, "Каркас", "--in-place"); // ключ — имя, как в документе
        Assert.True(mesh.Code == CliExitCode.Ok, mesh.Err + mesh.Out);
        var built = await Cli("apply", path, doc);
        Assert.True(built.Code == CliExitCode.Ok, built.Err + built.Out);

        var r = await Cli("fragment", "masks", path, "Этаж +3", "--all");
        Assert.True(r.Code == CliExitCode.Ok, r.Err + r.Out);
        // Пост: плиты 2·36·0,2·25 = 360, колонны 10·3·0,16·25 = 120, нагрузка 2·36·2 = 144 → 624 кН; Врем: 3·72 = 216 кН.
        string log = r.Out + r.Err; // строки служебного расчёта — в журнал (stderr)
        Assert.Contains("ΣFz = -624 кН", log);
        Assert.Contains("ΣFz = -216 кН", log);
        using var db = new DatabaseService(path);
        db.LoadAll();
        var set = db.GetFragmentMasks(db.FemSchemas.Single(s => s.Tag == "Этаж +3").Id);
        Assert.NotNull(set);
        Assert.Equal(5, set!.Items.Select(i => i.CutId).Distinct().Count());
        Assert.Equal(2, set.Items.Select(i => i.Key).Distinct().Count());
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
