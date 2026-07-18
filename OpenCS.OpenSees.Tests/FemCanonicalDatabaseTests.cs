using CScore.Fem;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;

namespace OpenCS.OpenSees.Tests;

public sealed class FemCanonicalDatabaseTests
{
    [Fact]
    public void SaveFemSchemaEdit_RemapsTransientLoadAndDefinitionReferences()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fem-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Transient" };
            db.SaveFemSchema(schema);
            var node = new FemNode { Id = -1, SchemaId = schema.Id, NodeTag = "1" };
            var loadCase = new FemLoadCase { Id = -2, SchemaId = schema.Id, Tag = "G", Sp20Type = "permanent" };
            var nodeLoad = new FemNodeLoad { SchemaId = schema.Id, LoadCaseId = -2, NodeId = -1, Fz = -12 };
            var definition = new FemLoadDefinition { SchemaId = schema.Id, Tag = "C1" };
            definition.SetExpression(new FemLoadExpression
            {
                Mode = FemLoadExpressionMode.Sum,
                Terms = [new FemLoadTerm { LoadCaseId = -2, Coefficient = 1 }]
            });

            db.SaveFemSchemaEdit(schema.Id, [node], [], [], [loadCase], [nodeLoad], [definition]);

            var savedLoadCase = db.GetFemLoadCases(schema.Id).Single();
            var savedNode = db.GetFemNodes(schema.Id).Single();
            var savedNodeLoad = db.GetFemNodeLoads(schema.Id).Single();
            var savedDefinition = db.GetFemLoadDefinitions(schema.Id).Single();
            Assert.True(savedLoadCase.Id > 0);
            Assert.True(savedNode.Id > 0);
            Assert.Equal(savedLoadCase.Id, savedNodeLoad.LoadCaseId);
            Assert.Equal(savedNode.Id, savedNodeLoad.NodeId);
            Assert.Equal(savedLoadCase.Id, savedDefinition.GetExpression().Terms.Single().LoadCaseId);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void FemLoadsAndAnalyses_RoundTripThroughDatabase()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fem-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Test schema", SourceType = "internal" };
            db.SaveFemSchema(schema);
            var loadCase = new FemLoadCase
            {
                SchemaId = schema.Id,
                Tag = "G",
                LoadType = "permanent",
                Sp20Type = "permanent",
                Sp20Group = "dead"
            };
            db.SaveFemLoadCase(loadCase);
            var node = new FemNode { SchemaId = schema.Id, NodeTag = "1", X = 1 };
            db.SaveFemTopology(schema.Id, [node], [], []);
            var savedNode = db.GetFemNodes(schema.Id).Single();
            db.SaveFemNodeLoad(new FemNodeLoad
            {
                SchemaId = schema.Id,
                LoadCaseId = loadCase.Id,
                NodeId = savedNode.Id,
                Fz = 10
            });
            var analysis = new FemAnalysis
            {
                SchemaId = schema.Id,
                Tag = "Linear",
                Kind = "linear",
                Status = "created"
            };
            analysis.SetLoadExpression(new FemLoadExpression
            {
                Mode = FemLoadExpressionMode.Single,
                LoadCaseIds = [loadCase.Id]
            });
            db.SaveFemAnalysis(analysis);

            Assert.Equal(10, db.GetFemNodeLoads(schema.Id).Single().Fz);
            Assert.Equal("permanent", db.GetFemLoadCases(schema.Id).Single().Sp20Type);
            var copy = db.GetFemAnalysis(analysis.Id);
            Assert.NotNull(copy);
            Assert.Equal(FemLoadExpressionMode.Single, copy!.GetLoadExpression().Mode);

