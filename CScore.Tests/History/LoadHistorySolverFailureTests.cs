using CScore.History;
using Xunit;

namespace CScore.Tests.History;

public class LoadHistorySolverFailureTests
{
    static Load L(double n, double mx) => new() { N = n, Mx = mx };
    static LoadHistoryOptions Opt(params PartRoles[] roles) =>
        new() { Calc = CalcType.C, Ten = false, Tol = 0.01, MaxIter = 100, Roles = roles,
                Failure = new FailureOptions { Steps = 60 } };

    /// <summary>Тест 5 спеки: одна часть, чистый изгиб — M_lim совпадает с limit_moment (0,5 %).</summary>
    [Fact]
    public void SinglePart_MLim_MatchesLimitForceSolver()
    {
        var s = StagedTestSections.SinglePart();
        var res = new LoadHistorySolver(s, Opt()).Run(
            [
                new HistoryStage("1", StageKind.Load, null, L(0, -50), 2),
                new HistoryStage("разрушение", StageKind.Failure, null, L(0, -1), 1),
            ], compareWithoutHistory: false);
        Assert.NotNull(res.WithHistory.MLim);

        var plain = new CrossSection { Areas = s.Parts[0].Areas };
        var lim = LimitForceSolver.ForCrossSection(plain, CalcType.C, ten: false).MomentFactor(n: 0, mx: -50, my: 0);
        Assert.True(lim.Converged);
        Assert.Equal(Math.Abs(lim.MxLimit), res.WithHistory.MLim!.Value, Math.Abs(lim.MxLimit) * 0.005);
        Assert.All(res.WithHistory.Steps.Where(st => st.Kind == StageKind.Failure),
            st => Assert.Equal(0.0, st.Result.N, 1));
    }

    [Fact]
    public void Failure_GoesPastPeak_StopsByDrop_OrCMax()
    {
        var s = StagedTestSections.SinglePart();
        var res = new LoadHistorySolver(s, Opt()).Run(
            [
                new HistoryStage("1", StageKind.Load, null, L(0, -50), 1),
                new HistoryStage("разрушение", StageKind.Failure, null, L(0, -1), 1),
            ], compareWithoutHistory: false);
        Assert.Contains(res.WithHistory.StopReason, new[] { "drop", "c_max" });
        Assert.NotNull(res.WithHistory.MPeak);
        Assert.True(res.WithHistory.MPeak >= res.WithHistory.MLim);
        Assert.Single(res.WithHistory.Steps, st => st.IsLimitPoint);
        // Шаги этапа разрушения (вместе с точкой предела) идут по порядку: номера 1..n, кривизна растёт.
        var failure = res.WithHistory.Steps.Where(st => st.Kind == StageKind.Failure).ToList();
        Assert.Equal(Enumerable.Range(1, failure.Count), failure.Select(st => st.Step));
        Assert.True(failure.Zip(failure.Skip(1)).All(p => p.Second.C > p.First.C));
    }

    /// <summary>Тест 6 спеки: «Разрушается» у бетона части 0 не уменьшает M_lim.</summary>
    [Fact]
    public void FailsRole_OnPartZeroConcrete_DoesNotReduceMLim()
    {
        HistoryStage[] stages =
        [
            new("1", StageKind.Load, null, L(0, -60), 2),
            new("усиление", StageKind.Load, 1, L(0, -80), 2),
            new("разрушение", StageKind.Failure, null, L(0, -1), 1),
        ];
        var norm = new LoadHistorySolver(StagedTestSections.TwoPartBeam(), Opt()).Run(stages, false);
        var fails = new LoadHistorySolver(StagedTestSections.TwoPartBeam(),
            Opt(new PartRoles(0, PartLimitRole.Fails, PartLimitRole.Limits))).Run(stages, false);
        Assert.NotNull(norm.WithHistory.MLim);
        Assert.NotNull(fails.WithHistory.MLim);
        Assert.True(fails.WithHistory.MLim >= norm.WithHistory.MLim * 0.999);
        // бетон части 0 предел не задаёт (арматура части 0 по-прежнему может)
        Assert.False(fails.WithHistory.GoverningPart == 0 && fails.WithHistory.GoverningMaterial == "concrete");
    }

    /// <summary>Тест 8 спеки (качественный): пики близки, кривизна с историей больше.</summary>
    [Fact]
    public void WithAndWithoutHistory_PeaksClose_CurvatureLarger()
    {
        var res = new LoadHistorySolver(StagedTestSections.TwoPartBeam(), Opt()).Run(
            [
                new HistoryStage("1", StageKind.Load, null, L(0, -100), 4),
                new HistoryStage("усиление", StageKind.Load, 1, L(0, -120), 2),
                new HistoryStage("разрушение", StageKind.Failure, null, L(0, -1), 1),
            ], compareWithoutHistory: true);
        double pW = res.WithHistory.MPeak!.Value, pN = res.WithoutHistory!.MPeak!.Value;
        Assert.InRange(pW / pN, 0.9, 1.05);
        var lastLoadW = res.WithHistory.Steps.Last(st => st.Kind == StageKind.Load);
        var lastLoadN = res.WithoutHistory.Steps.Last(st => st.Kind == StageKind.Load);
        Assert.True(Math.Abs(lastLoadW.C) > Math.Abs(lastLoadN.C));
    }
}
