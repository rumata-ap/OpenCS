using System.Text.Json;
using CScore.Fem;
using CScore.Import;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>кБ ЛИРЫ → конструктивные элементы: запись в БД, повторное преобразование, замок сетки.</summary>
public class LiraBlocksPersistenceTests
{
    static FemMeshNode N(string tag, double x, double y, double z) =>
        new() { NodeTag = tag, X = x, Y = y, Z = z, Origin = FemMember.MeshSourceImported };

    /// <summary>КЭ по узлам в порядке обхода; четырёхузловой хранится, как в ЛИРЕ, «1 2 4 3».</summary>
    static FemElement E(string tag, string type, params int[] nodes) =>
        new() { ElemTag = tag, ElemType = type,
                NodeIdsJson = JsonSerializer.Serialize(nodes.Length == 4 ? new[] { nodes[0], nodes[1], nodes[3], nodes[2] } : nodes), SectionTag = type == "beam" ? "К 40" : "Пл 200",
                ThicknessM = type == "shell" ? 0.2 : null, Origin = FemMember.MeshSourceImported };

    /// <summary>Колонна 1–2–3 (КЭ 1, 2) и плита 3-4-5-6 (КЭ 3) — схема ЛИРЫ с двумя кБ.</summary>
    static (DatabaseService Db, FemSchema Schema) CreateSchema()
    {
        string path = Path.Combine(Path.GetTempPath(), "opencs_lira_blocks_" + Guid.NewGuid().ToString("N") + ".db");
        var db = new DatabaseService(path);
        var schema = new FemSchema { Tag = "Схема", SourceType = "lira" };
        db.SaveFemSchema(schema);
        db.SaveFemMeshSnapshot(schema.Id,
            [N("1", 0, 0, 0), N("2", 0, 0, 1.5), N("3", 0, 0, 3), N("4", 2, 0, 3), N("5", 2, 2, 3), N("6", 0, 2, 3)],
            [E("1", "beam", 1, 2), E("2", "beam", 2, 3), E("3", "shell", 3, 4, 5, 6)]);
        db.SaveFemSchemaConstructiveBlocks(schema.Id,
        [
            new LiraConstructiveBlockRecord(1, "КОЛОННА", "1 этаж", "К-1", "", [1, 2]),
            new LiraConstructiveBlockRecord(2, "ПЛИТА", "1 этаж", "", "", [3]),
        ]);
        return (db, schema);
    }

    static void Convert(DatabaseService db, int schemaId, IReadOnlyList<LiraBlockInfo> blocks)
    {
        var nodes = db.GetFemMeshNodes(schemaId);
        var elements = db.GetFemMeshElements(schemaId);
        db.ApplyLiraBlockMembers(schemaId, blocks, blocks.Select(b => LiraBlockMemberBuilder.Build(b, nodes, elements)).ToList());
    }

    [Fact]
    public void Convert_SavesLockedMembersRegionAndLinksMesh()
    {
        var (db, schema) = CreateSchema();
        using var _ = db;
        var blocks = db.GetLiraBlocks(schema.Id);
        Assert.Equal(["КОЛОННА №1 [1 этаж] К-1", "ПЛИТА №2 [1 этаж]"], blocks.Select(b => b.Tag));

        Convert(db, schema.Id, blocks);

        var members = db.GetFemMembers(schema.Id);
        Assert.Equal(2, members.Count);
        Assert.All(members, m => Assert.True(m.IsMeshLocked));
        var plate = members.Single(m => m.ElemType == "shell");
        Assert.Equal("plate", plate.Kind);
        Assert.NotNull(db.GetPlanarRegions(schema.Id).SingleOrDefault(r => r.Id == plate.PlanarRegionId));
        Assert.Equal(["1", "3"], db.GetFemNodes(schema.Id).Select(n => n.NodeTag).Order());

        var mesh = db.GetFemMeshElements(schema.Id).ToDictionary(e => e.ElemTag);
        Assert.Equal("КОЛОННА №1 [1 этаж] К-1", mesh["1"].SourceMemberTag);
        Assert.Equal("ПЛИТА №2 [1 этаж]", mesh["3"].SourceMemberTag);
        Assert.All(mesh.Values, e => Assert.Equal(FemMember.MeshSourceImported, e.Origin));
    }

