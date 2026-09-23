using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.Submodel;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using static OpenCS.OpenSees.Tests.StraightBeamSubmodelPersistenceTests;

namespace OpenCS.OpenSees.Tests;

public sealed class SubmodelBoundaryScenarioPersistenceTests
{
    static BoundaryScenario Scenario(int extractionId, ScenarioStatus status = ScenarioStatus.Complete) => new(
        extractionId, 1.0, status, LoadCompleteness.Known,
        [new RetainedDistributedLoad("a", 0, 1, new PlanarVector3(0, 0, -10), new PlanarVector3(0, 0, -10), 1, "M1")],
        [], [], [], new LoadAccounting(1, 0, 0),
        [new ScenarioEnd(true, "1", "1", [.. Enumerable.Repeat(new DofAssignment(DofMode.Force, 2.5, DofSource.Auto), 6)],
            new Dof6(0, 0, 10, 0, 5, 0), [], null, null, new ControlCheck(new Dof6(0, 0, 10, 0, 5, 0), 0, 0, true, true))],
        [new FemValidationDiagnostic(BoundaryScenarioDiagnostics.GaugeRequired, "gauge", false, [])]);

    [Fact]
    public void NewDatabase_CreatesScenarioTable()
    {
        var path = TempDatabasePath();
        try
        {
            using var _ = new DatabaseService(path);
            Assert.True(TableExists(path, "submodel_boundary_scenarios"));
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void Version58Database_IsUpgradedWithScenarioTable()
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
                    DROP TABLE submodel_boundary_scenarios;
                    UPDATE settings SET value_json='58' WHERE key='schema_version';
                    """;
                command.ExecuteNonQuery();
            }

            using var _ = new DatabaseService(path);

            Assert.True(TableExists(path, "submodel_boundary_scenarios"));
            Assert.Equal("59", SchemaVersion(path));
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void Save_RoundTrips_AndReplacesSameOrdinal()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var extraction = db.CreateStraightBeamSubmodel(CreateSeed(db).Request);

            var first = db.SaveSubmodelBoundaryScenario(extraction.Id, Scenario(extraction.Id));
            var second = db.SaveSubmodelBoundaryScenario(extraction.Id, Scenario(extraction.Id, ScenarioStatus.Incomplete));

            var stored = Assert.Single(db.GetSubmodelBoundaryScenarios(extraction.Id));
            Assert.Equal(second.Id, stored.Id);
            Assert.NotEqual(first.Id, second.Id);
            Assert.Equal((0, ScenarioStatus.Incomplete, LoadCompleteness.Known), (stored.Ordinal, stored.Status, stored.LoadCompleteness));
            Assert.Equal(new Dof6(0, 0, 10, 0, 5, 0), stored.Scenario.Ends[0].BoundaryVector);
            Assert.Equal(-10, stored.Scenario.RetainedDistributedLoads[0].QAtA.Z);
            Assert.Equal(BoundaryScenarioDiagnostics.GaugeRequired, stored.Scenario.Diagnostics[0].Code);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void Save_StaleParentResult_IsRejected()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var seed = CreateSeed(db);
            var extraction = db.CreateStraightBeamSubmodel(seed.Request);
            var newer = new CalcResult { TaskKind = "fem_linear", TaskTag = "newer", Created = "2026-09-23", Status = "ok" };
            db.SaveCalcResult(newer);
            seed.Analysis.ResultId = newer.Id;
            db.SaveFemAnalysis(seed.Analysis);

            var error = Assert.Throws<InvalidOperationException>(() =>
                db.SaveSubmodelBoundaryScenario(extraction.Id, Scenario(extraction.Id)));

            Assert.Equal(BoundaryScenarioDiagnostics.ExtractionStale, error.Message);
            Assert.Equal(0, CountRows(path, "submodel_boundary_scenarios"));
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void Save_MismatchedOrMissingExtraction_IsRejected()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var extraction = db.CreateStraightBeamSubmodel(CreateSeed(db).Request);

            Assert.Equal("submodel_boundary_extraction_mismatch", Assert.Throws<InvalidOperationException>(() =>
                db.SaveSubmodelBoundaryScenario(extraction.Id, Scenario(extraction.Id + 1))).Message);
            Assert.Equal("submodel_boundary_extraction_missing", Assert.Throws<InvalidOperationException>(() =>
                db.SaveSubmodelBoundaryScenario(extraction.Id + 1, Scenario(extraction.Id + 1))).Message);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void DeleteChildSchema_RemovesScenarios_ParentStaysProtected()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var seed = CreateSeed(db);
            var extraction = db.CreateStraightBeamSubmodel(seed.Request);
            db.SaveSubmodelBoundaryScenario(extraction.Id, Scenario(extraction.Id));

            Assert.Throws<InvalidOperationException>(() => db.DeleteFemSchema(seed.Schema));
            Assert.Equal(1, CountRows(path, "submodel_boundary_scenarios"));

            db.DeleteFemSchema(db.FemSchemas.Single(x => x.Id == extraction.SubmodelSchemaId));

            Assert.Equal(0, CountRows(path, "submodel_boundary_scenarios"));
            Assert.Equal(0, CountRows(path, "submodel_extractions"));
        }
        finally { DeleteDatabase(path); }
    }
}
