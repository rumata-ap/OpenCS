using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.Submodel;
using CScore.Tests.Submodel;
using OpenCS.OpenSees.Structural;
using OpenCS.Services;
using OpenCS.Utilites;
using static OpenCS.OpenSees.Tests.StraightBeamSubmodelPersistenceTests;

namespace OpenCS.OpenSees.Tests;

/// <summary>Сервис на SQLite: рама из эталона записана в БД, результат — из фикстуры реального OpenSees.</summary>
public sealed class StraightBeamBoundaryScenarioServiceTests
{
    internal static PortalFrameReference.ResultFixture Fixture() => PortalFrameReference.Deserialize(File.ReadAllText(
        Path.Combine(FixtureDirectory(), PortalFrameReference.FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar))));

    static string FixtureDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "CScore.Tests"))) return Path.Combine(dir.FullName, "CScore.Tests");
        throw new DirectoryNotFoundException("Не найден каталог CScore.Tests.");
    }

    internal static FemLinearResult ToLinearResult(PortalFrameReference.ResultFixture fixture) => new()
    {
        Status = "ok",
        Displacements = fixture.Displacements.Select(r => new FemNodeDisplacement(int.Parse(r.Node),
            r.Values[0], r.Values[1], r.Values[2], r.Values[3], r.Values[4], r.Values[5])).ToList(),
        Reactions = fixture.Reactions.Select(r => new FemNodeReaction(int.Parse(r.Node),
            r.Values[0], r.Values[1], r.Values[2], r.Values[3], r.Values[4], r.Values[5])).ToList(),
        ElementForces = fixture.EndForces.Select(r => new FemElementEndForces(int.Parse(r.Element),
            r.I[0], r.I[1], r.I[2], r.I[3], r.I[4], r.I[5], r.J[0], r.J[1], r.J[2], r.J[3], r.J[4], r.J[5])).ToList(),
    };

    /// <summary>Записывает раму, её нагрузки, анализ с результатом и извлечение ригеля.</summary>
    internal static (SubmodelExtraction Extraction, ParentModel Parent) Seed(DatabaseService db, CalcResult result)
    {
        var model = PortalFrameReference.Model();
        var schema = new FemSchema { Tag = "portal", SourceType = "opensees" };
        db.SaveFemSchema(schema);
        foreach (var node in model.Nodes) node.Id = 0;
        foreach (var member in model.Members) member.Id = 0;
        db.SaveFemTopology(schema.Id, model.Nodes, model.Members, []);
        db.SaveFemMeshSnapshot(schema.Id, model.MeshNodes, model.MeshElements);
        var nodeIdByTag = db.GetFemNodes(schema.Id).ToDictionary(n => n.NodeTag, n => n.Id);
        var memberIdByTag = db.GetFemMembers(schema.Id).ToDictionary(m => m.ElemTag, m => m.Id);

        var loadCase = new FemLoadCase { SchemaId = schema.Id, Tag = "G+H" };
        db.SaveFemLoadCase(loadCase);
        db.SaveFemMemberLoad(new FemMemberLoad
        {
            SchemaId = schema.Id, LoadCaseId = loadCase.Id, MemberId = memberIdByTag["B"],
            DistributionType = "uniform", CoordinateSystem = "global", QzStart = PortalFrameReference.Q, QzEnd = PortalFrameReference.Q
        });
        db.SaveFemNodeLoad(new FemNodeLoad
        {
            SchemaId = schema.Id, LoadCaseId = loadCase.Id, NodeId = nodeIdByTag["2"], Fx = PortalFrameReference.H
        });

        db.SaveCalcResult(result);
        var analysis = new FemAnalysis { SchemaId = schema.Id, Tag = "linear", Kind = "linear", Status = "ok", ResultId = result.Id };
        db.SaveFemAnalysis(analysis);

        var parent = new ParentModel(db.GetFemNodes(schema.Id), db.GetFemMembers(schema.Id),
            db.GetFemMeshNodes(schema.Id), db.GetFemMeshElements(schema.Id));
        var adapted = MeshBeamSegmentAdapter.Build(["12", "13", "14"], parent.MeshElements, parent.MeshNodes, parent.Members);
        var chain = StraightBeamAnalyzer.Analyze(adapted.Segments, adapted.Environment, ChainTolerances.Default,
            adapted.PreferredDirection, new BeamLocalAxisFrameProvider(), adapted.Diagnostics);
        var draft = StraightBeamSubmodelBuilder.Build(schema.Id, chain, parent.MeshElements, parent.MeshNodes);
        Assert.True(draft.IsSuccess);
        var extraction = db.CreateStraightBeamSubmodel(
            new StraightBeamSubmodelRequest("ригель", schema.Id, analysis.Id, result.Id, draft.Draft!));
        return (extraction, parent);
    }

    [Fact]
    public void BuildAndSave_PersistsScenarioMatchingDomainReference()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var fixture = Fixture();
            var (extraction, parent) = Seed(db, new CalcResult
            {
                TaskKind = "fem_linear", TaskTag = "linear", Created = "2026-09-23", Status = "ok",
                DataJson = JsonSerializer.Serialize(ToLinearResult(fixture))
            });

            var saved = new StraightBeamBoundaryScenarioService(db).BuildAndSave(extraction.SubmodelSchemaId);

            var stored = Assert.Single(db.GetSubmodelBoundaryScenarios(extraction.Id));
            Assert.Equal(saved.Id, stored.Id);
            PortalFrameReference.Verify(parent, stored.Scenario);
            PortalFrameReference.VerifyHoggingAtRigidJoints(stored.Scenario);

            var domain = PortalFrameReference.Build(PortalFrameReference.Model(),
                PortalFrameReference.Extract(PortalFrameReference.Model(), ["12", "13", "14"]),
                PortalFrameReference.ToParentResult(fixture));
            foreach (var end in domain.Ends)
            {
                var persisted = stored.Scenario.Ends.Single(e => e.ParentNodeTag == end.ParentNodeTag).BoundaryVector!;
                for (int k = 0; k < 6; k++) Assert.Equal(end.BoundaryVector![k], persisted[k], 9);
            }
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void BuildAndSave_NonlinearParentResult_SavesBlockedScenario()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var (extraction, _) = Seed(db, new CalcResult
            {
                TaskKind = "fem_nonlinear", TaskTag = "nl", Created = "2026-09-23", Status = "ok", DataJson = "{}"
            });

            var saved = new StraightBeamBoundaryScenarioService(db).BuildAndSave(extraction.SubmodelSchemaId);

            Assert.Equal(ScenarioStatus.Blocked, saved.Status);
            var stored = Assert.Single(db.GetSubmodelBoundaryScenarios(extraction.Id));
            Assert.Contains(stored.Scenario.Diagnostics, d => d.Code == BoundaryScenarioDiagnostics.ParentResultNotLinear);
            Assert.Empty(stored.Scenario.Ends);
        }
        finally { DeleteDatabase(path); }
    }
}
