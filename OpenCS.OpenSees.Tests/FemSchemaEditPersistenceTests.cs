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
            var member = new FemMember { Tag = "M1", ElemIdsJson = "[0]", GjStrategy = "manual", GjManualValue = 500 };

            db.SaveFemSchemaEdit(schema.Id, [node1, node2], [], [], [loadCase], []);
            Assert.Equal(2, db.GetFemNodes(schema.Id).Count);
            var savedNodes = db.GetFemNodes(schema.Id);
            var n1 = savedNodes.Single(n => n.NodeTag == "1");
            var n2 = savedNodes.Single(n => n.NodeTag == "2");

            var element = new FemElement
            {
                ElemTag = "1", ElemType = "beam",
                NodeIdsJson = System.Text.Json.JsonSerializer.Serialize(new[] { n1.Id, n2.Id })
            };
            var nodeLoad = new FemNodeLoad { LoadCaseId = db.GetFemLoadCases(schema.Id).Single().Id, NodeId = n1.Id, Fz = 12 };

            db.SaveFemSchemaEdit(schema.Id, [n1, n2], [element], [member],
                db.GetFemLoadCases(schema.Id), [nodeLoad]);

            Assert.Single(db.GetFemElements(schema.Id));
            Assert.Single(db.GetFemNodeLoads(schema.Id));
            Assert.Equal(12, db.GetFemNodeLoads(schema.Id).Single().Fz);

            db.LoadAll();
            var loadedMember = db.FemSchemas.Single(s => s.Id == schema.Id).Members.Single();
            Assert.Equal("manual", loadedMember.GjStrategy);
            Assert.Equal(500, loadedMember.GjManualValue);

            db.SaveFemSchemaEdit(schema.Id, [n1], [], [], [], []);
            Assert.Single(db.GetFemNodes(schema.Id));
            Assert.Empty(db.GetFemElements(schema.Id));
            Assert.Empty(db.GetFemNodeLoads(schema.Id));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SaveFemSchemaEdit_PersistsSameMemberAcrossRepeatedSaves()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fem-edit-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Edit", SourceType = "internal" };
            db.SaveFemSchema(schema);

            var node = new FemNode { NodeTag = "1", X = 0 };
            var member = new FemMember { Tag = "M1", ElemIdsJson = "[]" };

            // Первое сохранение назначает member.Id из БД (как SaveFemMemberCore при Id==0).
            db.SaveFemSchemaEdit(schema.Id, [node], [], [member], [], []);
            Assert.NotEqual(0, member.Id);
            db.LoadAll();
            Assert.Single(db.FemSchemas.Single(s => s.Id == schema.Id).Members);

            // Второе сохранение того же member (Id уже не 0) не должно потерять запись: fem_members
            // полностью пересоздаётся, значит SaveFemMemberCore обязан вставить строку заново,
            // а не выполнить UPDATE по уже удалённому id.
            db.SaveFemSchemaEdit(schema.Id, db.GetFemNodes(schema.Id), [], [member], [], []);

            db.LoadAll();
            var members = db.FemSchemas.Single(s => s.Id == schema.Id).Members;
            Assert.Single(members);
            Assert.Equal("M1", members.Single().Tag);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
