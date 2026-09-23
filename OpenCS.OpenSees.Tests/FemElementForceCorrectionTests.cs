using System.Text.Json;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Tests;

public sealed class FemElementForceCorrectionTests
{
    static readonly double[] P0I = [1, 2, 3, 4, 5, 6], P0J = [7, 8, 9, 10, 11, 12];
    static readonly double[] P1I = [100, 0, 0, 0, 0, 0], P1J = [0, 0, 0, 0, 0, 200];

    static FemElementEndForces Forces(int tag, double value = 1000) =>
        new(tag, value, value, value, value, value, value, value, value, value, value, value, value);

    static FemNonlinearStepResult Step(int index, double lambda, int stage = 0, bool converged = true, bool refinement = false) =>
        new(index, lambda, converged, [], [], converged ? [Forces(5), Forces(6)] : [])
        { StageIndex = stage, IsRefinement = refinement };

    static FemNonlinearResult Result(params FemNonlinearStepResult[] steps) => new() { Status = "ok", Steps = steps };

    static double[] Vector(FemElementEndForces f) =>
        [f.Ni, f.Qyi, f.Qzi, f.Mxi, f.Myi, f.Mzi, f.Nj, f.Qyj, f.Qzj, f.Mxj, f.Myj, f.Mzj];

    static void AssertShift(FemElementEndForces actual, double[] shift)
    {
        var v = Vector(actual);
        for (int k = 0; k < 12; k++) Assert.Equal(1000 - shift[k], v[k], 9);
    }

    static double[] Combine(double l0, double l1) =>
        Enumerable.Range(0, 12).Select(k => l0 * (k < 6 ? P0I[k] : P0J[k - 6]) + l1 * (k < 6 ? P1I[k] : P1J[k - 6])).ToArray();

    [Fact]
    public void SingleStage_SubtractsLambdaTimesEquivalent()
    {
        var result = Result(Step(1, 0.5), Step(2, 1.0));

        var corrected = FemElementForceCorrection.Apply(result, [new(0, 5, P0I, P0J)], "d");

        AssertShift(corrected.Steps[0].ElementForces[0], Combine(0.5, 0));
        AssertShift(corrected.Steps[1].ElementForces[0], Combine(1.0, 0));
        Assert.Equal(1000, corrected.Steps[1].ElementForces[1].Ni);   // элемент без эквивалента
        Assert.True(corrected.MemberLoadsLumped);
        Assert.Equal("d", corrected.Diagnostics[^1]);
    }

    [Fact]
    public void TwoStages_PreviousStageHeldAtFinalLambda()
    {
        var result = Result(Step(1, 0.4, stage: 0), Step(2, 0.8, stage: 0), Step(3, 0.3, stage: 1));

        var corrected = FemElementForceCorrection.Apply(result, [new(0, 5, P0I, P0J), new(1, 5, P1I, P1J)], "d");

        AssertShift(corrected.Steps[1].ElementForces[0], Combine(0.8, 0));
        AssertShift(corrected.Steps[2].ElementForces[0], Combine(0.8, 0.3));
    }

    [Fact]
    public void StageWithoutConvergedSteps_ContributesZero()
    {
        var result = Result(Step(1, 0.2, stage: 0, converged: false), Step(2, 0.5, stage: 1));

        var corrected = FemElementForceCorrection.Apply(result, [new(0, 5, P0I, P0J), new(1, 5, P1I, P1J)], "d");

        Assert.Empty(corrected.Steps[0].ElementForces);
        AssertShift(corrected.Steps[1].ElementForces[0], Combine(0, 0.5));
    }

    [Fact]
    public void RefinementStep_IsCorrectedAndSetsFinalLambda()
    {
        var result = Result(Step(1, 0.5, stage: 0), Step(2, 0.55, stage: 0, refinement: true), Step(3, 0.2, stage: 1));

        var corrected = FemElementForceCorrection.Apply(result, [new(0, 5, P0I, P0J), new(1, 5, P1I, P1J)], "d");

        AssertShift(corrected.Steps[1].ElementForces[0], Combine(0.55, 0));
        AssertShift(corrected.Steps[2].ElementForces[0], Combine(0.55, 0.2));
        Assert.True(corrected.Steps[1].IsRefinement);
    }

    [Fact]
    public void NonConvergedStep_Untouched()
    {
        var failed = Step(2, 0.6, converged: false) with { StopReason = "no_convergence" };
        var result = Result(Step(1, 0.5), failed);

        var corrected = FemElementForceCorrection.Apply(result, [new(0, 5, P0I, P0J)], "d");

        Assert.Same(failed, corrected.Steps[1]);
    }

    [Fact]
    public void EmptyEquivalents_ReturnsSameObject()
    {
        var result = Result(Step(1, 1.0));

        Assert.Same(result, FemElementForceCorrection.Apply(result, [], "d"));
    }

    [Fact]
    public void CopiesEveryOtherProperty()
    {
        var result = new FemNonlinearResult
        {
            Status = "not_converged", Steps = [Step(1, 1.0)], Diagnostics = ["a"], ArtifactDirectory = "dir",
            LimitReached = true, LastConvergedLoadFactor = 0.9, FailedLoadFactor = 0.95, RefinementDivisions = 7,
            CalcTypeName = "NL", FiberStateFileName = "f.out", SectionOrderFileName = "s.out", StageTags = ["x"],
            StagePathControls = [new FemPathControlSettings()], PathControlSwitches = [new FemPathControlSwitch(0, 3)],
            StageCompletions = [new FemStageCompletion(0, "completed")]
        };
        // Контроль полноты фикстуры: каждое init-свойство (кроме изменяемых поправкой) задано явно.
        var changed = new HashSet<string> { nameof(FemNonlinearResult.Steps), nameof(FemNonlinearResult.Diagnostics), nameof(FemNonlinearResult.MemberLoadsLumped) };
        var defaults = new FemNonlinearResult();
        foreach (var property in typeof(FemNonlinearResult).GetProperties().Where(p => !changed.Contains(p.Name)))
            Assert.NotEqual(JsonSerializer.Serialize(property.GetValue(defaults)), JsonSerializer.Serialize(property.GetValue(result)));

        var corrected = FemElementForceCorrection.Apply(result, [new(0, 5, P0I, P0J)], "d");

        foreach (var property in typeof(FemNonlinearResult).GetProperties().Where(p => !changed.Contains(p.Name)))
            Assert.Equal(JsonSerializer.Serialize(property.GetValue(result)), JsonSerializer.Serialize(property.GetValue(corrected)));
        Assert.Equal(["a", "d"], corrected.Diagnostics);
    }

    [Fact]
    public void LegacyJsonWithoutFlag_DeserializesAsFalse()
    {
        var parsed = JsonSerializer.Deserialize<FemNonlinearResult>("""{"Status":"ok","Steps":[]}""");

        Assert.False(parsed!.MemberLoadsLumped);
    }
}
