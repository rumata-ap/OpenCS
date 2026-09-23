using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.Submodel;
using CScore.Tests.Submodel;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;
using static OpenCS.OpenSees.Tests.StraightBeamBoundaryScenarioServiceTests;
using static OpenCS.OpenSees.Tests.StraightBeamSubmodelPersistenceTests;

namespace OpenCS.OpenSees.Tests;

public sealed class SubmodelMaterializationPersistenceTests
{
    internal sealed record Seeded(SubmodelExtraction Extraction, SubmodelBoundaryScenario Scenario, int ParentSchemaId);

    /// <summary>Рама с результатом по фикстуре, извлечение ригеля 12–14 и сохранённый сценарий.</summary>
    internal static Seeded SeedScenario(DatabaseService db)
    {
        var (extraction, _) = Seed(db, new CalcResult
        {
            TaskKind = "fem_linear", TaskTag = "linear", Created = "2026-09-23", Status = "ok",
            DataJson = JsonSerializer.Serialize(ToLinearResult(Fixture()))
        });
        var scenario = new StraightBeamBoundaryScenarioService(db).BuildAndSave(extraction.SubmodelSchemaId);
        Assert.Equal(ScenarioStatus.Complete, scenario.Status);
        return new Seeded(extraction, scenario, extraction.ParentSchemaId);
    }

