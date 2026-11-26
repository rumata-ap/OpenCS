using System.IO;
using System.Text.Json.Nodes;
using CScore.Fem;
using OpenCS.Cli;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Консоль: вид femSchemas документа opencs-model — узлы, КонЭ (стержни и пластины), группы; list schemas.</summary>
[Collection("Loc language")]
public sealed class CliFemSchemaTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_cli_fem_{Guid.NewGuid():N}");
    public CliFemSchemaTests()
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

    /// <summary>Рама: две колонны и ригель (сечение Б-1), плита 6×4 м на отметке 3 м с отверстием, группа.</summary>
    const string Seed = """
        { "format": "opencs-model", "version": 1,
          "materials": [
            { "tag": "B25", "catalog": { "concrete": "B25" } },
            { "tag": "A500", "catalog": { "rebar": "A500" } } ],
          "sections": [ { "tag": "Б-1", "areas": [
            { "tag": "Бетон", "category": "region", "material": "B25", "diagram": "sp63",
              "polygon": { "outer": [[-200, -300], [200, -300], [200, 300], [-200, 300]] } } ] } ],
          "plateSections": [ { "tag": "Плита 200", "h": 200, "concrete": "B25", "rebar": "A500" } ],
          "femSchemas": [
            { "tag": "Рама",
              "mesh": { "barStepM": 0.5, "plateStepM": 0.4 },
              "nodes": [
                { "tag": 1, "x": 0, "y": 0, "z": 0, "fix": ["x", "y", "z", "rx", "ry", "rz"] },
                { "tag": 2, "x": 0, "y": 0, "z": 3 },
                { "tag": 3, "x": 6, "y": 0, "z": 3 },
                { "tag": 4, "x": 6, "y": 0, "z": 0, "fix": ["x", "y", "z"] } ],
              "members": [
                { "tag": "К1", "nodes": [1, 2], "section": "Б-1", "rotationDeg": 90 },
                { "tag": "Р1", "nodes": [2, 3], "section": "Б-1", "meshStepM": 0.25, "torsion": { "gj": 1200 } },
                { "tag": "К2", "nodes": [3, 4], "section": "Б-1" },
                { "tag": "П1", "type": "shell", "plateSection": "Плита 200",
                  "region": { "outline": [[0, 0, 3], [6, 0, 3], [6, 4, 3], [0, 4, 3]],
                              "holes": [ [[2, 1, 3], [3, 1, 3], [3, 2, 3], [2, 2, 3]] ] } } ],
              "groups": [ { "tag": "Колонны", "memberType": "column", "members": ["К1", "К2"] } ] } ] }
        """;

    static (FemSchema Schema, List<FemNode> Nodes, List<FemMember> Members, List<CScore.Planar.PlanarRegion> Regions) Load(string project, string tag = "Рама")
    {
        using var db = new DatabaseService(project);
        db.LoadAll();
        var s = db.FemSchemas.Single(x => x.Tag == tag);
        return (s, db.GetFemNodes(s.Id), db.GetFemMembers(s.Id), db.GetPlanarRegions(s.Id));
    }

    [Fact]
    public async Task Apply_CreatesSchema_DryRunWritesNothing_RoundTripSame()
    {
        string first = await NewProject("a.db");
        var dry = await Run("apply", first, Doc("seed.json", Seed), "--dry-run");
        Assert.True(dry.Code == CliExitCode.Ok, dry.Err);
        using (var db = new DatabaseService(first)) { db.LoadAll(); Assert.Empty(db.FemSchemas); }

        var r = await Run("apply", first, PathOf("seed.json"));
        Assert.True(r.Code == CliExitCode.Ok, r.Err);
        var (schema, nodes, members, regions) = Load(first);
        Assert.Equal("internal", schema.SourceType);
        using (var db = new DatabaseService(first)) Assert.Equal(0.5, db.GetFemSchemaMeshSteps(schema.Id).Bar);
        Assert.Equal(4, nodes.Count);
        Assert.Equal(FemBoundaryDofs.All, nodes.Single(n => n.NodeTag == "1").DofMask);
        Assert.Equal(0b111, nodes.Single(n => n.NodeTag == "4").DofMask);
        var r1 = members.Single(m => m.ElemTag == "Р1");
        Assert.Equal("[2,3]", r1.NodeIdsJson);
        Assert.Equal(0.25, r1.TargetMeshLengthM);
        Assert.Equal(1.2e6, r1.GjManualValue!.Value, 6);
        Assert.Equal(90, members.Single(m => m.ElemTag == "К1").RotationDeg);
        var plate = members.Single(m => m.ElemTag == "П1");
        Assert.Equal("shell", plate.ElemType);
        var region = Assert.Single(regions);
        Assert.Equal(region.Id, plate.PlanarRegionId);
        Assert.Equal(3, region.Frame.Origin.Z, 9);
        Assert.Equal(1, region.Frame.LocalZ.Z, 9);
        Assert.Equal(2, region.Contours.Count);

        string e1 = PathOf("e1.json");
        Assert.True((await Run("export", "model", first, "--to", e1)).Code == CliExitCode.Ok);
        string second = await NewProject("b.db");
        var apply = await Run("apply", second, e1);
        Assert.True(apply.Code == CliExitCode.Ok, apply.Err);
        string e2 = PathOf("e2.json");
        Assert.True((await Run("export", "model", second, "--to", e2)).Code == CliExitCode.Ok);
        var a = JsonNode.Parse(File.ReadAllText(e1))!["femSchemas"];
        var b = JsonNode.Parse(File.ReadAllText(e2))!["femSchemas"];
        Assert.Equal(a!.ToJsonString(), b!.ToJsonString());
        Assert.Equal(4, a![0]!["members"]!.AsArray().Count);
    }

    [Fact]
    public async Task Apply_Again_KeepsGroupAndRegionIds_OmittedArrayUntouched_RemovedWarned()
    {
        string project = await NewProject();
        Assert.Equal(CliExitCode.Ok, (await Run("apply", project, Doc("seed.json", Seed))).Code);
        var before = Load(project);
        int groupId = before.Schema.MemberGroups.Single().Id;
        int regionId = before.Regions.Single().Id;

        // Тот же документ повторно: области не перезаписываются, группа — тот же Id.
        Assert.Equal(CliExitCode.Ok, (await Run("apply", project, PathOf("seed.json"))).Code);
        var again = Load(project);
        Assert.Equal(groupId, again.Schema.MemberGroups.Single().Id);
        Assert.Equal(regionId, again.Regions.Single().Id);

        // Только КонЭ, без узлов и групп: узлы и группа не трогаются, удалённая пластина — с областью и предупреждением.
        var r = await Run("apply", project, Doc("u.json", """
            { "format": "opencs-model", "version": 1,
              "femSchemas": [ { "tag": "Рама", "members": [
                { "tag": "К1", "nodes": [1, 2], "section": "Б-1" },
                { "tag": "Р1", "nodes": [2, 3], "section": "Б-1" },
                { "tag": "К2", "nodes": [3, 4], "section": "Б-1" } ] } ] }
            """));
        Assert.True(r.Code == CliExitCode.Ok, r.Err);
        Assert.Contains("КонЭ — 1", r.Out + r.Err);
        var after = Load(project);
        Assert.Equal(4, after.Nodes.Count);
        Assert.Equal(groupId, after.Schema.MemberGroups.Single().Id);
        Assert.Empty(after.Regions);
        Assert.Equal(0, after.Members.Single(m => m.ElemTag == "К1").RotationDeg);
    }

    [Fact]
    public async Task Apply_Errors_WithPaths_NothingWritten()
    {
        string project = await NewProject();
        var r = await Run("apply", project, Doc("bad.json", """
            { "format": "opencs-model", "version": 1,
              "femSchemas": [
                { "tag": "Ошибки",
                  "nodes": [ { "tag": 1, "x": 0, "y": 0, "z": 0 }, { "tag": "А", "x": 1, "y": 0, "z": 0 }, { "tag": 3, "x": 2, "y": 0, "z": 0, "fix": ["q"] } ],
                  "members": [
                    { "tag": "С1", "nodes": [1, 9] },
                    { "tag": "С2", "nodes": [1, 1] },
                    { "tag": "С3", "nodes": [1, 3], "section": "Нет такого" },
                    { "tag": "П1", "type": "shell", "region": { "outline": [[0, 0, 0], [1, 0, 0], [1, 1, 0.5], [0, 1, 0]] } } ],
                  "groups": [ { "tag": "Г", "members": ["С1", "Призрак"] } ] } ] }
            """));
        Assert.Equal(CliExitCode.Usage, r.Code);
        Assert.Contains("femSchemas[0].nodes[1].tag", r.Err);
        Assert.Contains("femSchemas[0].nodes[2].fix[0]", r.Err);
        Assert.Contains("femSchemas[0].members[1].nodes", r.Err);
        Assert.Contains("femSchemas[0].members[2].section", r.Err);
        using var db = new DatabaseService(project);
        db.LoadAll();
        Assert.Empty(db.FemSchemas);
    }

    [Fact]
    public async Task Apply_TopologyAndPlanarityErrors_WithPaths()
    {
        string project = await NewProject();
        var r = await Run("apply", project, Doc("bad.json", """
            { "format": "opencs-model", "version": 1,
              "femSchemas": [
                { "tag": "Ошибки",
                  "nodes": [ { "tag": 1, "x": 0, "y": 0, "z": 0 }, { "tag": 2, "x": 1, "y": 0, "z": 0 } ],
                  "members": [
                    { "tag": "С1", "nodes": [1, 9] },
                    { "tag": "П1", "type": "shell", "region": { "outline": [[0, 0, 0], [1, 0, 0], [1, 1, 0.5], [0, 1, 0]] } } ],
                  "groups": [ { "tag": "Г", "members": ["С1", "Призрак"] } ] } ] }
            """));
        Assert.Equal(CliExitCode.Usage, r.Code);
        Assert.Contains("femSchemas[0].members[0]", r.Err);
        Assert.Contains("femSchemas[0].members[1].region.outline", r.Err);
        Assert.Contains("femSchemas[0].groups[0].members[1]", r.Err);
    }

    [Fact]
    public async Task Apply_ImportedSchemaWithSameTag_Refused_ExportSkipsIt()
    {
        string project = await NewProject();
        using (var db = new DatabaseService(project))
            db.SaveFemSchema(new FemSchema { Tag = "ЛИРА", SourceType = "lira" });
        var r = await Run("apply", project, Doc("l.json", """
            { "format": "opencs-model", "version": 1, "femSchemas": [ { "tag": "ЛИРА", "nodes": [] } ] }
            """));
        Assert.Equal(CliExitCode.Usage, r.Code);
        Assert.Contains("femSchemas[0].tag", r.Err);
        var ex = await Run("export", "model", project, "--to", PathOf("e.json"), "--only", "femSchemas");
        Assert.True(ex.Code == CliExitCode.Ok, ex.Err);
        Assert.Empty(JsonNode.Parse(File.ReadAllText(PathOf("e.json")))!["femSchemas"]!.AsArray());
        Assert.Contains("ЛИРА", ex.Out + ex.Err);
    }

    /// <summary>Рама с ГУ 6б: пружины узла, АЖТ ригеля, линейные связи по внешнему контуру и стороне отверстия.</summary>
    static string BoundarySeed => Seed.Replace("""{ "tag": 2, "x": 0, "y": 0, "z": 3 }""",
            """{ "tag": 2, "x": 0, "y": 0, "z": 3, "fix": ["x", "y"], "springs": { "kz": 5000, "kuy": 120.5 } }""")
        .Replace("""{ "tag": "Р1", "nodes": [2, 3], "section": "Б-1", "meshStepM": 0.25, "torsion": { "gj": 1200 } }""",
            """{ "tag": "Р1", "nodes": [2, 3], "section": "Б-1", "meshStepM": 0.25, "torsion": { "gj": 1200 }, "rigidJunction": { "mode": "off", "kind": "coupled", "dofs": ["z", "uz"] } }""")
        .Replace("""["К1", "К2"] } ] } ] }""",
            """["К1", "К2"] } ], "lineSupports": [ { "member": "П1", "sides": [1, 2], "fix": ["x", "y", "z"], "fixEdgeRotation": true }, { "member": "П1", "hole": 1, "sides": [2], "springs": { "kz": 20000 } } ] } ] }""");

    [Fact]
    public async Task Apply_Boundary_SpringsRigidJunctionLineSupports_RoundTripSame()
    {
        Assert.Contains("lineSupports", BoundarySeed);
        Assert.Contains("rigidJunction", BoundarySeed);
        Assert.Contains("springs", BoundarySeed);
        string first = await NewProject("a.db");
        var r = await Run("apply", first, Doc("seed.json", BoundarySeed));
        Assert.True(r.Code == CliExitCode.Ok, r.Err);
        using (var db = new DatabaseService(first))
        {
            db.LoadAll();
            var s = db.FemSchemas.Single(x => x.Tag == "Рама");
            // rx ry rz документа 6а читаются как ux uy uz.
            Assert.Equal(FemBoundaryDofs.All, db.GetFemNodes(s.Id).Single(n => n.NodeTag == "1").DofMask);
            var spring = Assert.Single(db.GetFemSprings(s.Id), x => x.TargetKind == FemSpringTargetKinds.Node);
            Assert.Equal("2", spring.NodeTag);
            Assert.Equal(5e6, spring.Kz, 6);
            Assert.Equal(120.5e3, spring.Kuy, 6);
            var r1 = db.GetFemMembers(s.Id).Single(m => m.ElemTag == "Р1");
            Assert.Equal(FemMember.RigidJunctionOff, r1.RigidJunctionMode);
            Assert.Equal(FemMember.RigidJunctionCoupled, r1.RigidJunctionKind);
            Assert.Equal(0b100100, r1.RigidJunctionMask);
            Assert.Null(db.GetFemMembers(s.Id).Single(m => m.ElemTag == "К1").RigidJunctionMode);

            var lines = db.GetFemLineSupports(s.Id);
            Assert.Equal(3, lines.Count);
            // Сторона 1 внешнего контура — от (0,0) к (6,0); сторона 2 отверстия — от (3,1) к (3,2) в порядке документа
            // (обход отверстия при записи разворачивается).
            Assert.Contains(lines, l => l.Loop == CScore.Planar.BoundaryLoop.Outer && l.FixEdgeRotation && l.Mask == 0b111
                && Math.Abs(l.Y1) < 1e-9 && Math.Abs(l.Y2) < 1e-9);
            var hole = Assert.Single(lines, l => l.Loop == CScore.Planar.BoundaryLoop.Hole);
            Assert.Equal(2e7, hole.Kz, 6);
            Assert.Equal(3, hole.X1, 9);
            Assert.Equal(3, hole.X2, 9);
            Assert.Equal(3, Math.Round(hole.Y1 + hole.Y2, 9));
        }

        string e1 = PathOf("e1.json");
        Assert.True((await Run("export", "model", first, "--to", e1)).Code == CliExitCode.Ok);
        var exported = JsonNode.Parse(File.ReadAllText(e1))!["femSchemas"]![0]!;
        Assert.Equal("""["x","y","z","ux","uy","uz"]""", exported["nodes"]![0]!["fix"]!.ToJsonString());
        Assert.Equal(5000, (double)exported["nodes"]![1]!["springs"]!["kz"]!);
        Assert.Equal(2, exported["lineSupports"]!.AsArray().Count);
        Assert.Null(exported["members"]!.AsArray().Single(m => (string?)m!["tag"] == "К1")!["rigidJunction"]);

        string second = await NewProject("b.db");
        var apply = await Run("apply", second, e1);
        Assert.True(apply.Code == CliExitCode.Ok, apply.Err);
        string e2 = PathOf("e2.json");
        Assert.True((await Run("export", "model", second, "--to", e2)).Code == CliExitCode.Ok);
        Assert.Equal(exported.ToJsonString(), JsonNode.Parse(File.ReadAllText(e2))!["femSchemas"]![0]!.ToJsonString());

        // Без lineSupports связи не трогаются; пустой массив снимает все с предупреждением.
        var keep = await Run("apply", second, Doc("k.json", """
            { "format": "opencs-model", "version": 1, "femSchemas": [ { "tag": "Рама", "mesh": { "barStepM": 0.5 } } ] }
            """));
        Assert.True(keep.Code == CliExitCode.Ok, keep.Err);
        using (var db = new DatabaseService(second)) { db.LoadAll(); Assert.Equal(3, db.GetFemLineSupports(db.FemSchemas.Single().Id).Count); }
        var clear = await Run("apply", second, Doc("c.json", """
            { "format": "opencs-model", "version": 1, "femSchemas": [ { "tag": "Рама", "lineSupports": [] } ] }
            """));
        Assert.True(clear.Code == CliExitCode.Ok, clear.Err);
        Assert.Contains("линейных связей — 3", clear.Out + clear.Err);
        using (var db = new DatabaseService(second)) { db.LoadAll(); Assert.Empty(db.GetFemLineSupports(db.FemSchemas.Single().Id)); }
    }

    [Fact]
    public async Task Apply_BoundaryErrors_WithPaths_NothingWritten()
    {
        string project = await NewProject();
        var r = await Run("apply", project, Doc("bad.json", """
            { "format": "opencs-model", "version": 1,
              "femSchemas": [
                { "tag": "Ошибки",
                  "nodes": [ { "tag": 1, "x": 0, "y": 0, "z": 0, "fix": ["z"], "springs": { "kz": 10, "kx": -1, "kq": 1 } },
                             { "tag": 2, "x": 4, "y": 0, "z": 0 }, { "tag": 3, "x": 4, "y": 4, "z": 0 } ],
                  "members": [
                    { "tag": "Ф1", "type": "truss", "nodes": [1, 2], "rigidJunction": { "mode": "off" } },
                    { "tag": "С1", "nodes": [2, 3], "rigidJunction": { "dofs": [] } } ],
                  "lineSupports": [ { "member": "П1", "sides": [1] } ] } ] }
            """));
        Assert.Equal(CliExitCode.Usage, r.Code);
        Assert.Contains("femSchemas[0].nodes[0].springs.kz", r.Err);
        Assert.Contains("femSchemas[0].nodes[0].springs.kx", r.Err);
        Assert.Contains("femSchemas[0].nodes[0].springs.kq", r.Err);
        Assert.Contains("femSchemas[0].members[0].rigidJunction", r.Err);
        Assert.Contains("femSchemas[0].members[1].rigidJunction.dofs", r.Err);
        Assert.Contains("femSchemas[0].lineSupports[0]", r.Err);

        // Ссылки линейных связей — при сборке схемы (документ без ошибок разбора).
        var refs = await Run("apply", project, Doc("refs.json", """
            { "format": "opencs-model", "version": 1,
              "femSchemas": [
                { "tag": "Ссылки",
                  "nodes": [ { "tag": 2, "x": 4, "y": 0, "z": 0 }, { "tag": 3, "x": 4, "y": 4, "z": 0 } ],
                  "members": [
                    { "tag": "С1", "nodes": [2, 3] },
                    { "tag": "П1", "type": "shell", "region": { "outline": [[0, 0, 0], [4, 0, 0], [4, 4, 0], [0, 4, 0]] } } ],
                  "lineSupports": [
                    { "member": "С1", "sides": [1], "fix": ["z"] },
                    { "member": "П1", "sides": [9], "fix": ["z"] },
                    { "member": "П1", "sides": [1, 2], "fix": ["z"] },
                    { "member": "П1", "sides": [2], "fix": ["x"] },
                    { "member": "П1", "hole": 1, "sides": [1], "fix": ["z"] } ] } ] }
            """));
        Assert.Equal(CliExitCode.Usage, refs.Code);
        Assert.Contains("femSchemas[0].lineSupports[0].member", refs.Err);
        Assert.Contains("femSchemas[0].lineSupports[1].sides[0]", refs.Err);
        Assert.Contains("femSchemas[0].lineSupports[3].sides[0]", refs.Err);
        Assert.Contains("femSchemas[0].lineSupports[4].hole", refs.Err);
        using var db = new DatabaseService(project);
        db.LoadAll();
        Assert.Empty(db.FemSchemas);
    }

    [Fact]
    public async Task ListSchemas_ShowsSourceAndMeshState()
    {
        string project = await NewProject();
        Assert.Equal(CliExitCode.Ok, (await Run("apply", project, Doc("seed.json", """
            { "format": "opencs-model", "version": 1,
              "femSchemas": [ { "tag": "Балка",
                "nodes": [ { "tag": 1, "x": 0, "y": 0, "z": 0, "fix": ["x","y","z","rx","ry","rz"] }, { "tag": 2, "x": 3, "y": 0, "z": 0 } ],
                "members": [ { "tag": "Б1", "nodes": [1, 2] } ] } ] }
            """))).Code);
        var r = await Run("list", "schemas", project, "--json");
        Assert.True(r.Code == CliExitCode.Ok, r.Err);
        var row = JsonNode.Parse(r.Out)!.AsArray().Single()!;
        Assert.Equal("Балка", (string?)row["tag"]);
        Assert.Equal("internal", (string?)row["source"]);
        Assert.Equal(2, (int?)row["nodes"]);
        Assert.Equal("none", (string?)row["mesh"]);

        var m = await Run("run", "mesh", project, "--all", "--in-place");
        Assert.True(m.Code == CliExitCode.Ok, m.Err + m.Out);
        var again = JsonNode.Parse((await Run("list", "schemas", project, "--json")).Out)!.AsArray().Single()!;
        Assert.Equal("current", (string?)again["mesh"]);
    }
}
