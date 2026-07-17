using CScore.Fem;
using OpenCS.Utilites;

namespace OpenCS.OpenSees.Tests;

public sealed class FemSchemaEditPersistenceTests
{
    [Fact]
    public void SaveFemSchemaEdit_ReplacesWholeSnapshotAtomically()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fem-edit-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Edit", SourceType = "internal" };
            db.SaveFemSchema(schema);

            var node1 = new FemNode { NodeTag = "1", X = 0 };
            var node2 = new FemNode { NodeTag = "2", X = 1 };
            var loadCase = new FemLoadCase { Tag = "G", Sp20Type = "permanent" };
            var group = new FemMemberGroup { Tag = "M1", MemberTagsJson = "[1]" };

            db.SaveFemSchemaEdit(schema.Id, [node1, node2], [], [], [loadCase], []);
            Assert.Equal(2, db.GetFemNodes(schema.Id).Count);
            var savedNodes = db.GetFemNodes(schema.Id);
            var n1 = savedNodes.Single(n => n.NodeTag == "1");
            var n2 = savedNodes.Single(n => n.NodeTag == "2");

            var member = new FemMember
            {
                ElemTag = "1", ElemType = "beam",
                NodeIdsJson = System.Text.Json.JsonSerializer.Serialize(new[] { n1.Id, n2.Id }),
                GjStrategy = "manual", GjManualValue = 500,
            };
            var nodeLoad = new FemNodeLoad { LoadCaseId = db.GetFemLoadCases(schema.Id).Single().Id, NodeId = n1.Id, Fz = 12 };

            db.SaveFemSchemaEdit(schema.Id, [n1, n2], [member], [group],
                db.GetFemLoadCases(schema.Id), [nodeLoad]);

            Assert.Single(db.GetFemMembers(schema.Id));
            Assert.Single(db.GetFemNodeLoads(schema.Id));
            Assert.Equal(12, db.GetFemNodeLoads(schema.Id).Single().Fz);

            db.LoadAll();
            var loadedMember = db.GetFemMembers(schema.Id).Single();
            Assert.Equal("manual", loadedMember.GjStrategy);
            Assert.Equal(500, loadedMember.GjManualValue);

            db.SaveFemSchemaEdit(schema.Id, [n1], [], [], [], []);
            Assert.Single(db.GetFemNodes(schema.Id));
            Assert.Empty(db.GetFemMembers(schema.Id));
            Assert.Empty(db.GetFemNodeLoads(schema.Id));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SaveFemSchemaEdit_PersistsSameMemberGroupAcrossRepeatedSaves()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fem-edit-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Edit", SourceType = "internal" };
            db.SaveFemSchema(schema);

            var node = new FemNode { NodeTag = "1", X = 0 };
            var group = new FemMemberGroup { Tag = "M1", MemberTagsJson = "[]" };

            // Первое сохранение назначает group.Id из БД (как SaveFemMemberGroupCore при Id==0).
            db.SaveFemSchemaEdit(schema.Id, [node], [], [group], [], []);
            Assert.NotEqual(0, group.Id);
            db.LoadAll();
            Assert.Single(db.FemSchemas.Single(s => s.Id == schema.Id).MemberGroups);

            // Второе сохранение той же группы (Id уже не 0) не должно потерять запись: fem_member_groups
            // полностью пересоздаётся, значит SaveFemMemberGroupCore обязан вставить строку заново,
            // а не выполнить UPDATE по уже удалённому id.
            db.SaveFemSchemaEdit(schema.Id, db.GetFemNodes(schema.Id), [], [group], [], []);

            db.LoadAll();
            var groups = db.FemSchemas.Single(s => s.Id == schema.Id).MemberGroups;
            Assert.Single(groups);
            Assert.Equal("M1", groups.Single().Tag);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
