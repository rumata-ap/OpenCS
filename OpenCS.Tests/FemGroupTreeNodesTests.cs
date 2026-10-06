using CScore.Fem;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Узлы дерева «Группы КЭ» / «Группы КонЭ»: разбор групп по виду, видимость, удаление.</summary>
public sealed class FemGroupTreeNodesTests
{
    static FemMeshNode N(string tag, double x) => new() { NodeTag = tag, X = x, Origin = FemMember.MeshSourceImported };

    static FemElement E(string tag) =>
        new() { ElemTag = tag, ElemType = "beam", NodeIdsJson = "[1,2]", Origin = FemMember.MeshSourceImported };

    [Fact]
    public void GroupsAreSplitByKind_AndFollowCreationAndDeletion()
    {
        string path = TempDbPath();
        try
        {
            using var db = new DatabaseService(path);
            var service = new FemGroupService(db, new NullLog());
            var schema = new FemSchema { Tag = "ЛИРА", SourceType = "lira" };
            db.SaveFemSchema(schema);
            db.SaveFemMember(new FemMember { SchemaId = schema.Id, ElemTag = "Колонна · 1" });
            db.SaveFemMeshSnapshot(schema.Id, [N("1", 0), N("2", 1)], [E("1"), E("2")]);
            var mesh = service.CreateMeshGroup(schema, ["1"], "Жёсткость 1", null)!;

            var tree = new FemSchemaTreeVM(schema, db, []);
            Assert.Equal([mesh], tree.MeshGroupsSubNode.Groups);
            Assert.Empty(tree.MemberGroupsSubNode.Groups);

            var members = service.CreateMembersGroup(schema, ["Колонна · 1"], "Колонны", null)!;
            var empty = service.CreateEmptyGroup(schema, "Пустая", FemMemberGroup.KindMembers);
            Assert.Equal([mesh], tree.MeshGroupsSubNode.Groups);
            Assert.Equal([members, empty], tree.MemberGroupsSubNode.Groups);
            Assert.Equal(2, tree.MemberGroupsSubNode.Count);

            Assert.True(service.Rename(members, "  Колонны 1 этажа "));
            Assert.Equal("Колонны 1 этажа", members.Tag);
            Assert.False(service.Rename(members, "   "));

            db.DeleteFemMemberGroup(members);
            Assert.Equal([empty], tree.MemberGroupsSubNode.Groups);

            using var reloaded = new DatabaseService(path);
            reloaded.LoadAll();
            var saved = reloaded.FemSchemas.Single(s => s.Id == schema.Id).MemberGroups;
            Assert.Equal(["Жёсткость 1", "Пустая"], saved.Select(g => g.Tag));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void EmptyNodesAreShownOnlyWhenSchemaCanHaveSuchGroups()
    {
        string path = TempDbPath();
        try
        {
            using var db = new DatabaseService(path);
            var imported = new FemSchema { Tag = "ЛИРА", SourceType = "lira" };
            var editor = new FemSchema { Tag = "Редактор" };
            db.SaveFemSchema(imported);
            db.SaveFemSchema(editor);
            db.SaveFemMeshSnapshot(imported.Id, [N("1", 0), N("2", 1)], [E("1")]);

            // Чистый импорт сетки: групп КонЭ быть не может, пока не построены КонЭ.
            var importedTree = new FemSchemaTreeVM(imported, db, []);
            Assert.True(importedTree.MeshGroupsSubNode.IsVisible);
            Assert.False(importedTree.MemberGroupsSubNode.IsVisible);

            db.SaveFemMember(new FemMember { SchemaId = imported.Id, ElemTag = "кБ 1" });
            importedTree.ReloadTopology();
            Assert.True(importedTree.MemberGroupsSubNode.IsVisible);

            // Схема редактора без импортированной сетки: групп КЭ нет и быть не может.
            var editorTree = new FemSchemaTreeVM(editor, db, []);
            Assert.False(editorTree.MeshGroupsSubNode.IsVisible);
            Assert.True(editorTree.MemberGroupsSubNode.IsVisible);

            // Группа, оставшаяся от прежних данных, видна всегда.
            editor.MemberGroups.Add(new FemMemberGroup { SchemaId = editor.Id, Tag = "Старая", Kind = FemMemberGroup.KindMesh });
            Assert.True(editorTree.MeshGroupsSubNode.IsVisible);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void DeletingGroup_RemovesItsChecks_KeepsOthers()
    {
        string path = TempDbPath();
        try
        {
            using var db = new DatabaseService(path);
            var service = new FemGroupService(db, new NullLog());
            var schema = new FemSchema { Tag = "Схема" };
            db.SaveFemSchema(schema);
            db.SaveFemMember(new FemMember { SchemaId = schema.Id, ElemTag = "Б1" });
            var doomed = service.CreateMembersGroup(schema, ["Б1"], "Удаляемая", null)!;
            var kept = service.CreateMembersGroup(schema, ["Б1"], "Остаётся", null)!;
            db.SaveFemCheck(new FemCheck { SchemaId = schema.Id, MemberId = doomed.Id, Tag = "П1" });
            db.SaveFemCheck(new FemCheck { SchemaId = schema.Id, MemberId = kept.Id, Tag = "П2" });

            db.DeleteFemMemberGroup(doomed);

            Assert.DoesNotContain(db.FemChecks, c => c.MemberId == doomed.Id);
            Assert.Equal(0L, Scalar(path, $"SELECT COUNT(*) FROM fem_checks WHERE member_id={doomed.Id}"));
            Assert.Equal(1L, Scalar(path, $"SELECT COUNT(*) FROM fem_checks WHERE member_id={kept.Id}"));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void MembersAreListedUnderBarsAndShells_WithTheirOwnChecks()
    {
        string path = TempDbPath();
        try
        {
            using var db = new DatabaseService(path);
            var service = new FemGroupService(db, new NullLog());
            var schema = new FemSchema { Tag = "Схема" };
            db.SaveFemSchema(schema);
            db.SaveFemTopology(schema.Id,
                [new FemNode { SchemaId = schema.Id, NodeTag = "1", DofMask = 63 },
                 new FemNode { SchemaId = schema.Id, NodeTag = "2", X = 3, DofMask = 5 }], [], []);
            var beam = new FemMember { SchemaId = schema.Id, ElemTag = "Б1", NodeIdsJson = "[1,2]" };
            var plate = new FemMember { SchemaId = schema.Id, ElemTag = "П1", ElemType = "shell", Kind = FemMemberTypes.Plate };
            db.SaveFemMember(beam);
            db.SaveFemMember(plate);
            var group = service.CreateMembersGroup(schema, ["Б1"], "Балки", null)!;

            var tree = new FemSchemaTreeVM(schema, db, []);
            var bars = tree.ElementsSubNode.Bars.Members;
            var shells = tree.ElementsSubNode.Shells.Members;
            Assert.Equal(["Б1"], bars.Select(i => i.Member.ElemTag));
            Assert.Equal(["П1"], shells.Select(i => i.Member.ElemTag));
            Assert.Null(bars[0].TypeCode);
            Assert.Equal(FemMemberTypes.Plate, shells[0].TypeCode);

            // Узлы: подпись закреплений и примыкающие КонЭ по тегам узлов.
            var nodes = tree.NodesSubNode.Nodes;
            Assert.Equal(["1", "2"], nodes.Select(n => n.Node.NodeTag));
            Assert.Equal("Tx Tz", nodes[1].Supports);
            Assert.Equal(["Б1"], nodes[0].AdjacentMembers().Select(i => i.Member.ElemTag));

            // В узел КонЭ попадают только проверки, нацеленные на него; групповая — нет.
            var own = new FemCheck { SchemaId = schema.Id, ElementId = beam.Id, Tag = "Своя" };
            db.SaveFemCheck(own);
            db.SaveFemCheck(new FemCheck { SchemaId = schema.Id, MemberId = group.Id, Tag = "Группы" });
            Assert.Equal([own], bars[0].Checks);
            Assert.Empty(shells[0].Checks);

            // Перечитывание топологии сохраняет проверки у новых узлов КонЭ.
            db.SaveFemMember(new FemMember { SchemaId = schema.Id, ElemTag = "Б2" });
            tree.ReloadTopology();
            bars = tree.ElementsSubNode.Bars.Members;
            Assert.Equal(["Б1", "Б2"], bars.Select(i => i.Member.ElemTag).Order());
            Assert.Equal([own], bars.Single(i => i.Member.Id == beam.Id).Checks);

            db.DeleteFemCheck(own);
            Assert.Empty(bars.Single(i => i.Member.Id == beam.Id).Checks);
        }
        finally
        {
            Delete(path);
        }
    }

    sealed class NullLog : ILogService
    {
        public System.Collections.ObjectModel.ObservableCollection<LogEntry> LogEntries { get; } = [];
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }

    static object? Scalar(string path, string sql)
    {
        using var c = new SqliteConnection($"Data Source={path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    static string TempDbPath()
        => Path.Combine(Path.GetTempPath(), $"opencs-group-tree-{Guid.NewGuid():N}.db");

    static void Delete(string path)
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
    }
}
