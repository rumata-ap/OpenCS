using System.Text.Json;
using CScore.Fem;
using OpenCS.Utilites;

namespace OpenCS.OpenSees.Tests;

public sealed class FemMeshPersistenceTests
{
    [Fact]
    public void SaveFemMeshSnapshot_RoundTripsDiscretizedMeshAndMetadata()
    {
        string path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var schema = SaveSchema(db);
            var nodes = new List<FemNode>
            {
                new FemNode { NodeTag = "1", X = 0 },
                new FemNode { NodeTag = "2", X = 6 },
            };
            var member = new FemMember
            {
                SchemaId = schema.Id,
                ElemTag = "17",
                NodeIdsJson = "[1,2]",
                CrossSectionId = 23,
                GjStrategy = "manual",
                GjManualValue = 456.5,
                GjTorsionTaskId = 78,
            };

            db.SaveFemSchemaEdit(schema.Id, nodes, [member], [], [], []);
            var mesh = FemMeshDiscretizer.Discretize(schema.Id, nodes, [member], 3);

            db.SaveFemMeshSnapshot(schema.Id, mesh.Nodes, mesh.Elements);

            var loadedNodes = db.GetFemMeshNodes(schema.Id);
            var loadedElements = db.GetFemMeshElements(schema.Id);
            Assert.Equal(3, loadedNodes.Count);
            Assert.Equal(2, loadedElements.Count);
            Assert.All(loadedNodes, node =>
            {
                Assert.Equal(schema.Id, node.SchemaId);
                Assert.NotEqual(0, node.Id);
            });
            Assert.All(loadedElements, element =>
            {
                Assert.Equal(schema.Id, element.SchemaId);
                Assert.NotEqual(0, element.Id);
                Assert.Equal("17", element.SourceMemberTag);
                Assert.Equal(23, element.CrossSectionId);
                Assert.Equal("manual", element.GjStrategy);
                Assert.Equal(456.5, element.GjManualValue);
                Assert.Equal(78, element.GjTorsionTaskId);
            });
            Assert.Equal("1", loadedNodes.Single(node => node.SourceNodeTag == "1").NodeTag);
            Assert.Equal("2", loadedNodes.Single(node => node.SourceNodeTag == "2").NodeTag);
            Assert.All(loadedElements, element =>
                Assert.All(JsonSerializer.Deserialize<int[]>(element.NodeIdsJson)!,
                    nodeId => Assert.Contains(nodeId, loadedNodes.Select(node => int.Parse(node.NodeTag)))));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public void SaveFemMeshSnapshot_ReplacesOnlyTheSchemaSnapshot()
    {
        string path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var schema = SaveSchema(db);
            var nodes = new List<FemNode>
            {
                new FemNode { NodeTag = "1", X = 0 },
                new FemNode { NodeTag = "2", X = 6 },
            };
            var member = new FemMember { SchemaId = schema.Id, ElemTag = "17", NodeIdsJson = "[1,2]" };
            db.SaveFemSchemaEdit(schema.Id, nodes, [member], [], [], []);

            var first = FemMeshDiscretizer.Discretize(schema.Id, nodes, [member], 3);
            db.SaveFemMeshSnapshot(schema.Id, first.Nodes, first.Elements);
            var replacement = FemMeshDiscretizer.Discretize(schema.Id, nodes, [member], 6);

            db.SaveFemMeshSnapshot(schema.Id, replacement.Nodes, replacement.Elements);

            var loadedNodes = db.GetFemMeshNodes(schema.Id);
            Assert.Collection(loadedNodes, _ => { }, _ => { });
            Assert.Single(db.GetFemMeshElements(schema.Id));
            Assert.NotEqual(loadedNodes[0].Id, loadedNodes[1].Id);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public void SaveFemMeshSnapshot_PreservesConstructiveLayer()
    {
        string path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var schema = SaveSchema(db);
            var node1 = new FemNode { NodeTag = "1", X = 0 };
            var node2 = new FemNode { NodeTag = "2", X = 6 };
            var member = new FemMember { SchemaId = schema.Id, ElemTag = "17", NodeIdsJson = "[1,2]" };
            var group = new FemMemberGroup { Tag = "group", MemberTagsJson = "[17]" };
            db.SaveFemSchemaEdit(schema.Id, [node1, node2], [member], [group], [], []);

            var mesh = FemMeshDiscretizer.Discretize(schema.Id, new[] { node1, node2 }, new[] { member }, 3);
            db.SaveFemMeshSnapshot(schema.Id, mesh.Nodes, mesh.Elements);

            var loadedMember = db.GetFemMembers(schema.Id).Single();
            Assert.Equal("17", loadedMember.ElemTag);
            Assert.Equal("[1,2]", loadedMember.NodeIdsJson);
            db.LoadAll();
            Assert.Single(db.FemSchemas.Single(item => item.Id == schema.Id).MemberGroups);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public void FemMember_TargetMeshLengthMRoundTripsThroughSchemaEdit()
    {
        string path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var schema = SaveSchema(db);
            var node1 = new FemNode { NodeTag = "1", X = 0 };
            var node2 = new FemNode { NodeTag = "2", X = 6 };
            var member = new FemMember
            {
                SchemaId = schema.Id,
                ElemTag = "17",
                NodeIdsJson = "[1,2]",
                TargetMeshLengthM = 0.25,
            };

            db.SaveFemSchemaEdit(schema.Id, [node1, node2], [member], [], [], []);
            db.LoadAll();

            Assert.Equal(0.25, db.GetFemMembers(schema.Id).Single().TargetMeshLengthM);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    static FemSchema SaveSchema(DatabaseService db)
    {
        var schema = new FemSchema { Tag = "Mesh", SourceType = "internal" };
        db.SaveFemSchema(schema);
        return schema;
    }

    static string TempDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"opencs-fem-mesh-{Guid.NewGuid():N}.db");

    static void DeleteDatabase(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
