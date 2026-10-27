using CScore.History;
using Xunit;

namespace CScore.Tests.History;

/// <summary>
/// Продолжение несошедшегося шага по нагрузке. На двухчастной балке при ten = true шаги Mx = −40 и
/// −80…−100 попадают на провал момента после трещинообразования (L2): StrainSolver их не решает.
/// </summary>
public class LoadHistoryFallbackTests
{
    static HistoryStage[] Stages =>
    [
        new("1", StageKind.Load, null, new Load { Mx = -60 }, 3),
        new("2", StageKind.Load, 1, new Load { Mx = -120 }, 3),
    ];

    static LoadHistoryOptions Opt(LoadStepFallback fb) =>
        new() { Calc = CalcType.C, Ten = true, Tol = 0.01, MaxIter = 100, Fallback = fb };

    /// <summary>По кривизне: полное равновесие с растяжением бетона, путь доходит до конца.</summary>
    [Fact]
    public void CurvatureControl_PassesCrackingDip_WithTension()
    {
        var s = StagedTestSections.TwoPartBeam();
        var res = new LoadHistorySolver(s, Opt(LoadStepFallback.CurvatureControl)).Run(Stages, true);

        foreach (var run in new[] { res.WithHistory, res.WithoutHistory! })
        {
            Assert.Equal("ok", run.Status);
            Assert.Equal(6, run.Steps.Count);
            Assert.Contains(run.Steps, st => st.Warnings.Contains("curvature_control"));
            foreach (var st in run.Steps)
            {
                Assert.True(st.Converged);
                Assert.Equal(st.Target.N, st.Result.N, 1);
                Assert.Equal(st.Target.Mx, st.Result.Mx, 1);
            }
        }
        Assert.Equal(-120, res.WithHistory.Steps[^1].Result.Mx, 1);
    }

    /// <summary>Без растяжения бетона: проблемный шаг перерешён с ten = false, следующие — снова с растяжением.</summary>
    [Fact]
    public void WithoutConcreteTension_ResolvesStepWithoutTension()
    {
        var s = StagedTestSections.TwoPartBeam();
        var res = new LoadHistorySolver(s, Opt(LoadStepFallback.WithoutConcreteTension)).Run(Stages, false);
        var run = res.WithHistory;
        Assert.Equal("ok", run.Status);
        Assert.Equal(6, run.Steps.Count);
        var marked = run.Steps.Where(st => st.Warnings.Contains("without_concrete_tension")).ToList();
        Assert.NotEmpty(marked);
        // результат помеченного шага проинтегрирован без растяжения и равен заданному
        foreach (var st in marked) Assert.Equal(st.Target.Mx, st.Result.Mx, 1);
        Assert.Equal(-120, run.Steps[^1].Result.Mx, 1);
    }

    /// <summary>Усилие сверх несущей способности: оба способа не помогают, путь останавливается.</summary>
    [Theory]
    [InlineData(LoadStepFallback.CurvatureControl)]
    [InlineData(LoadStepFallback.WithoutConcreteTension)]
    public void BeyondCapacity_StillStops(LoadStepFallback fb)
    {
        var s = StagedTestSections.SinglePart();
        var res = new LoadHistorySolver(s, Opt(fb)).Run(
            [new HistoryStage("1", StageKind.Load, null, new Load { Mx = -5000 }, 5)], false);
        Assert.Equal("partial", res.WithHistory.Status);
        Assert.Equal("load_not_converged", res.WithHistory.StopReason);
    }
}