    [Fact]
    public void ConvertAgain_ReplacesPreviousMembers()
    {
        var (db, schema) = CreateSchema();
        using var _ = db;
        var blocks = db.GetLiraBlocks(schema.Id);
        Convert(db, schema.Id, blocks);
        Convert(db, schema.Id, blocks);

        Assert.Equal(2, db.GetFemMembers(schema.Id).Count);
        Assert.Single(db.GetPlanarRegions(schema.Id));
        Assert.Equal(2, db.GetFemNodes(schema.Id).Count);
    }

    [Fact]
    public void EditorSave_KeepsRegionAndLock_AndUnlinksDeletedMember()
    {
        var (db, schema) = CreateSchema();
        using var _ = db;
        Convert(db, schema.Id, db.GetLiraBlocks(schema.Id));
        var members = db.GetFemMembers(schema.Id);
        var kept = members.Where(m => m.ElemType == "shell").ToList();

        db.SaveFemSchemaEdit(schema.Id, db.GetFemNodes(schema.Id), kept, [], [], [], []);

        var reloaded = Assert.Single(db.GetFemMembers(schema.Id));
        Assert.True(reloaded.IsMeshLocked);
        Assert.Equal(kept[0].PlanarRegionId, reloaded.PlanarRegionId);
        Assert.Equal("plate", reloaded.Kind);
        var mesh = db.GetFemMeshElements(schema.Id).ToDictionary(e => e.ElemTag);
        Assert.Null(mesh["1"].SourceMemberTag); // колонну удалили — её КЭ отвязаны, но остались
        Assert.Equal(reloaded.ElemTag, mesh["3"].SourceMemberTag);
    }

    [Fact]
    public void DiscretizeKeepingImported_PreservesImportedRowIds()
    {
        var (db, schema) = CreateSchema();
        using var _ = db;
        Convert(db, schema.Id, db.GetLiraBlocks(schema.Id));
        var idsBefore = db.GetFemMeshElements(schema.Id).Select(e => e.Id).ToList();
        var nodes = db.GetFemNodes(schema.Id);
        nodes.Add(new FemNode { NodeTag = "100", X = 0, Y = 0, Z = 6 });
        var members = db.GetFemMembers(schema.Id);
        members.Add(new FemMember { ElemTag = "Надстройка", NodeIdsJson = "[3,100]" });

        var mesh = FemMeshDiscretizer.DiscretizeKeepingImported(schema.Id, nodes, members, 1.0,
            db.GetFemMeshNodes(schema.Id), db.GetFemMeshElements(schema.Id));
        db.SaveFemMeshSnapshotKeepingImported(schema.Id, mesh.Nodes, mesh.Elements);

        var after = db.GetFemMeshElements(schema.Id);
        Assert.Equal(idsBefore, after.Where(e => e.Origin == FemMember.MeshSourceImported).Select(e => e.Id));
        Assert.Equal(3, after.Count(e => e.SourceMemberTag == "Надстройка"));
        Assert.True(db.HasImportedMesh(schema.Id));
    }

    [Fact]
    public void GetLiraBlocks_FallsBackToBlockGroups()
    {
        string path = Path.Combine(Path.GetTempPath(), "opencs_lira_blocks_fb_" + Guid.NewGuid().ToString("N") + ".db");
        using var db = new DatabaseService(path);
        var schema = new FemSchema { Tag = "Старая схема", SourceType = "lira" };
        db.SaveFemSchema(schema);
        db.SaveFemMemberGroups(schema.Id,
        [
            new FemMemberGroup { SchemaId = schema.Id, Tag = "СТЕНА №5 [1-й этаж] С-1", MemberTagsJson = "[11,12]" },
            new FemMemberGroup { SchemaId = schema.Id, Tag = "Жёсткость 1", MemberType = "beam", MemberTagsJson = "[1]" },
        ]);

        var block = Assert.Single(db.GetLiraBlocks(schema.Id));
        Assert.Equal((5, "СТЕНА", "1-й этаж", "С-1"), (block.Id, block.Type, block.Floor, block.Mark));
        Assert.Equal(["11", "12"], block.ElementTags);
    }
}
