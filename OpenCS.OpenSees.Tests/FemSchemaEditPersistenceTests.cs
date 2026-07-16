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
}