            db.LoadAll();
            var loadedSchema = db.FemSchemas.Single();
            Assert.Single(loadedSchema.LoadCases);
            Assert.Single(loadedSchema.Analyses);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SchemaVersion27Database_GetsFemV28ColumnsAndTables()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fem-{Guid.NewGuid():N}.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE settings (key TEXT PRIMARY KEY, value_json TEXT NOT NULL);
                    INSERT INTO settings (key, value_json) VALUES ('schema_version', '27');
                    CREATE TABLE fem_schemas (id INTEGER PRIMARY KEY AUTOINCREMENT, tag TEXT NOT NULL DEFAULT '', source_type TEXT NOT NULL DEFAULT 'internal', created TEXT NOT NULL DEFAULT '');
                    CREATE TABLE fem_nodes (id INTEGER PRIMARY KEY AUTOINCREMENT, schema_id INTEGER NOT NULL, node_tag TEXT NOT NULL DEFAULT '', x REAL NOT NULL DEFAULT 0, y REAL NOT NULL DEFAULT 0, z REAL NOT NULL DEFAULT 0, dof_mask INTEGER NOT NULL DEFAULT 0);
                    CREATE TABLE fem_load_cases (id INTEGER PRIMARY KEY AUTOINCREMENT, schema_id INTEGER NOT NULL, tag TEXT NOT NULL DEFAULT '', load_type TEXT);
                """;
                command.ExecuteNonQuery();
            }

            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "old" };
            db.SaveFemSchema(schema);
            var loadCase = new FemLoadCase { SchemaId = schema.Id, Tag = "Q", Sp20Type = "short_term" };
            db.SaveFemLoadCase(loadCase);

            Assert.Equal("short_term", db.GetFemLoadCases(schema.Id).Single().Sp20Type);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SchemaVersion32Database_RepairsFemMembersWithoutThicknessColumn()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fem-{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new DatabaseService(path))
            {
            }

            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    ALTER TABLE fem_members DROP COLUMN thickness_m;
                    UPDATE settings SET value_json = '32' WHERE key = 'schema_version';
                    """;
                command.ExecuteNonQuery();
            }

            using var repaired = new DatabaseService(path);
            Assert.Empty(repaired.GetFemMembers(1));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SaveFemTopology_PreservesLoadsByNodeTag()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fem-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Topology" };
            db.SaveFemSchema(schema);
            var loadCase = new FemLoadCase { SchemaId = schema.Id, Tag = "Q" };
            db.SaveFemLoadCase(loadCase);
            db.SaveFemTopology(schema.Id, [new FemNode { NodeTag = "A" }], [], []);
            db.SaveFemNodeLoad(new FemNodeLoad
            {
                SchemaId = schema.Id,
                LoadCaseId = loadCase.Id,
                NodeId = db.GetFemNodes(schema.Id).Single().Id,
                Fy = 4
            });

            db.SaveFemTopology(schema.Id, [new FemNode { NodeTag = "A", X = 2 }], [], []);

            var load = db.GetFemNodeLoads(schema.Id).Single();
            Assert.Equal(4, load.Fy);
            Assert.Equal(db.GetFemNodes(schema.Id).Single().Id, load.NodeId);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SaveFemTopology_RejectsDeletedLoadedNode()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fem-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Topology" };
            db.SaveFemSchema(schema);
            var loadCase = new FemLoadCase { SchemaId = schema.Id, Tag = "Q" };
            db.SaveFemLoadCase(loadCase);
            db.SaveFemTopology(schema.Id, [new FemNode { NodeTag = "A" }], [], []);
            db.SaveFemNodeLoad(new FemNodeLoad
            {
                SchemaId = schema.Id,
                LoadCaseId = loadCase.Id,
                NodeId = db.GetFemNodes(schema.Id).Single().Id,
                Fy = 4
            });

            var exception = Assert.Throws<InvalidOperationException>(() =>
                db.SaveFemTopology(schema.Id, [new FemNode { NodeTag = "B" }], [], []));

            Assert.Contains("A", exception.Message, StringComparison.Ordinal);
            Assert.Equal("A", db.GetFemNodes(schema.Id).Single().NodeTag);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
