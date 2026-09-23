using System.Text.Json;
using CScore;
using CScore.Submodel;
using OpenCS.OpenSees.Structural;
using OpenCS.Services;
using OpenCS.Utilites;
using static OpenCS.OpenSees.Tests.StraightBeamBoundaryScenarioServiceTests;
using static OpenCS.OpenSees.Tests.StraightBeamSubmodelPersistenceTests;
using static OpenCS.OpenSees.Tests.SubmodelMaterializationPersistenceTests;

namespace OpenCS.OpenSees.Tests;

public sealed class StraightBeamSubmodelMaterializationServiceTests
{
    [Fact]
    public void Materialize_CreatesLayerWithGaugeAtStartJoint()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var seeded = SeedScenario(db);

            var outcome = new StraightBeamSubmodelMaterializationService(db).Materialize(seeded.Extraction.SubmodelSchemaId);

            Assert.NotNull(outcome.Materialization);
            Assert.DoesNotContain(outcome.Diagnostics, d => d.IsError);
            Assert.Equal(6, outcome.Materialization!.Summary.GaugeDofs.Count);
            Assert.All(outcome.Materialization.Summary.GaugeDofs, g => Assert.Equal("2", g.ChildNodeTag));
            Assert.Equal(3, db.GetFemMembers(seeded.Extraction.SubmodelSchemaId).Count);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void Verify_WithoutChildResult_AsksToRunAnalysis()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var seeded = SeedScenario(db);
            var service = new StraightBeamSubmodelMaterializationService(db);
            service.Materialize(seeded.Extraction.SubmodelSchemaId);

            var outcome = service.Verify(seeded.Extraction.SubmodelSchemaId);

            Assert.Null(outcome.Report);
            Assert.Contains(outcome.Diagnostics, d => d.Code == SubmodelMaterializationDiagnostics.Info && !d.IsError);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void Verify_WithoutMaterialization_IsStale()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var seeded = SeedScenario(db);

            var outcome = new StraightBeamSubmodelMaterializationService(db).Verify(seeded.Extraction.SubmodelSchemaId);

            Assert.Null(outcome.Report);
            Assert.Contains(outcome.Diagnostics, d => d.Code == SubmodelMaterializationDiagnostics.Stale);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void Verify_IdealChildResult_Passes()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var seeded = SeedScenario(db);
            int child = seeded.Extraction.SubmodelSchemaId;
            var service = new StraightBeamSubmodelMaterializationService(db);
            var materialization = service.Materialize(child).Materialization!;
            PlantChildResult(db, child, IdealChildResult(seeded.Scenario.Scenario, materialization.Summary));

            var outcome = service.Verify(child);

            Assert.NotNull(outcome.Report);
            Assert.True(outcome.Report!.Passed, string.Join(" | ", outcome.Diagnostics.Select(d => d.Message)));
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void RecreatedMesh_BlocksMaterializeAndVerify()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var seeded = SeedScenario(db);
            int child = seeded.Extraction.SubmodelSchemaId;
            var service = new StraightBeamSubmodelMaterializationService(db);
            var materialization = service.Materialize(child).Materialization!;
            PlantChildResult(db, child, IdealChildResult(seeded.Scenario.Scenario, materialization.Summary));

            var nodes = db.GetFemMeshNodes(child);
            var elements = db.GetFemMeshElements(child);
            foreach (var n in nodes) n.Id = 0;
            foreach (var e in elements) e.Id = 0;
            db.SaveFemMeshSnapshot(child, nodes, elements);

            var materialize = service.Materialize(child);
            Assert.Null(materialize.Materialization);
            Assert.Contains(materialize.Diagnostics, d => d.Code == SubmodelMaterializationDiagnostics.MeshStale);
            var verify = service.Verify(child);
            Assert.Null(verify.Report);
            Assert.Contains(verify.Diagnostics, d => d.Code == SubmodelMaterializationDiagnostics.MeshStale);
        }
        finally { DeleteDatabase(path); }
    }

    /// <summary>Результат ребёнка, совпадающий с родителем на цепочке; реакции gauge = p_control − p_boundary.</summary>
    static FemLinearResult IdealChildResult(BoundaryScenario scenario, SubmodelMaterializationSummary summary)
    {
        var parent = ToLinearResult(Fixture());
        var chainNodes = new HashSet<int> { 2, 3, 4, 5 };
        var chainElements = new HashSet<int> { 12, 13, 14 };
        var reactions = new List<FemNodeReaction>();
        foreach (var end in scenario.Ends)
        {
            var gauge = summary.GaugeDofs.Where(g => g.AtStart == end.AtStart).Select(g => g.Dof).ToHashSet();
            if (gauge.Count == 0) continue;
            var r = Enumerable.Range(0, 6).Select(d => gauge.Contains(d) ? end.Control.PControl![d] - end.Dofs[d].Value!.Value : 0).ToArray();
            reactions.Add(new FemNodeReaction(int.Parse(end.ChildNodeTag), r[0], r[1], r[2], r[3], r[4], r[5]));
        }
        return new FemLinearResult
        {
            Status = "ok",
            Displacements = parent.Displacements.Where(d => chainNodes.Contains(d.NodeTag)).ToList(),
            ElementForces = parent.ElementForces.Where(f => chainElements.Contains(f.ElemTag)).ToList(),
            Reactions = reactions
        };
    }

    static void PlantChildResult(DatabaseService db, int child, FemLinearResult result)
    {
        var calc = new CalcResult
        {
            TaskKind = "fem_linear", TaskTag = SubmodelMaterializationPlanner.AnalysisTag, Created = "2026-09-23", Status = "ok",
            DataJson = JsonSerializer.Serialize(result)
        };
        db.SaveCalcResult(calc);
        var analysis = db.GetFemAnalyses(child).Single(a => a.Tag == SubmodelMaterializationPlanner.AnalysisTag);
        analysis.ResultId = calc.Id;
        analysis.Status = "ok";
        db.SaveFemAnalysis(analysis);
    }
}
