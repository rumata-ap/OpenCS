using System.IO;
using System.Text.Json.Nodes;
using CScore.Fem;
using CScore.Fem.Fields;
using OpenCS.Cli;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Подраздел 6г: поля из таблиц точек — вид <c>fields</c> внутри <c>femSchemas</c> документа opencs-model и служба
/// <see cref="FemFieldAuthoring"/> (проверка с путями, запись, число единиц на столбец).
/// </summary>
[Collection("Loc language")]
public sealed class CliFemFieldsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_cli_fields_{Guid.NewGuid():N}");
    public CliFemFieldsTests()
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

    string Doc(string name, string json)
    {
        File.WriteAllText(PathOf(name), json);
        return PathOf(name);
    }

    /// <summary>Схема «Рама» с пластиной П1 и группой «Плиты»; поля в документе не заданы.</summary>
    const string Base = """
        { "format": "opencs-model", "version": 1,
          "materials": [
            { "tag": "B25", "catalog": { "concrete": "B25" } },
            { "tag": "A500", "catalog": { "rebar": "A500" } } ],
          "plateSections": [ { "tag": "Плита 200", "h": 200, "concrete": "B25", "rebar": "A500" } ],
          "femSchemas": [
            { "tag": "Рама",
              "members": [
                { "tag": "П1", "type": "shell", "plateSection": "Плита 200",
                  "region": { "outline": [[0, 0, 3], [6, 0, 3], [6, 4, 3], [0, 4, 3]] } } ],
              "groups": [ { "tag": "Плиты", "memberType": "plate", "members": ["П1"] } ]
              FIELDS } ] }
        """;

    static string WithFields(string fields) => Base.Replace("FIELDS", fields.Length == 0 ? "" : ", " + fields);

    /// <summary>Поле C1 по пяти точкам таблицы (кН/м³) на группу «Плиты».</summary>
    const string C1Field = """
        "fields": [ { "tag": "C1 плита", "quantity": "foundation.c1",
          "columns": [ { "header": "C1, кН/м3", "component": "c1", "unit": "кН/м³" } ],
          "points": [ [0, 0, 12], [6, 0, 11], [6, 4, 10], [0, 4, 9], [3, 2, 10.5] ],
          "targets": { "groups": ["Плиты"] }, "maxDistanceM": 2 } ]
        """;

    static (DatabaseService Db, FemSchema Schema) Open(string project)
    {
        var db = new DatabaseService(project);
        db.LoadAll();
        return (db, db.FemSchemas.Single(s => s.Tag == "Рама"));
    }

    [Fact]
    public async Task Apply_Fields_DryRunNothing_ValuesInSi_ExportRoundTrip()
    {
        string project = PathOf("a.db");
        Assert.Equal(CliExitCode.Ok, (await Run("new", project)).Code);
        var dry = await Run("apply", project, Doc("dry.json", WithFields(C1Field)), "--dry-run");
        Assert.True(dry.Code == CliExitCode.Ok, dry.Err + dry.Out);
        using (var db0 = new DatabaseService(project)) { db0.LoadAll(); Assert.Empty(db0.FemSchemas); }

        var r = await Run("apply", project, Doc("seed.json", WithFields(C1Field)));
        Assert.True(r.Code == CliExitCode.Ok, r.Err + r.Out);
        var (db, schema) = Open(project);
        using (db)
        {
            var fields = db.GetFemFields(schema.Id);
            var f = Assert.Single(fields);
            Assert.Equal(("C1 плита", FemFieldQuantities.FoundationC1, 5), (f.Name, f.Quantity, f.Points.Count));
            // Значения хранятся в единицах ввода столбца (кН/м³), множитель к СИ — в столбце: 1000 Н/м³ на кН/м³.
            Assert.Equal(12, f.Points[0].Values[0], 9);
            Assert.Equal(1000, f.Definition.Columns[0].UnitFactor, 6);
            Assert.Equal(2, f.Definition.Settings.MaxDistance, 9);
            Assert.Single(f.Definition.Targets.GroupIds);
        }

        string e1 = PathOf("e1.json");
        Assert.Equal(CliExitCode.Ok, (await Run("export", "model", project, "--to", e1)).Code);
        var fieldsOut1 = JsonNode.Parse(File.ReadAllText(e1))!["femSchemas"]![0]!["fields"]!.AsArray();
        var o = fieldsOut1[0]!.AsObject();
        Assert.Equal("Плиты", o["targets"]!["groups"]![0]!.GetValue<string>());
        Assert.Equal(12, o["points"]![0]![2]!.GetValue<double>(), 9);   // точки — в единицах столбца, как в таблице
        Assert.Equal("interpolate", o["method"]!.GetValue<string>());

        // Кругооборот: выгрузку заново в новый проект — те же поля.
        string second = PathOf("b.db");
        Assert.Equal(CliExitCode.Ok, (await Run("new", second)).Code);
        Assert.Equal(CliExitCode.Ok, (await Run("apply", second, e1)).Code);
        string e2 = PathOf("e2.json");
        Assert.Equal(CliExitCode.Ok, (await Run("export", "model", second, "--to", e2)).Code);
        var fieldsOut2 = JsonNode.Parse(File.ReadAllText(e2))!["femSchemas"]![0]!["fields"];
        Assert.Equal(fieldsOut1.ToJsonString(), fieldsOut2!.ToJsonString());
    }

    [Fact]
    public async Task Apply_FieldErrors_WithPaths_NothingWritten()
    {
        string project = PathOf("e.db");
        Assert.Equal(CliExitCode.Ok, (await Run("new", project)).Code);
        // Ошибки разбора: строка точек не той длины, единица не из реестра, неизвестная величина.
        var parse = await Run("apply", project, Doc("bad1.json", WithFields("""
            "fields": [
              { "tag": "Первое", "quantity": "foundation.c1",
                "columns": [ { "component": "c1", "unit": "кН/м³" } ],
                "points": [ [0, 0, 12], [1, 1] ] },
              { "tag": "Второе", "quantity": "foundation.c1",
                "columns": [ { "component": "c1", "unit": "кН/м3" } ],
                "points": [ [0, 0, 12] ] },
              { "tag": "Третье", "quantity": "величина",
                "columns": [ { "component": "c1", "unit": "кН/м³" } ],
                "points": [ [0, 0, 1] ] } ]
            """)));
        Assert.Equal(CliExitCode.Usage, parse.Code);
        string text = parse.Out + parse.Err;
        foreach (var path in new[]
                 {
                     "femSchemas[0].fields[0].points[1]", "femSchemas[0].fields[1].columns[0].unit",
                     "femSchemas[0].fields[2].quantity",
                 })
            Assert.Contains(path, text);

        // Ошибки плана (после разбора): пустое имя, цель — группа, которой нет в схеме.
        var plan = await Run("apply", project, Doc("bad2.json", WithFields("""
            "fields": [
              { "tag": "", "quantity": "foundation.c1",
                "columns": [ { "component": "c1", "unit": "кН/м³" } ],
                "points": [ [0, 0, 12] ] },
              { "tag": "Другое", "quantity": "foundation.c1",
                "columns": [ { "component": "c1", "unit": "кН/м³" } ],
                "points": [ [0, 0, 12] ], "targets": { "groups": ["Нет такой"] } } ]
            """)));
        Assert.Equal(CliExitCode.Usage, plan.Code);
        string planText = plan.Out + plan.Err;
        Assert.Contains("femSchemas[0].fields[0].name", planText);
        Assert.Contains("femSchemas[0].fields[1].targets.groups[0]", planText);

        // Ничего не записано.
        using var db = new DatabaseService(project);
        db.LoadAll();
        Assert.Empty(db.FemSchemas);
    }

    /// <summary>
    /// Повторный apply того же документа поле не перезаписывает (кэш значений — подпись сетки — цел); изменённое поле
    /// пишется с очисткой кэша; перестановка полей в документе — порядок наложения, без перезаписи.
    /// </summary>
    [Fact]
    public async Task Apply_SameFieldsKeepsCache_OrderFollowsDocument()
    {
        string project = PathOf("o.db");
        Assert.Equal(CliExitCode.Ok, (await Run("new", project)).Code);
        const string a = """
            { "tag": "А", "quantity": "foundation.c1", "columns": [ { "component": "c1", "unit": "кН/м³" } ],
              "points": [ [0, 0, 5], [6, 4, 5] ], "targets": { "groups": ["Плиты"] } }
            """;
        const string b = """
            { "tag": "Б", "quantity": "foundation.c1", "columns": [ { "component": "c1", "unit": "кН/м³" } ],
              "points": [ [0, 0, 7], [6, 4, 7] ], "targets": { "groups": ["Плиты"] } }
            """;
        string ab = Doc("ab.json", WithFields($"\"fields\": [ {a}, {b} ]"));
        Assert.Equal(CliExitCode.Ok, (await Run("apply", project, ab)).Code);

        void Mark()
        {
            using var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={project};Pooling=False");
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE fem_fields SET mesh_signature='sig'";
            cmd.ExecuteNonQuery();
        }
        List<(string Name, string? Sig)> Read()
        {
            var (db, schema) = Open(project);
            using (db) return db.GetFemFields(schema.Id).Select(f => (f.Name, f.MeshSignature)).ToList();
        }

        Mark();
        Assert.Equal(CliExitCode.Ok, (await Run("apply", project, ab)).Code);
        Assert.Equal([("А", "sig"), ("Б", "sig")], Read());

        string ba = Doc("ba.json", WithFields($"\"fields\": [ {b}, {a.Replace("[6, 4, 5]", "[6, 4, 6]")} ]"));
        Assert.Equal(CliExitCode.Ok, (await Run("apply", project, ba)).Code);
        Assert.Equal([("Б", "sig"), ("А", (string?)null)], Read());
    }

    [Fact]
    public async Task Apply_FieldsArray_ReplacesByTag_OmittedKeepsFields()
    {
        string project = PathOf("r.db");
        Assert.Equal(CliExitCode.Ok, (await Run("new", project)).Code);
        Assert.Equal(CliExitCode.Ok, (await Run("apply", project, Doc("s1.json", WithFields(C1Field)))).Code);

        const string other = """
            { "format": "opencs-model", "version": 1, "femSchemas": [ { "tag": "Рама",
              "fields": [ { "tag": "Другое", "quantity": "foundation.c1",
                "columns": [ { "component": "c1", "unit": "кН/м³" } ],
                "points": [ [0, 0, 5], [6, 4, 5] ], "targets": { "groups": ["Плиты"] } } ] } ] }
            """;
        Assert.Equal(CliExitCode.Ok, (await Run("apply", project, Doc("s2.json", other))).Code);
        using (var db = new DatabaseService(project))
        {
            db.LoadAll();
            var schema = db.FemSchemas.Single(s => s.Tag == "Рама");
            Assert.Equal(new[] { "Другое" }, db.GetFemFields(schema.Id).Select(f => f.Name).ToArray());
        }

        // Без массива fields набор полей не трогается.
        Assert.Equal(CliExitCode.Ok, (await Run("apply", project, Doc("s3.json", """
            { "format": "opencs-model", "version": 1, "femSchemas": [ { "tag": "Рама" } ] }
            """))).Code);
        using (var db = new DatabaseService(project))
        {
            db.LoadAll();
            var schema = db.FemSchemas.Single(s => s.Tag == "Рама");
            Assert.Equal(new[] { "Другое" }, db.GetFemFields(schema.Id).Select(f => f.Name).ToArray());
        }
    }
}
