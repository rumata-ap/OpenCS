using CScore;
using CScore.Fem;
using OpenCS.Utilites;
using static OpenCS.OpenSees.Tests.StraightBeamSubmodelPersistenceTests;

namespace OpenCS.OpenSees.Tests;

/// <summary>Удаление FEM-схемы не оставляет зависимых строк и не держит удалённое в кэшах процесса.</summary>
public sealed class FemSchemaDeletionTests
{
    [Fact]
    public void DeleteFemSchema_RemovesKinematicLoadsAnalysisAndCheckResults()
    {
        var path = TempDatabasePath();
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "s", SourceType = "internal" };
            db.SaveFemSchema(schema);
            db.SaveFemTopology(schema.Id, [new FemNode { NodeTag = "1" }, new FemNode { NodeTag = "2", X = 1 }],
                [new FemMember { ElemTag = "m", NodeIdsJson = "[1,2]" }], []);
            var nodeId = db.GetFemNodes(schema.Id).First().Id;
            var loadCase = new FemLoadCase { SchemaId = schema.Id, Tag = "G" };
            db.SaveFemLoadCase(loadCase);
            db.SaveFemKinematicLoad(new FemKinematicLoad { SchemaId = schema.Id, LoadCaseId = loadCase.Id, NodeId = nodeId, Dof = 1, Value = 0.01 });

            var analysisResult = new CalcResult { TaskKind = "fem_linear", TaskTag = "a", Status = "ok", DataJson = "{}" };
            db.SaveCalcResult(analysisResult);
            db.SaveFemAnalysis(new FemAnalysis { SchemaId = schema.Id, Tag = "a", ResultId = analysisResult.Id });

            var group = new FemMemberGroup { SchemaId = schema.Id, Tag = "g", MemberTagsJson = "[\"m\"]" };
            db.SaveFemMemberGroup(group);
            var check = new FemCheck { SchemaId = schema.Id, MemberId = group.Id, Tag = "c" };
            db.SaveFemCheck(check);
            var checkResult = new CalcResult { TaskKind = "fem_check", TaskTag = "c", Status = "ok", DataJson = "{}" };
            db.SaveCalcResultRaw(checkResult, check.Id);

            db.DeleteFemSchema(schema);

            foreach (var table in new[] { "fem_kinematic_loads", "fem_analyses", "fem_checks", "fem_load_cases", "fem_nodes" })
                Assert.Equal(0, CountRows(path, table));
            Assert.Null(db.GetCalcResultById(analysisResult.Id));
            Assert.Null(db.GetCalcResultById(checkResult.Id));
            Assert.DoesNotContain(db.CalcResults, r => r.Id == analysisResult.Id || r.Id == checkResult.Id);
            Assert.DoesNotContain(db.FemChecks, c => c.SchemaId == schema.Id);
        }
        finally { DeleteDatabase(path); }
    }
}
