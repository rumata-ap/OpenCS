using System.Text.Json;
using CScore;
using CScore.Fem;
using OpenCS.OpenSees.Structural;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверки сохранения созданного OpenSees набора усилий.</summary>
public class FemMemberForceSetPersistenceTests
{
    [Fact]
    public void SaveCreatedForceSet_PersistsAndAddsToForceSetCollection()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), "opencs_force_set_save_" + Guid.NewGuid().ToString("N") + ".db");
        using var db = new DatabaseService(dbPath);
        var schema = new FemSchema { Tag = "Схема" };
        db.SaveFemSchema(schema);
        var member = new FemMember { SchemaId = schema.Id, ElemTag = "M1" };
        db.SaveFemMember(member);
        var forceSet = new ForceSet
        {
            Num = 18, Tag = "OS M1", Description = "step",
            Kind = "bar", SourceType = "fea",
            SourceSchemaId = schema.Id, SourceMemberId = member.Id,
            SourceElementTag = member.ElemTag,
            Items = [
                new LoadItem { Num = 1, Label = "node 10" },
                new LoadItem { Num = 2, Label = "node 20" },
                new LoadItem { Num = 3, Label = "node 30" }]
        };

        db.SaveForceSet(forceSet);
        db.ForceSets.Add(forceSet);

        Assert.Contains(forceSet, db.ForceSets);
        Assert.Equal("fea", forceSet.SourceType);
        Assert.Equal(schema.Id, forceSet.SourceSchemaId);
        Assert.Equal(member.Id, forceSet.SourceMemberId);
        Assert.Equal(3, forceSet.Items.Count);
    }

    [Fact]
    public void CreateMemberForceSet_NotConvergedSelectedStepDoesNotRaiseEvent()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), "opencs_force_set_not_converged_" + Guid.NewGuid().ToString("N") + ".db");
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
        bool raised = false;
        vm.CreateMemberForceSetRequested += _ => raised = true;

        vm.RequestCreateMemberForceSet("M1");

        Assert.False(vm.CanCreateMemberForceSet("M1"));
        Assert.False(raised);
        Assert.DoesNotContain(db.ForceSets, set => set.Tag == "OS M1");
    }
}
