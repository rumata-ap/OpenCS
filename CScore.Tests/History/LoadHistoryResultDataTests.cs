using CScore.History;
using Xunit;

namespace CScore.Tests.History;

public class LoadHistoryResultDataTests
{
    [Fact]
    public void Build_Parse_RoundTrip()
    {
        var s = StagedTestSections.TwoPartBeam();
        HistoryStage[] stages =
        [
            new("1", StageKind.Load, null, new Load { Mx = -60 }, 2),
            new("усиление", StageKind.Load, 1, new Load { Mx = -100 }, 2),
            new("разрушение", StageKind.Failure, null, new Load { Mx = -1 }, 1),
        ];
        var o = new LoadHistoryOptions { Calc = CalcType.C, Ten = false, Tol = 0.01, MaxIter = 100,
            Failure = new FailureOptions { Steps = 30 },
            Roles = [new PartRoles(1, PartLimitRole.Fails, PartLimitRole.Limits)] };
        var r = new LoadHistorySolver(s, o).Run(stages, compareWithoutHistory: true);

        var data = LoadHistoryResultData.Build(r, s, stages, o);
        var json = data.ToJson();
        Assert.Contains("\"m_lim\"", json);
        var back = LoadHistoryResultData.Parse(json);

        Assert.Equal(r.WithHistory.Steps.Count, back.Steps.Count);
        Assert.Equal(r.WithoutHistory!.Steps.Count, back.StepsNoHistory!.Count);
        Assert.Equal(["балка", "надбетонка"], back.PartTags);
        Assert.Equal(["1", "усиление", "разрушение"], back.StageTags);
        Assert.Equal(r.WithHistory.MLim, back.With.MLim);
        Assert.Equal(r.WithHistory.ActivationPlanes[1].ky, back.ActivationPlanes[1][1], 12);
        Assert.Equal("failure", back.Steps[^1].Kind);
        Assert.Single(back.Steps, st => st.LimitPoint);
        Assert.Equal(2, back.Steps[^1].PartStates.Count);
        var role = Assert.Single(back.NonStandardRoles);
        Assert.Equal(PartLimitRole.Fails, role.Concrete);
        Assert.Equal("plane", back.StrainMode);
        Assert.Equal(r.Phi, back.Phi, 12);
    }

    [Fact]
    public void Parse_Broken_ReturnsErrorObject() =>
        Assert.NotNull(LoadHistoryResultData.Parse("{ broken").Error);
}
