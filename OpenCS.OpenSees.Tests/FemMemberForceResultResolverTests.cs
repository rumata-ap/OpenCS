using System.Text.Json;
using CScore;
using CScore.Fem;
using OpenCS.OpenSees.Structural;
using OpenCS.ViewModels;

namespace OpenCS.OpenSees.Tests;

public sealed class FemMemberForceResultResolverTests
{
    [Fact]
    public void ResolveElementForces_UsesLastConvergedNonlinearStep()
    {
        var first = new FemNonlinearStepResult(
            1, 0.4, true, [], [], [new FemElementEndForces(7, 10, 0, 0, 0, 20, 0, -10, 0, 0, 0, -20, 0)]);
        var failed = new FemNonlinearStepResult(2, 0.5, false, [], [], []);
        var result = new CalcResult
        {
            Status = "not_converged",
            DataJson = JsonSerializer.Serialize(new FemNonlinearResult
            {
                Status = "not_converged",
                Steps = [first, failed]
            })
        };

        var forces = FemMemberForceResultResolver.ResolveElementForces(result);

        var force = Assert.Single(forces);
        Assert.Equal(7, force.ElemTag);
        Assert.Equal(20, force.Myi);
    }

    [Fact]
    public void FindLatestAnalysisWithResult_IncludesNotConvergedAnalysis()
    {
        var analyses = new[]
        {
            new FemAnalysis { Id = 10, Status = "ok", ResultId = 100 },
            new FemAnalysis { Id = 11, Status = "not_converged", ResultId = 101 },
            new FemAnalysis { Id = 12, Status = "error", ResultId = 102 }
        };

        var selected = FemAnalysisResultResolver.FindLatestWithResult(analyses);

        Assert.NotNull(selected);
        Assert.Equal(101, selected!.ResultId);
        Assert.Equal("not_converged", selected.Status);
    }

    [Fact]
    public void ResolveStep_ExplicitIndex_ReturnsThatStepNotTheLastOne()
    {
        var first = new FemNonlinearStepResult(
            1, 0.4, true, [], [], [new FemElementEndForces(7, 10, 0, 0, 0, 20, 0, -10, 0, 0, 0, -20, 0)]);
        var second = new FemNonlinearStepResult(
            2, 0.8, true, [], [], [new FemElementEndForces(7, 20, 0, 0, 0, 40, 0, -20, 0, 0, 0, -40, 0)]);

        var step = FemMemberForceResultResolver.ResolveStep(Result(first, second), 1);

        Assert.NotNull(step);
        Assert.Equal(1, step!.StepIndex);
        Assert.Equal(20, step.Forces[0].Myi);          // не 40 — усилия именно первого шага
    }

    [Fact]
    public void ResolveStep_NullIndex_ReportsActualIndexOfLastConvergedStep()
    {
        var first = new FemNonlinearStepResult(
            1, 0.4, true, [], [], [new FemElementEndForces(7, 10, 0, 0, 0, 20, 0, -10, 0, 0, 0, -20, 0)]);
        var failed = new FemNonlinearStepResult(2, 0.5, false, [], [], []);

        var step = FemMemberForceResultResolver.ResolveStep(Result(first, failed), null);

        Assert.Equal(1, step!.StepIndex);
        Assert.True(step.Converged);
    }

    [Fact]
    public void ResolveStep_NotConvergedStep_IsReturnedWithFlag()
    {
        var failed = new FemNonlinearStepResult(2, 0.5, false, [], [], []);

        var step = FemMemberForceResultResolver.ResolveStep(Result(failed), 2);

        Assert.NotNull(step);
        Assert.False(step!.Converged);
    }

    [Fact]
    public void ResolveStep_UnknownIndex_ReturnsNull()
    {
        var first = new FemNonlinearStepResult(1, 0.4, true, [], [], []);

        Assert.Null(FemMemberForceResultResolver.ResolveStep(Result(first), 42));
    }

    static CalcResult Result(params FemNonlinearStepResult[] steps) => new()
    {
        Status = "ok",
        DataJson = JsonSerializer.Serialize(new FemNonlinearResult { Status = "ok", Steps = steps })
    };
}
