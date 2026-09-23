using System.Text.Json;
using CScore;
using CScore.Submodel;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

public sealed class FemLinearResultParentAdapterTests
{
    static CalcResult Linear(FemLinearResult result, string status = "ok") => new()
    {
        Id = 9, TaskKind = "fem_linear", Status = status, DataJson = JsonSerializer.Serialize(result)
    };

    static readonly FemLinearResult Sample = new()
    {
        Status = "ok",
        Displacements = [new FemNodeDisplacement(2, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6)],
        Reactions = [new FemNodeReaction(1, 10, 20, 30, 40, 50, 60)],
        ElementForces = [new FemElementEndForces(7, 1, 2, 3, 4, 5, 6, -1, -2, -3, -4, -5, -6)]
    };

    [Fact]
    public void CalcResult_IsMappedWithStringTags()
    {
        var outcome = FemLinearResultParentAdapter.FromCalcResult(Linear(Sample));

        Assert.Null(outcome.Error);
        var result = outcome.Result!;
        Assert.Equal(new ParentResultCapabilities(true, true, true, true), result.Capabilities);
        Assert.True(result.TryGetDisplacement("2", out var u));
        Assert.Equal(new Dof6(0.1, 0.2, 0.3, 0.4, 0.5, 0.6), u);
        Assert.True(result.TryGetReaction("1", out var r));
        Assert.Equal(new Dof6(10, 20, 30, 40, 50, 60), r);
        Assert.True(result.TryGetEndForces("7", out var f));
        Assert.Equal(new Dof6(1, 2, 3, 4, 5, 6), f.I);
        Assert.Equal(new Dof6(-1, -2, -3, -4, -5, -6), f.J);
        Assert.False(result.TryGetEndForces("8", out _));
    }

    [Fact]
    public void NonlinearTaskKind_IsNotLinear()
    {
        var outcome = FemLinearResultParentAdapter.FromCalcResult(new CalcResult { TaskKind = "fem_nonlinear", DataJson = "{}" });

        Assert.Null(outcome.Result);
        Assert.Equal(BoundaryScenarioDiagnostics.ParentResultNotLinear, outcome.Error!.Code);
        Assert.True(outcome.Error.IsError);
    }

    [Theory]
    [InlineData("error", null)]
    [InlineData("ok", "{broken")]
    [InlineData("ok", "{}")]
    public void BadStatusBrokenJsonOrNoForces_IsInvalid(string status, string? json)
    {
        var calc = Linear(Sample, status);
        if (json is not null) calc.DataJson = json;

        var outcome = FemLinearResultParentAdapter.FromCalcResult(calc);

        Assert.Null(outcome.Result);
        Assert.Equal(BoundaryScenarioDiagnostics.ParentResultInvalid, outcome.Error!.Code);
    }
}
