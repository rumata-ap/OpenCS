using CScore.History;
using OpenCS.LoadHistory;
using Xunit;

namespace OpenCS.Tests;

public sealed class LoadHistoryCurveBuilderTests
{
    static HistoryStepDto S(int stage, string kind, int parts, double c, double mu, bool limit = false) =>
        new() { Stage = stage, Kind = kind, Parts = parts, C = c, Mu = mu, LimitPoint = limit, Converged = true };

    [Fact]
    public void Build_CurvesActivationsLimitAndPeak()
    {
        var d = new LoadHistoryResultData
        {
            Steps = [S(0, "load", 1, 1e-3, 30), S(0, "load", 1, 2e-3, 60), S(1, "load", 2, 2.5e-3, 80),
                     S(2, "failure", 2, 5e-3, 120, limit: true), S(2, "failure", 2, 8e-3, 130), S(2, "failure", 2, 9e-3, 100)],
            StepsNoHistory = [S(0, "load", 2, 5e-4, 30), S(2, "failure", 2, 6e-3, 128)],
            With = new RunSummaryDto { MLim = 120, MPeak = 130 },
            Without = new RunSummaryDto { MPeak = 128 },
        };
        var c = LoadHistoryCurveBuilder.Build(d);
        Assert.Equal(7, c.WithHistory.Count);                  // + начало (0, 0)
        Assert.Equal((0.0, 0.0), (c.WithHistory[0].C, c.WithHistory[0].M));
        Assert.Equal(-1, c.WithHistory[0].StepIndex);
        var act = Assert.Single(c.Activations);
        Assert.Equal(2, act.StepIndex);                        // первый шаг с Parts = 2
        Assert.Equal(120, c.LimitWith!.M);
        Assert.Equal(130, c.PeakWith!.M);
        Assert.Null(c.LimitWithout);
        Assert.Equal(128, c.PeakWithout!.M);
    }
}
