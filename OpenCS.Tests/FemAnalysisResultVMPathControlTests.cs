using System.Text.Json;
using CScore;
using CScore.Fem;
using OpenCS.OpenSees.Structural;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

public class FemAnalysisResultVMPathControlTests
{
    static (DatabaseService Db, FemSchema Schema) NewSchema()
    {
        var db = new DatabaseService(Path.Combine(Path.GetTempPath(), "opencs_vm_pc_" + Guid.NewGuid().ToString("N") + ".db"));
        var schema = new FemSchema { Tag = "Тестовая схема" };
        db.SaveFemSchema(schema);
        return (db, schema);
    }

    static FemNodeDisplacement Disp(int tag, double uz = 0) => new(tag, 0, 0, uz, 0, 0, 0);

    [Fact]
    public void Ctor_LegacyDataJsonWithoutPathControlFields_DoesNotThrow_ChartHidden()
    {
        var (db, schema) = NewSchema();
        try
        {
            var legacyResult = new FemNonlinearResult
            {
                Status = "ok",
                Steps = [new FemNonlinearStepResult(1, 1.0, true, [Disp(1)], [], []) { StageIndex = 0 }],
                StageTags = ["Стадия 1"]
            };
            var calcResult = new CalcResult { Status = "ok", DataJson = JsonSerializer.Serialize(legacyResult) };

            var vm = new FemAnalysisResultVM(calcResult, db, schema);

            Assert.False(vm.HasControlDisplacementChart);
            Assert.Equal(vm.Steps.Count, vm.ControlDisplacementPoints.Count);
            Assert.True(double.IsNaN(vm.ControlDisplacementPoints[0].X));
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public void Ctor_DisplacementControlResult_ChartVisibleWithMatchingPointCount()
    {
        var (db, schema) = NewSchema();
        try
        {
            var dc = new FemDisplacementControlSettings(1, 3, 0.001, 0.0001, 0.01, -0.02, 50);
            var result = new FemNonlinearResult
            {
                Status = "ok",
                Steps =
                [
                    new FemNonlinearStepResult(1, 0.5, true, [Disp(1, uz: -0.01)], [], []) { StageIndex = 0 },
                    new FemNonlinearStepResult(2, 0.5, true, [Disp(1, uz: -0.02)], [], []) { StageIndex = 0 },
                ],
                StageTags = ["Стадия 1"],
                StagePathControls = [new FemPathControlSettings(FemPathControlMode.DisplacementControl, DisplacementControl: dc)]
            };
            var calcResult = new CalcResult { Status = "ok", DataJson = JsonSerializer.Serialize(result) };

            var vm = new FemAnalysisResultVM(calcResult, db, schema);

            Assert.True(vm.HasControlDisplacementChart);
            Assert.Equal(vm.Steps.Count, vm.ControlDisplacementPoints.Count);
            Assert.Equal(-0.02, vm.ControlDisplacementPoints[1].X, 12);
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public void Ctor_LastStepFailedWithKnownReason_SetsHasStopReasonAndText()
    {
        var (db, schema) = NewSchema();
        try
        {
            var result = new FemNonlinearResult
            {
                Status = "not_converged",
                Steps =
                [
                    new FemNonlinearStepResult(1, 0.5, true, [Disp(1)], [], []) { StageIndex = 0 },
                    new FemNonlinearStepResult(2, 0.6, false, [], [], []) { StageIndex = 0, StopReason = "min_increment_reached" },
                ],
                StageTags = ["Стадия 1"]
            };
            var calcResult = new CalcResult { Status = "not_converged", DataJson = JsonSerializer.Serialize(result) };

            var vm = new FemAnalysisResultVM(calcResult, db, schema);

            Assert.True(vm.HasStopReason);
            Assert.Equal(Loc.S("FemResultStopReasonMinIncrementReached"), vm.StopReasonText);
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public void Ctor_AllStepsConverged_HasStopReasonFalse()
    {
        var (db, schema) = NewSchema();
        try
        {
            var result = new FemNonlinearResult
            {
                Status = "ok",
                Steps = [new FemNonlinearStepResult(1, 1.0, true, [Disp(1)], [], []) { StageIndex = 0 }],
                StageTags = ["Стадия 1"]
            };
            var calcResult = new CalcResult { Status = "ok", DataJson = JsonSerializer.Serialize(result) };

            var vm = new FemAnalysisResultVM(calcResult, db, schema);

            Assert.False(vm.HasStopReason);
            Assert.Equal("", vm.StopReasonText);
        }
        finally { db.Dispose(); }
    }
}
