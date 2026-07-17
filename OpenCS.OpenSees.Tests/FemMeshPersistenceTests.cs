using System.Text.Json;
using CScore.Fem;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;

namespace OpenCS.OpenSees.Tests;

public sealed class FemMeshPersistenceTests
{
    [Fact]
    public void DatabaseService_MigratesV30LegacyFemElementsBeforeMeshSnapshot()
    {
        string path = TempDatabasePath();
        try
        {
            CreateV30Database(path);

            using var db = new DatabaseService(path);
            var members = db.GetFemMembers(1);
            Assert.Equal(2, members.Count);
            Assert.Contains(members, member => member.ElemTag == "current");
            var migratedLegacy = Assert.Single(members, member => member.ElemTag == "legacy");
            Assert.Equal("legacy-section", migratedLegacy.SectionTag);
            Assert.Equal("legacy-material", migratedLegacy.MaterialTag);
            Assert.Equal(0.4, migratedLegacy.ThicknessM);

            db.SaveFemSchemaEdit(1, [], [migratedLegacy], [], [], []);
            var reloadedLegacy = Assert.Single(db.GetFemMembers(1));
            Assert.Equal("legacy-material", reloadedLegacy.MaterialTag);

            var nodes = new[]
            {
                new FemMeshNode { SchemaId = 1, NodeTag = "1", X = 0, Y = 1, Z = 2, SourceNodeTag = "A", SourceMemberTag = "legacy" },
                new FemMeshNode { SchemaId = 1, NodeTag = "2", X = 3, Y = 4, Z = 5 },
            };
            var elements = new[]
            {
                new FemElement { SchemaId = 1, ElemTag = "mesh-1", NodeIdsJson = "[1,2]", SourceMemberTag = null },
            };

            db.SaveFemMeshSnapshot(1, nodes, elements);

            var loadedNodes = db.GetFemMeshNodes(1);
            var loadedElements = db.GetFemMeshElements(1);
            Assert.Equal(2, loadedNodes.Count);
            var loadedElement = Assert.Single(loadedElements);
            Assert.Equal("mesh-1", loadedElement.ElemTag);
            Assert.Equal("[1,2]", loadedElement.NodeIdsJson);
            Assert.Null(loadedElement.SourceMemberTag);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

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
    public void SaveFemMeshSnapshot_ReplacesOnlyTheSchemaSnapshotAndIsolatesSchemas()
    {
        string path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var schema = SaveSchema(db);
            var otherSchema = SaveSchema(db);
            var originalNodes = new[]
            {
                new FemMeshNode { NodeTag = "a1", X = 1, Y = 2, Z = 3, SourceNodeTag = "source-a1", SourceMemberTag = "member-a" },
                new FemMeshNode { NodeTag = "a2", X = 4, Y = 5, Z = 6 },
            };
            var originalElements = new[]
            {
                new FemElement
                {
                    ElemTag = "old",
                    NodeIdsJson = "[11,12]",
                    SourceMemberTag = "member-a",
                    CrossSectionId = 5,
                    GjStrategy = "saint_venant",
                    GjTorsionTaskId = 9,
                },
            };
            var otherNodes = new[]
            {
                new FemMeshNode { NodeTag = "b1", X = -1, Y = -2, Z = -3, SourceNodeTag = "source-b1", SourceMemberTag = "member-b" },
            };
            var otherElements = new[]
            {
                new FemElement { ElemTag = "other", NodeIdsJson = "[21]", GjStrategy = "manual" },
            };
            db.SaveFemMeshSnapshot(schema.Id, originalNodes, originalElements);
            db.SaveFemMeshSnapshot(otherSchema.Id, otherNodes, otherElements);

            var replacementNodes = new[]
            {
                new FemMeshNode { NodeTag = "r1", X = 10, Y = 20, Z = 30, SourceNodeTag = "source-r1", SourceMemberTag = "member-r" },
                new FemMeshNode { NodeTag = "r2", X = 40, Y = 50, Z = 60, SourceNodeTag = null, SourceMemberTag = null },
            };
            var replacementElements = new[]
            {
                new FemElement
                {
                    ElemTag = "new",
                    NodeIdsJson = "[31,32,33]",
                    SourceMemberTag = null,
                    CrossSectionId = null,
                    GjStrategy = "manual",
                    GjManualValue = null,
                    GjTorsionTaskId = null,
                },
            };

            db.SaveFemMeshSnapshot(schema.Id, replacementNodes, replacementElements);

            var loadedNodes = db.GetFemMeshNodes(schema.Id);
            Assert.Collection(loadedNodes,
                node =>
                {
                    Assert.Equal("r1", node.NodeTag);
                    Assert.Equal(10, node.X);
                    Assert.Equal(20, node.Y);
                    Assert.Equal(30, node.Z);
                    Assert.Equal("source-r1", node.SourceNodeTag);
                    Assert.Equal("member-r", node.SourceMemberTag);
                },
                node =>
                {
                    Assert.Equal("r2", node.NodeTag);
                    Assert.Equal(40, node.X);
                    Assert.Equal(50, node.Y);
                    Assert.Equal(60, node.Z);
                    Assert.Null(node.SourceNodeTag);
                    Assert.Null(node.SourceMemberTag);
                });
            var loadedElement = Assert.Single(db.GetFemMeshElements(schema.Id));
            Assert.Equal("new", loadedElement.ElemTag);
            Assert.Equal("[31,32,33]", loadedElement.NodeIdsJson);
            Assert.Null(loadedElement.SourceMemberTag);
            Assert.Null(loadedElement.CrossSectionId);
            Assert.Equal("manual", loadedElement.GjStrategy);
            Assert.Null(loadedElement.GjManualValue);
            Assert.Null(loadedElement.GjTorsionTaskId);

            var untouchedOtherNode = Assert.Single(db.GetFemMeshNodes(otherSchema.Id));
            Assert.Equal("b1", untouchedOtherNode.NodeTag);
            var untouchedOtherElement = Assert.Single(db.GetFemMeshElements(otherSchema.Id));
            Assert.Equal("other", untouchedOtherElement.ElemTag);
            Assert.Equal("[21]", untouchedOtherElement.NodeIdsJson);
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

    static void CreateV30Database(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE settings (key TEXT PRIMARY KEY, value_json TEXT NOT NULL);
            INSERT INTO settings (key, value_json) VALUES ('schema_version', '30');
            CREATE TABLE fem_schemas (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                tag TEXT NOT NULL DEFAULT '',
                source_type TEXT NOT NULL DEFAULT 'internal',
                created TEXT NOT NULL DEFAULT ''
            );
            INSERT INTO fem_schemas (id, tag, source_type) VALUES (1, 'legacy-v30', 'internal');
            CREATE TABLE fem_nodes (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                schema_id INTEGER NOT NULL,
                node_tag TEXT NOT NULL DEFAULT '',
                x REAL NOT NULL DEFAULT 0,
                y REAL NOT NULL DEFAULT 0,
                z REAL NOT NULL DEFAULT 0,
                dof_mask INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE fem_members (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                schema_id INTEGER NOT NULL,
                elem_tag TEXT NOT NULL DEFAULT '',
                elem_type TEXT NOT NULL DEFAULT 'beam',
                node_ids_json TEXT NOT NULL DEFAULT '[]',
                section_tag TEXT,
                material_tag TEXT,
                thickness_m REAL,
                cross_section_id INTEGER,
                gj_strategy TEXT NOT NULL DEFAULT 'manual',
                gj_manual_value REAL,
                gj_torsion_task_id INTEGER
            );
            INSERT INTO fem_members (schema_id, elem_tag, elem_type, node_ids_json, section_tag, thickness_m)
            VALUES (1, 'current', 'beam', '[1,2]', 'current-section', 0.3);
            CREATE TABLE fem_elements (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                schema_id INTEGER NOT NULL,
                elem_tag TEXT NOT NULL DEFAULT '',
                elem_type TEXT NOT NULL DEFAULT 'beam',
                node_ids_json TEXT NOT NULL DEFAULT '[]',
                section_tag TEXT,
                material_tag TEXT,
                thickness_m REAL
            );
            INSERT INTO fem_elements (schema_id, elem_tag, elem_type, node_ids_json, section_tag, material_tag, thickness_m)
            VALUES (1, 'current', 'beam', '[1,2]', 'duplicate-section', 'duplicate-material', 0.1);
            INSERT INTO fem_elements (schema_id, elem_tag, elem_type, node_ids_json, section_tag, material_tag, thickness_m)
            VALUES (1, 'legacy', 'beam', '[1,2]', 'legacy-section', 'legacy-material', 0.4);
            """;
        command.ExecuteNonQuery();
    }

    static string TempDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"opencs-fem-mesh-{Guid.NewGuid():N}.db");

    static void DeleteDatabase(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
