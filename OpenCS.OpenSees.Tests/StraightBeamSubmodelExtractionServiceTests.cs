using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.Submodel;
using OpenCS.Services;
using OpenCS.Utilites;
using static OpenCS.OpenSees.Tests.StraightBeamBoundaryScenarioServiceTests;
using static OpenCS.OpenSees.Tests.StraightBeamSubmodelPersistenceTests;

namespace OpenCS.OpenSees.Tests;

/// <summary>Сервис извлечения на SQLite: рама из эталона, линейный результат из фикстуры OpenSees.</summary>
public sealed class StraightBeamSubmodelExtractionServiceTests
{
    static CalcResult LinearResult() => new()
    {
        TaskKind = "fem_linear", TaskTag = "linear", Created = "2026-09-29", Status = "ok",
        DataJson = JsonSerializer.Serialize(ToLinearResult(Fixture()))
    };

    static void InDatabase(Action<DatabaseService> body)
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            body(db);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void Extract_Girder_CreatesSubmodelSchemaWithProvenance() => InDatabase(db =>
    {
        var (schema, analysis, parent) = SeedParent(db, LinearResult());
        int schemas = db.FemSchemas.Count;

        var outcome = new StraightBeamSubmodelExtractionService(db)
            .Extract(schema.Id, analysis.Id, ["12", "13", "14"], parent.Members, "portal — субмодель B");

        Assert.DoesNotContain(outcome.Diagnostics, d => d.IsError);
        var extraction = outcome.Extraction!;
        Assert.Equal(schemas + 1, db.FemSchemas.Count);
        var child = db.FemSchemas.Single(s => s.Id == extraction.SubmodelSchemaId);
        Assert.Equal("submodel", child.SourceType);
        Assert.Equal("portal — субмодель B", child.Tag);
        Assert.Equal(analysis.Id, extraction.ParentAnalysisId);
        Assert.Equal(analysis.ResultId, extraction.ParentResultId);
        Assert.Equal(["12", "13", "14"], extraction.Segments.OrderBy(s => s.Ordinal).Select(s => s.ParentElementTag));
        Assert.Equal(["2", "3", "4", "5"], extraction.Nodes.Select(n => n.ParentNodeTag).Order(StringComparer.Ordinal));
        Assert.NotEqual(ChainVerdict.NotExtractable, outcome.Chain!.Verdict);
    });

    [Fact]
    public void Extract_MemberTag_ExpandsToMeshSegments() => InDatabase(db =>
    {
        var (schema, analysis, parent) = SeedParent(db, LinearResult());

        var outcome = new StraightBeamSubmodelExtractionService(db).Extract(schema.Id, analysis.Id, ["B"], parent.Members, "B");

        Assert.NotEqual(ChainVerdict.NotExtractable, outcome.Chain!.Verdict);
        Assert.Equal(3, outcome.Extraction!.Segments.Count);
    });

    [Fact]
    public void Extract_NonlinearOrResultlessAnalysis_IsRejected() => InDatabase(db =>
    {
        var (schema, _, parent) = SeedParent(db, LinearResult());
        var nonlinearResult = new CalcResult { TaskKind = "fem_nonlinear", TaskTag = "nl", Created = "2026-09-29", Status = "ok", DataJson = "{}" };
        db.SaveCalcResult(nonlinearResult);
        var nonlinear = new FemAnalysis { SchemaId = schema.Id, Tag = "nl", Kind = "nonlinear", Status = "ok", ResultId = nonlinearResult.Id };
        db.SaveFemAnalysis(nonlinear);
        var resultless = new FemAnalysis { SchemaId = schema.Id, Tag = "empty", Kind = "linear" };
        db.SaveFemAnalysis(resultless);
        var service = new StraightBeamSubmodelExtractionService(db);
        int schemas = db.FemSchemas.Count;

        Assert.DoesNotContain(service.EligibleParentAnalyses(schema.Id), a => a.Id == nonlinear.Id || a.Id == resultless.Id);
        foreach (var analysis in new[] { nonlinear, resultless })
        {
            var outcome = service.Extract(schema.Id, analysis.Id, ["12", "13", "14"], parent.Members, "x");
            Assert.Null(outcome.Extraction);
            Assert.Contains(outcome.Diagnostics, d => d.Code == StraightBeamSubmodelExtractionService.ParentAnalysisInvalid && d.IsError);
        }
        Assert.Equal(schemas, db.FemSchemas.Count);
    });

    [Fact]
    public void Extract_DeletedParentResult_IsStale() => InDatabase(db =>
    {
        var (schema, analysis, parent) = SeedParent(db, LinearResult());
        db.DeleteCalcResult(db.CalcResults.Single(r => r.Id == analysis.ResultId));
        int schemas = db.FemSchemas.Count;

        var outcome = new StraightBeamSubmodelExtractionService(db)
            .Extract(schema.Id, analysis.Id, ["12", "13", "14"], parent.Members, "x");

        Assert.Null(outcome.Extraction);
        Assert.Contains(outcome.Diagnostics, d => d.Code == "submodel_persistence_parent_result_stale" && d.IsError);
        Assert.Equal(schemas, db.FemSchemas.Count);
    });

    [Fact]
    public void Extract_ColumnAndGirder_IsNotExtractable() => InDatabase(db =>
    {
        var (schema, analysis, parent) = SeedParent(db, LinearResult());
        int schemas = db.FemSchemas.Count;

        var outcome = new StraightBeamSubmodelExtractionService(db)
            .Extract(schema.Id, analysis.Id, ["11", "12"], parent.Members, "x");

        Assert.Null(outcome.Extraction);
        Assert.Equal(ChainVerdict.NotExtractable, outcome.Chain!.Verdict);
        Assert.Contains(outcome.Diagnostics, d => d.IsError);
        Assert.Equal(schemas, db.FemSchemas.Count);
    });
}
