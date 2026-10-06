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
