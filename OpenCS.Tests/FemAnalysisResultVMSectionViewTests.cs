using System.Text.Json;
using CScore;
using CScore.Fem;
using OpenCS.OpenSees.Structural;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки событий действий контекстного меню результата OpenSees.</summary>
public class FemAnalysisResultVMSectionViewTests
{
    [Fact]
    public void RequestShowMemberSection_RaisesEventWithMemberTag()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), "opencs_section_view_" + Guid.NewGuid().ToString("N") + ".db");
        var db = new DatabaseService(dbPath);
        try
        {
            var schema = new FemSchema { Tag = "Схема" };
            db.SaveFemSchema(schema);
            var result = new FemLinearResult();
            var vm = new FemAnalysisResultVM(
                new CalcResult { Status = "ok", DataJson = JsonSerializer.Serialize(result) },
                db, schema);

            string? actualTag = null;
            vm.ShowMemberSectionRequested += tag => actualTag = tag;

            vm.RequestShowMemberSection("M1");

            Assert.Equal("M1", actualTag);
        }
        finally
        {
            db.Dispose();
            try { File.Delete(dbPath); } catch (IOException) { }
        }
    }

    [Fact]
    public void RequestCreateMemberForceSet_RaisesCurrentMemberTag()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), "opencs_force_set_event_" + Guid.NewGuid().ToString("N") + ".db");
        using var db = new DatabaseService(dbPath);
        var schema = new FemSchema { Tag = "Схема" };
        db.SaveFemSchema(schema);
        var member = new FemMember { SchemaId = schema.Id, ElemTag = "M1", NodeIdsJson = "[100,200]" };
        db.SaveFemTopology(schema.Id,
            [new FemNode { SchemaId = schema.Id, NodeTag = "100", X = 0 },
             new FemNode { SchemaId = schema.Id, NodeTag = "200", X = 1 }],
            [member], []);
        db.SaveFemMeshSnapshot(schema.Id,
            [new FemMeshNode { NodeTag = "10", X = 0, SourceNodeTag = "100" },
             new FemMeshNode { NodeTag = "20", X = 1, SourceNodeTag = "200" }],
            [new FemElement { ElemTag = "101", NodeIdsJson = "[10,20]", SourceMemberTag = "M1" }]);
        var result = new FemLinearResult
        {
            Status = "ok",
            ElementForces = [new FemElementEndForces(101, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12)]
        };
        var vm = new FemAnalysisResultVM(
            new CalcResult { Status = "ok", DataJson = JsonSerializer.Serialize(result) },
            db, schema);
        string? actual = null;
        vm.CreateMemberForceSetRequested += tag => actual = tag;

        vm.RequestCreateMemberForceSet("M1");

        Assert.Equal("M1", actual);
    }

    [Fact]
    public void CanCreateMemberForceSet_UsesSelectedNotConvergedStepAndBuildInput()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), "opencs_force_set_step_" + Guid.NewGuid().ToString("N") + ".db");
        using var db = new DatabaseService(dbPath);
        var schema = new FemSchema { Tag = "Схема" };
        db.SaveFemSchema(schema);
        var member = new FemMember { SchemaId = schema.Id, ElemTag = "M1", NodeIdsJson = "[100,200]" };
        db.SaveFemTopology(schema.Id,
            [new FemNode { SchemaId = schema.Id, NodeTag = "100", X = 0 },
             new FemNode { SchemaId = schema.Id, NodeTag = "200", X = 1 }],
            [member], []);
        db.SaveFemMeshSnapshot(schema.Id,
            [new FemMeshNode { NodeTag = "10", X = 0, SourceNodeTag = "100" },
             new FemMeshNode { NodeTag = "20", X = 1, SourceNodeTag = "200" }],
            [new FemElement { ElemTag = "101", NodeIdsJson = "[10,20]", SourceMemberTag = "M1" }]);
        var force = new FemElementEndForces(101, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
        var nonlinear = new FemNonlinearResult
        {
            Status = "partial",
            Steps = [
                new FemNonlinearStepResult(1, 0.5, true, [], [], [force]),
                new FemNonlinearStepResult(2, 1.0, false, [], [], [force])]
        };
        var vm = new FemAnalysisResultVM(
            new CalcResult { Status = "partial", DataJson = JsonSerializer.Serialize(nonlinear) },
            db, schema);
        vm.SelectedStepIndex = 1;

        Assert.False(vm.CanCreateMemberForceSet("M1"));
        var input = vm.BuildMemberForceSetInput(member);
        Assert.Equal(1, input.StepIndex);
        Assert.False(input.StepConverged);
        Assert.Equal(force, Assert.Single(input.ElementForces));
    }
}