    internal static SubmodelMaterializationPlan PlanFor(DatabaseService db, Seeded seeded)
    {
        var build = SubmodelMaterializationPlanner.Plan(new(seeded.Extraction, seeded.Scenario.Scenario,
            db.GetFemMeshNodes(seeded.Extraction.SubmodelSchemaId), db.GetFemMeshElements(seeded.Extraction.SubmodelSchemaId),
            db.GetFemNodes(seeded.ParentSchemaId)));
        Assert.True(build.IsSuccess, string.Join(" | ", build.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        return build.Plan!;
    }

    static int Child(Seeded seeded) => seeded.Extraction.SubmodelSchemaId;

    [Fact]
    public void Version59Database_IsUpgradedWithMaterializationTable()
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
                    DROP TABLE submodel_materializations;
                    UPDATE settings SET value_json='59' WHERE key='schema_version';
                    """;
                command.ExecuteNonQuery();
            }

            using var _ = new DatabaseService(path);

            Assert.True(TableExists(path, "submodel_materializations"));
            Assert.Equal(DatabaseService.SchemaVersion.ToString(), SchemaVersion(path));
            Assert.Equal("60", SchemaVersion(path));
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void Apply_ReplacesLayerRemapsReferencesAndKeepsProvenance()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var seeded = SeedScenario(db);
            var plan = PlanFor(db, seeded);
            int child = Child(seeded);
            var meshIdsBefore = db.GetFemMeshNodes(child).Select(n => n.Id).OrderBy(i => i).ToList();

            var stored = db.ApplySubmodelMaterialization(child, seeded.Scenario.Id, plan);

            var nodes = db.GetFemNodes(child);
            var members = db.GetFemMembers(child);
            Assert.Equal(plan.Nodes.Select(n => n.NodeTag).OrderBy(t => t), nodes.Select(n => n.NodeTag).OrderBy(t => t));
            Assert.Equal(["12", "13", "14"], members.Select(m => m.ElemTag).OrderBy(t => t));
            Assert.All(members, m => Assert.Equal(PortalFrameReference.SectionId, m.CrossSectionId));
            var loadCase = Assert.Single(db.GetFemLoadCases(child));
            Assert.Equal(SubmodelMaterializationPlanner.LoadCaseTag, loadCase.Tag);

            var nodeIds = nodes.Select(n => n.Id).ToHashSet();
            var nodeLoads = db.GetFemNodeLoads(child);
            var kinematic = db.GetFemKinematicLoads(child);
            var memberLoads = db.GetFemMemberLoads(child);
            Assert.Equal(plan.NodeLoads.Count, nodeLoads.Count);
            Assert.Equal(plan.KinematicLoads.Count, kinematic.Count);
            Assert.Equal(plan.MemberLoads.Count, memberLoads.Count);
            Assert.All(nodeLoads, l => Assert.Contains(l.NodeId, nodeIds));
            Assert.All(kinematic, l => Assert.Contains(l.NodeId, nodeIds));
            Assert.All(memberLoads, l => Assert.Contains(l.MemberId, members.Select(m => m.Id)));
            Assert.All(nodeLoads, l => Assert.Equal(loadCase.Id, l.LoadCaseId));
            Assert.All(kinematic, l => Assert.Equal(loadCase.Id, l.LoadCaseId));
            Assert.All(memberLoads, l => Assert.Equal(loadCase.Id, l.LoadCaseId));
            Assert.All(memberLoads, l => Assert.Equal("global", l.CoordinateSystem));

            // Gauge на узле 2: 6 заданных перемещений именно на этом узле.
            int gaugeNodeId = nodes.Single(n => n.NodeTag == "2").Id;
            Assert.Equal(6, kinematic.Count(k => k.NodeId == gaugeNodeId));

            var analysis = Assert.Single(db.GetFemAnalyses(child));
            Assert.Equal(SubmodelMaterializationPlanner.AnalysisTag, analysis.Tag);
            Assert.Equal([loadCase.Id], analysis.GetLoadExpression().LoadCaseIds);

            var meshNodes = db.GetFemMeshNodes(child);
            Assert.Equal(meshIdsBefore, meshNodes.Select(n => n.Id).OrderBy(i => i));
            Assert.All(meshNodes, n => Assert.Equal(n.NodeTag, n.SourceNodeTag));
            Assert.All(db.GetFemMeshElements(child), e => Assert.Equal(e.ElemTag, e.SourceMemberTag));
            var extraction = db.GetSubmodelExtractionBySubmodelSchema(child)!;
            Assert.Equal(seeded.Extraction.Nodes.Select(n => n.SourceNodeTag), extraction.Nodes.Select(n => n.SourceNodeTag));

            var read = db.GetSubmodelMaterialization(seeded.Scenario.Id)!;
            Assert.Equal(stored.Id, read.Id);
            Assert.Equal(plan.Summary.GaugeDofs, read.Summary.GaugeDofs);
            Assert.Equal(plan.Summary.RankBefore, read.Summary.RankBefore);
            Assert.Equal(plan.Summary.Diagnostics.Select(d => (d.Code, d.IsError)), read.Summary.Diagnostics.Select(d => (d.Code, d.IsError)));
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void Reapply_ReplacesLayerAndDropsOldAnalysisChecksAndResultsIncludingCaches()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var seeded = SeedScenario(db);
            int child = Child(seeded);
            db.ApplySubmodelMaterialization(child, seeded.Scenario.Id, PlanFor(db, seeded));

            var schema = db.FemSchemas.Single(s => s.Id == child);
            var oldAnalysis = schema.Analyses.Single();
            var analysisResult = new CalcResult { TaskKind = "fem_linear", TaskTag = "child", Status = "ok", DataJson = "{}" };
            db.SaveCalcResult(analysisResult);
            oldAnalysis.ResultId = analysisResult.Id;
            oldAnalysis.Status = "ok";
            db.SaveFemAnalysis(oldAnalysis);
            var group = new FemMemberGroup { SchemaId = child, Tag = "g", MemberTagsJson = "[\"13\"]" };
            db.SaveFemMemberGroup(group);
            var check = new FemCheck { SchemaId = child, MemberId = group.Id, Tag = "c" };
            db.SaveFemCheck(check);
            var checkResult = new CalcResult { TaskKind = "fem_check", TaskTag = "c", Status = "ok", DataJson = "{}" };
            db.SaveCalcResultRaw(checkResult, check.Id);

            db.ApplySubmodelMaterialization(child, seeded.Scenario.Id, PlanFor(db, seeded));

            Assert.Equal(4, db.GetFemNodes(child).Count);
            Assert.Single(db.GetFemLoadCases(child));
            var newAnalysis = Assert.Single(db.GetFemAnalyses(child));
            Assert.NotEqual(oldAnalysis.Id, newAnalysis.Id);
            Assert.Null(db.GetCalcResultById(analysisResult.Id));
            Assert.Null(db.GetCalcResultById(checkResult.Id));
            Assert.Equal(0, CountRows(path, "fem_checks"));
            Assert.Equal(1, CountRows(path, "submodel_materializations"));

            // Кэши того же экземпляра DatabaseService.
            Assert.Same(schema, db.FemSchemas.Single(s => s.Id == child));
            var cachedAnalysis = Assert.Single(schema.Analyses);
            Assert.Equal(newAnalysis.Id, cachedAnalysis.Id);
            Assert.Equal(db.GetFemLoadCases(child).Single().Id, Assert.Single(schema.LoadCases).Id);
            Assert.Empty(schema.MemberGroups);
            Assert.DoesNotContain(db.CalcResults, r => r.Id == analysisResult.Id || r.Id == checkResult.Id);
            Assert.DoesNotContain(db.FemChecks, c => c.SchemaId == child);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void StaleParentResult_IsRejectedAndNothingWritten()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var seeded = SeedScenario(db);
            var plan = PlanFor(db, seeded);
            var parentAnalysis = db.GetFemAnalysis(seeded.Extraction.ParentAnalysisId)!;
            var newer = new CalcResult { TaskKind = "fem_linear", TaskTag = "linear2", Status = "ok", DataJson = "{}" };
            db.SaveCalcResult(newer);
            parentAnalysis.ResultId = newer.Id;
            db.SaveFemAnalysis(parentAnalysis);

            var error = Assert.Throws<InvalidOperationException>(() => db.ApplySubmodelMaterialization(Child(seeded), seeded.Scenario.Id, plan));

            Assert.Equal(SubmodelMaterializationDiagnostics.Stale, error.Message);
            Assert.Empty(db.GetFemNodes(Child(seeded)));
            Assert.Equal(0, CountRows(path, "submodel_materializations"));
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void ForeignScenarioOrForeignPlan_IsRejected()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var seeded = SeedScenario(db);
            var plan = PlanFor(db, seeded);

            Assert.Throws<InvalidOperationException>(() => db.ApplySubmodelMaterialization(Child(seeded), seeded.Scenario.Id + 100, plan));
            var foreign = plan with { Summary = plan.Summary with { ExtractionId = seeded.Extraction.Id + 1 } };
            var error = Assert.Throws<InvalidOperationException>(() => db.ApplySubmodelMaterialization(Child(seeded), seeded.Scenario.Id, foreign));
            Assert.Equal("submodel_materialization_plan_mismatch", error.Message);
            Assert.Empty(db.GetFemNodes(Child(seeded)));
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void RecreatedMesh_MakesMaterializationStale()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var seeded = SeedScenario(db);
            int child = Child(seeded);
            var plan = PlanFor(db, seeded);
            db.ApplySubmodelMaterialization(child, seeded.Scenario.Id, plan);

            // Пересоздание сетки тем же снимком: теги и координаты прежние, id строк новые.
            var nodes = db.GetFemMeshNodes(child);
            var elements = db.GetFemMeshElements(child);
            foreach (var n in nodes) n.Id = 0;
            foreach (var e in elements) e.Id = 0;
            db.SaveFemMeshSnapshot(child, nodes, elements);

            var error = Assert.Throws<InvalidOperationException>(() => db.ApplySubmodelMaterialization(child, seeded.Scenario.Id, plan));
            Assert.Equal(SubmodelMaterializationDiagnostics.MeshStale, error.Message);
            Assert.NotEmpty(SubmodelMeshIntegrity.Check(db.GetSubmodelExtractionBySubmodelSchema(child)!,
                db.GetFemMeshNodes(child), db.GetFemMeshElements(child), checkIds: true));
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void ChildThatIsParentOfAnotherExtraction_CannotBeReplaced()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var seeded = SeedScenario(db);
            int child = Child(seeded);
            db.ApplySubmodelMaterialization(child, seeded.Scenario.Id, PlanFor(db, seeded));

            // Внук: извлечение КЭ 13 из материализованной дочерней схемы.
            var analysis = db.GetFemAnalyses(child).Single();
            var result = new CalcResult { TaskKind = "fem_linear", TaskTag = "child", Status = "ok", DataJson = "{}" };
            db.SaveCalcResult(result);
            analysis.ResultId = result.Id;
            db.SaveFemAnalysis(analysis);
            var childMeshNodes = db.GetFemMeshNodes(child);
            var childMeshElements = db.GetFemMeshElements(child);
            var adapted = MeshBeamSegmentAdapter.Build(["13"], childMeshElements, childMeshNodes, db.GetFemMembers(child));
            var chain = StraightBeamAnalyzer.Analyze(adapted.Segments, adapted.Environment, ChainTolerances.Default,
                adapted.PreferredDirection, new BeamLocalAxisFrameProvider(), adapted.Diagnostics);
            var draft = StraightBeamSubmodelBuilder.Build(child, chain, childMeshElements, childMeshNodes);
            Assert.True(draft.IsSuccess);
            db.CreateStraightBeamSubmodel(new StraightBeamSubmodelRequest("внук", child, analysis.Id, result.Id, draft.Draft!));

            var error = Assert.Throws<InvalidOperationException>(() =>
                db.ApplySubmodelMaterialization(child, seeded.Scenario.Id, PlanFor(db, seeded)));
            Assert.Equal("submodel_materialization_child_is_parent", error.Message);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void ScenarioResave_DropsMaterialization_AndChildDeletionRemovesEverything()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var seeded = SeedScenario(db);
            int child = Child(seeded);
            db.ApplySubmodelMaterialization(child, seeded.Scenario.Id, PlanFor(db, seeded));

            var resaved = new StraightBeamBoundaryScenarioService(db).BuildAndSave(child);
            Assert.Null(db.GetSubmodelMaterialization(seeded.Scenario.Id));
            Assert.Null(db.GetSubmodelMaterialization(resaved.Id));
            Assert.Equal(0, CountRows(path, "submodel_materializations"));

            var reseeded = seeded with { Scenario = resaved };
            db.ApplySubmodelMaterialization(child, resaved.Id, PlanFor(db, reseeded));
            Assert.Throws<InvalidOperationException>(() => db.DeleteFemSchema(db.FemSchemas.Single(s => s.Id == seeded.ParentSchemaId)));

            db.DeleteFemSchema(db.FemSchemas.Single(s => s.Id == child));

            foreach (var table in new[] { "submodel_materializations", "submodel_boundary_scenarios", "submodel_extractions" })
                Assert.Equal(0, CountRows(path, table));
            foreach (var table in new[] { "fem_nodes", "fem_members", "fem_kinematic_loads", "fem_node_loads", "fem_member_loads",
                         "fem_load_cases", "fem_analyses", "fem_mesh_nodes", "fem_elements" })
                Assert.Equal(0, CountSchemaRows(path, table, child));
            Assert.NotEqual(0, CountSchemaRows(path, "fem_nodes", seeded.ParentSchemaId));
        }
        finally { DeleteDatabase(path); }
    }

    static int CountSchemaRows(string path, string table, int schemaId)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE schema_id=@sid";
        command.Parameters.AddWithValue("@sid", schemaId);
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
