using System.Text.Json;
using CScore;
using CScore.Submodel;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

public sealed class FemNonlinearResultParentAdapterTests
{
    static FemNonlinearStepResult Step(int index, double lambda, bool converged = true, int stage = 0, bool refinement = false) =>
        new(index, lambda, converged,
            converged ? [new FemNodeDisplacement(2, 0.001 * lambda, 0, 0, 0, 0, 0)] : [],
            converged ? [new FemNodeReaction(1, 10 * lambda, 0, 0, 0, 0, 0)] : [],
            converged ? [new FemElementEndForces(7, 100 * lambda, 0, 0, 0, 0, 0, -100 * lambda, 0, 0, 0, 0, 0)] : [])
        { StageIndex = stage, IsRefinement = refinement };

    static CalcResult Calc(FemNonlinearResult result, string? status = null, string kind = "fem_nonlinear") => new()
    {
        Id = 42, TaskKind = kind, Status = status ?? result.Status, DataJson = JsonSerializer.Serialize(result)
    };

    [Fact]
    public void Ok_TakesOnlyConvergedStepsWithTags()
    {
        var result = new FemNonlinearResult { Status = "ok", Steps = [Step(1, 0.5), Step(2, 0.75, converged: false), Step(3, 1.0)] };

        var outcome = FemNonlinearResultParentAdapter.FromCalcResult(Calc(result));

        Assert.Null(outcome.Error);
        Assert.Equal([1, 3], outcome.Steps!.Select(s => s.StepIndex));
        Assert.Equal([0.5, 1.0], outcome.Steps!.Select(s => s.Lambda));
        var state = outcome.Steps![1].State;
        Assert.False(state.Capabilities.IsLinear);
        Assert.True(state.TryGetDisplacement("2", out var u));
        Assert.Equal(0.001, u.X, 12);
        Assert.True(state.TryGetReaction("1", out var r));
        Assert.Equal(10, r.X, 12);
        Assert.True(state.TryGetEndForces("7", out var f));
        Assert.Equal(-100, f.J.X, 12);
    }

    [Theory]
    [InlineData("not_converged")]
    [InlineData("partial")]
    public void AcceptedNonOkStatuses_WithConvergedSteps(string status)
    {
        var result = new FemNonlinearResult { Status = status, Steps = [Step(1, 0.4), Step(2, 0.45, converged: false)] };

        var outcome = FemNonlinearResultParentAdapter.FromCalcResult(Calc(result));

        Assert.Null(outcome.Error);
        Assert.Single(outcome.Steps!);
    }

    [Theory]
    [InlineData("error")]
    [InlineData("cancelled")]
    [InlineData("weird")]
    public void RejectedStatuses_AreResultInvalid(string status)
    {
        var result = new FemNonlinearResult { Status = status, Steps = [Step(1, 1.0)], Diagnostics = ["причина"] };

        var outcome = FemNonlinearResultParentAdapter.FromCalcResult(Calc(result));

        Assert.Null(outcome.Steps);
        Assert.Equal(SubmodelNonlinearDiagnostics.ResultInvalid, outcome.Error!.Code);
        Assert.True(outcome.Error.IsError);
        Assert.Contains("причина", outcome.Error.Message);
    }

    [Fact]
    public void ResolveError_JsonWithErrorsArray_IsResultInvalidWithText()
    {
        var calc = new CalcResult
        {
            TaskKind = "fem_nonlinear", Status = "error",
            DataJson = JsonSerializer.Serialize(new { error = "Модель не прошла валидацию.", errors = new[] { "нет сечения" } })
        };

        var outcome = FemNonlinearResultParentAdapter.FromCalcResult(calc);

        Assert.Equal(SubmodelNonlinearDiagnostics.ResultInvalid, outcome.Error!.Code);
        Assert.Contains("нет сечения", outcome.Error.Message);
    }

    [Fact]
    public void LinearKind_IsResultInvalid()
    {
        var outcome = FemNonlinearResultParentAdapter.FromCalcResult(Calc(new FemNonlinearResult { Status = "ok" }, kind: "fem_linear"));

        Assert.Equal(SubmodelNonlinearDiagnostics.ResultInvalid, outcome.Error!.Code);
    }

    [Fact]
    public void BrokenJson_IsResultInvalid()
    {
        var outcome = FemNonlinearResultParentAdapter.FromCalcResult(new CalcResult { TaskKind = "fem_nonlinear", Status = "ok", DataJson = "{not json" });

        Assert.Equal(SubmodelNonlinearDiagnostics.ResultInvalid, outcome.Error!.Code);
    }

    [Fact]
    public void EmptyHistoryAndOnlyFailedSteps_AreNoConvergedSteps()
    {
        var empty = FemNonlinearResultParentAdapter.FromCalcResult(Calc(new FemNonlinearResult { Status = "ok" }));
        var failed = FemNonlinearResultParentAdapter.FromCalcResult(Calc(
            new FemNonlinearResult { Status = "not_converged", Steps = [Step(1, 0.1, converged: false)] }));

        Assert.Equal(SubmodelNonlinearDiagnostics.NoConvergedSteps, empty.Error!.Code);
        Assert.Equal(SubmodelNonlinearDiagnostics.NoConvergedSteps, failed.Error!.Code);
    }

    [Fact]
    public void TwoStages_TakesLastStageSteps()
    {
        var result = new FemNonlinearResult
        {
            Status = "ok", StageTags = ["a", "b"], Steps = [Step(1, 1.0, stage: 0), Step(2, 0.5, stage: 1), Step(3, 1.0, stage: 1)]
        };

        var outcome = FemNonlinearResultParentAdapter.FromNonlinearResult(result);

        Assert.Equal([2, 3], outcome.Steps!.Select(s => s.StepIndex));
    }

    [Fact]
    public void LegacyWithoutStageTags_IsAccepted()
    {
        var outcome = FemNonlinearResultParentAdapter.FromNonlinearResult(
            new FemNonlinearResult { Status = "ok", Steps = [Step(1, 1.0)] });

        Assert.Null(outcome.Error);
    }

    [Fact]
    public void StageIndexBeyondStageTags_IsStructuralConflict()
    {
        var outcome = FemNonlinearResultParentAdapter.FromNonlinearResult(
            new FemNonlinearResult { Status = "ok", StageTags = ["a"], Steps = [Step(1, 1.0, stage: 1)] });

        Assert.Equal(SubmodelNonlinearDiagnostics.ResultInvalid, outcome.Error!.Code);
        Assert.Contains("конфликт", outcome.Error.Message);
    }

    [Fact]
    public void RefinementStep_IsIncludedAndLumpedFlagPassed()
    {
        var outcome = FemNonlinearResultParentAdapter.FromNonlinearResult(new FemNonlinearResult
        {
            Status = "not_converged", MemberLoadsLumped = true, Steps = [Step(1, 0.5), Step(2, 0.55, refinement: true)]
        });

        Assert.Equal(2, outcome.Steps!.Count);
        Assert.True(outcome.MemberLoadsLumped);
    }
}
