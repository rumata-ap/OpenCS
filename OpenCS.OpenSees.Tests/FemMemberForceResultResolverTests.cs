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
}
