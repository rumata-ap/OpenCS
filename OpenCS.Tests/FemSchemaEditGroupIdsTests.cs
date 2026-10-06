using CScore.Fem;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Сохранение редактора схемы не должно менять Id групп: на них ссылаются проверки
/// (<c>fem_checks.member_id</c>), наборы усилий и параметры проверок.</summary>
public sealed class FemSchemaEditGroupIdsTests
{
    [Fact]
    public void EditorSave_KeepsGroupIds_SoChecksStayAttached()
    {
        string path = Path.Combine(Path.GetTempPath(), "opencs_group_ids_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            int schemaId, groupId;
            using (var db = new DatabaseService(path))
            {
                var schema = new FemSchema { Tag = "Схема" };
                db.SaveFemSchema(schema);
                schemaId = schema.Id;
                db.SaveFemMember(new FemMember { SchemaId = schemaId, ElemTag = "Б1", ElemType = "beam" });
                var kept = FemGroupComposition.NewMembersGroup(schemaId, ["Б1"], "Балки", null);
                db.SaveFemMemberGroup(kept);
                groupId = kept.Id;
                db.SaveFemCheck(new FemCheck { SchemaId = schemaId, MemberId = groupId, Tag = "Проверка" });

                // Сессия редактора: та же группа и новая, созданная в сеансе.
                var added = FemGroupComposition.NewMembersGroup(schemaId, ["Б1"], "Новая", null);
                db.SaveFemSchemaEdit(schemaId, db.GetFemNodes(schemaId), db.GetFemMembers(schemaId), [kept, added], [], [], []);

                Assert.Equal(groupId, kept.Id);
                Assert.True(added.Id > 0);
                Assert.NotEqual(groupId, added.Id);
            }

            using (var db = new DatabaseService(path))
            {
                db.LoadAll();
                var groups = db.FemSchemas.Single(s => s.Id == schemaId).MemberGroups;
                Assert.Equal(["Балки", "Новая"], groups.Select(g => g.Tag).OrderBy(t => t));
                var check = db.FemChecks.Single(c => c.SchemaId == schemaId);
                Assert.Equal(groupId, check.MemberId);
                Assert.Contains(groups, g => g.Id == check.MemberId);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public void EditorSave_DropsGroupsRemovedInSession()
    {
        string path = Path.Combine(Path.GetTempPath(), "opencs_group_ids_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Схема" };
            db.SaveFemSchema(schema);
            db.SaveFemMember(new FemMember { SchemaId = schema.Id, ElemTag = "Б1", ElemType = "beam" });
            var a = FemGroupComposition.NewMembersGroup(schema.Id, ["Б1"], "А", null);
            var b = FemGroupComposition.NewMembersGroup(schema.Id, ["Б1"], "Б", null);
            db.SaveFemMemberGroup(a);
            db.SaveFemMemberGroup(b);

            db.SaveFemSchemaEdit(schema.Id, [], db.GetFemMembers(schema.Id), [b], [], [], []);

            db.LoadAll();
            Assert.Equal(["Б"], db.FemSchemas.Single(s => s.Id == schema.Id).MemberGroups.Select(g => g.Tag));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
