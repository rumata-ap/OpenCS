using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.Submodel;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public sealed class StraightBeamSubmodelPersistenceTests
{
    [Fact]
    public void NewDatabase_CreatesSubmodelExtractionTables()
    {
        var path = TempDatabasePath();
        try
        {
            using var _ = new DatabaseService(path);

            Assert.True(TableExists(path, "submodel_extractions"));
            Assert.True(TableExists(path, "submodel_extraction_nodes"));
            Assert.True(TableExists(path, "submodel_extraction_segments"));
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void Version56Database_IsUpgradedTo57WithSubmodelTables()
    {
        var path = TempDatabasePath();
        try
        {
            using (var db = new DatabaseService(path)) { }
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    DROP TABLE submodel_extraction_nodes;
                    DROP TABLE submodel_extraction_segments;
                    DROP TABLE submodel_extractions;
                    UPDATE settings SET value_json='56' WHERE key='schema_version';
                    """;
                command.ExecuteNonQuery();
            }

            using var _ = new DatabaseService(path);

            Assert.True(TableExists(path, "submodel_extractions"));
            Assert.True(TableExists(path, "submodel_extraction_nodes"));
            Assert.True(TableExists(path, "submodel_extraction_segments"));
            Assert.Equal("57", SchemaVersion(path));
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void CreateStraightBeamSubmodel_PersistsAutonomousMeshAndProvenance()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var seed = CreateSeed(db);

            var extraction = db.CreateStraightBeamSubmodel(seed.Request);

            Assert.Equal(seed.Schema.Id, extraction.ParentSchemaId);
            Assert.Equal(seed.Analysis.Id, extraction.ParentAnalysisId);
            Assert.Equal(seed.Result.Id, extraction.ParentResultId);
            Assert.Equal(seed.Analysis.LoadExpressionJson, extraction.LoadExpressionJson);
            Assert.Equal(1.0, extraction.ReferenceScale);
            Assert.Equal((3, 2, 0), db.GetFemMeshSnapshotCounts(extraction.SubmodelSchemaId));
            Assert.Empty(db.GetFemLoadCases(extraction.SubmodelSchemaId));
            Assert.Empty(db.GetFemAnalyses(extraction.SubmodelSchemaId));
            Assert.Equal(["1", "2", "3"], extraction.Nodes.Select(x => x.SubmodelNodeTag));
            Assert.Equal([0, 1], extraction.Segments.Select(x => x.Ordinal));
            Assert.True(extraction.Segments[1].IsReversed);

            var child = Assert.Single(db.FemSchemas, x => x.Id == extraction.SubmodelSchemaId);
            Assert.Equal("submodel", child.SourceType);
            Assert.Equal(extraction.Id, db.GetSubmodelExtractionBySubmodelSchema(child.Id)!.Id);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void CreateStraightBeamSubmodel_StaleParentResult_RejectsWithoutChildSchema()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var seed = CreateSeed(db);
            var newer = new CalcResult { TaskKind = "fem_analysis", TaskTag = "newer", Created = "2026-09-10", Status = "ok" };
            db.SaveCalcResult(newer);
            seed.Analysis.ResultId = newer.Id;
            db.SaveFemAnalysis(seed.Analysis);

            var error = Assert.Throws<InvalidOperationException>(() => db.CreateStraightBeamSubmodel(seed.Request));

            Assert.Equal("submodel_persistence_parent_result_stale", error.Message);
            Assert.DoesNotContain(db.FemSchemas, x => x.SourceType == "submodel");
            Assert.Equal(0, CountRows(path, "submodel_extractions"));
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void CreateStraightBeamSubmodel_MissingParentSchema_RejectsWithoutChildSchema()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var seed = CreateSeed(db);
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM fem_schemas WHERE id=@id";
                command.Parameters.AddWithValue("@id", seed.Schema.Id);
                command.ExecuteNonQuery();
            }

            var error = Assert.Throws<InvalidOperationException>(() => db.CreateStraightBeamSubmodel(seed.Request));

            Assert.Equal("submodel_persistence_parent_schema_missing", error.Message);
            Assert.Equal(0, CountRows(path, "submodel_extractions"));
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void CreateStraightBeamSubmodel_DuplicateSegmentOrdinal_RollsBackEverything()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var seed = CreateSeed(db);
            var draft = seed.Request.Draft;
            var duplicateOrdinal = draft.Segments[1] with { Ordinal = draft.Segments[0].Ordinal };
            var invalidDraft = draft with { Segments = [draft.Segments[0], duplicateOrdinal] };
            var invalidRequest = seed.Request with { SubmodelTag = "invalid", Draft = invalidDraft };

            Assert.Throws<SqliteException>(() => db.CreateStraightBeamSubmodel(invalidRequest));

            Assert.DoesNotContain(db.FemSchemas, x => x.SourceType == "submodel");
            Assert.Equal(0, CountRows(path, "submodel_extractions"));
            Assert.Equal(0, CountRows(path, "submodel_extraction_nodes"));
            Assert.Equal(0, CountRows(path, "submodel_extraction_segments"));
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void CreateStraightBeamSubmodel_UnresolvableMeshElementNode_RejectsWithoutChildSchema()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var seed = CreateSeed(db);
            seed.Request.Draft.MeshElements[0].NodeIdsJson = "[1,999]";

            var error = Assert.Throws<InvalidOperationException>(() => db.CreateStraightBeamSubmodel(seed.Request));

            Assert.Equal("submodel_persistence_mesh_nodes_invalid", error.Message);
            Assert.DoesNotContain(db.FemSchemas, x => x.SourceType == "submodel");
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void DeleteFemSchema_SubmodelDeletesProvenanceButParentIsProtected()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var seed = CreateSeed(db);
            var extraction = db.CreateStraightBeamSubmodel(seed.Request);
            var child = db.FemSchemas.Single(x => x.Id == extraction.SubmodelSchemaId);

            Assert.Throws<InvalidOperationException>(() => db.DeleteFemSchema(seed.Schema));
            db.DeleteFemSchema(child);

            Assert.Null(db.GetSubmodelExtractionBySubmodelSchema(extraction.SubmodelSchemaId));
            Assert.Equal(0, CountRows(path, "submodel_extractions"));
            Assert.Equal(0, CountRows(path, "submodel_extraction_nodes"));
            Assert.Equal(0, CountRows(path, "submodel_extraction_segments"));
        }
        finally { DeleteDatabase(path); }
    }

    internal static Seed CreateSeed(DatabaseService db)
    {
        var schema = new FemSchema { Tag = "parent", SourceType = "internal" };
        db.SaveFemSchema(schema);
        var nodes = new List<FemMeshNode>
        {
            new() { NodeTag = "1", X = 0, Y = 0, Z = 0, SourceNodeTag = "101", SourceMemberTag = "M1" },
            new() { NodeTag = "2", X = 1, Y = 0, Z = 0, SourceNodeTag = "102", SourceMemberTag = "M1" },
            new() { NodeTag = "3", X = 2, Y = 0, Z = 0, SourceNodeTag = "103", SourceMemberTag = "M1" }
        };
        var elements = new List<FemElement>
        {
            new() { ElemTag = "a", ElemType = "beam", NodeIdsJson = "[1,2]", SourceMemberTag = "M1" },
            new() { ElemTag = "b", ElemType = "beam", NodeIdsJson = "[2,3]", SourceMemberTag = "M1" }
        };
        db.SaveFemMeshSnapshot(schema.Id, nodes, elements);

        var result = new CalcResult { TaskKind = "fem_analysis", TaskTag = "parent-result", Created = "2026-09-10", Status = "ok" };
        db.SaveCalcResult(result);
        var analysis = new FemAnalysis
        {
            SchemaId = schema.Id, Tag = "parent-analysis", Status = "ok", ResultId = result.Id,
            LoadExpressionJson = "{\"kind\":\"combination\",\"id\":17}"
        };
        db.SaveFemAnalysis(analysis);

        var build = StraightBeamSubmodelBuilder.Build(schema.Id, Analysis(), elements, nodes);
        var draft = Assert.IsType<SubmodelExtractionDraft>(build.Draft);
        Assert.True(build.IsSuccess);
        return new(schema, analysis, result, new StraightBeamSubmodelRequest("straight-chain", schema.Id, analysis.Id, result.Id, draft));
    }

    static StraightBeamChainAnalysis Analysis()
    {
        var first = new BeamSegmentInput("a", new PlanarVector3(0, 0, 0), new PlanarVector3(1, 0, 0), 0, BetaSource.Member, "M1");
        var second = new BeamSegmentInput("b", new PlanarVector3(1, 0, 0), new PlanarVector3(2, 0, 0), 10, BetaSource.Member, "M1");
        var chain = new StraightBeamChain(
            [new OrderedBeamSegment(first, false, 0, 1, 1, 0), new OrderedBeamSegment(second, true, 1, 2, 1, 0)],
            [new ChainNode(new PlanarVector3(0, 0, 0), 0, true, ["a"], 0), new ChainNode(new PlanarVector3(1, 0, 0), 1, false, ["a", "b"], 0), new ChainNode(new PlanarVector3(2, 0, 0), 2, true, ["b"], 0)],
            new PlanarVector3(0, 0, 0), new PlanarVector3(1, 0, 0), 2, [], []);
        return new StraightBeamChainAnalysis(ChainVerdict.Extractable, chain, [], [], ChainTolerances.Default.Resolve(2), new ChainMetrics(null, null, null, null, null, 2, 1));
    }

    static bool TableExists(string path, string table)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name";
        command.Parameters.AddWithValue("@name", table);
        return (long)command.ExecuteScalar()! == 1;
    }

    static int CountRows(string path, string table)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    static string? SchemaVersion(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value_json FROM settings WHERE key='schema_version'";
        return command.ExecuteScalar() as string;
    }

    static string TempDatabasePath() => Path.Combine(Path.GetTempPath(), $"opencs-submodel-{Guid.NewGuid():N}.db");

    static void DeleteDatabase(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    internal sealed record Seed(FemSchema Schema, FemAnalysis Analysis, CalcResult Result, StraightBeamSubmodelRequest Request);
}
