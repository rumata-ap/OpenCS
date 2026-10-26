using CScore;
using CScore.History;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Продолжение несошедшегося шага «по кривизне» на сечении демопроекта (материалы из каталога, бетон L3) при
/// настройках приложения по умолчанию: после трещин M(c) пилообразна и в отдельных точках по c равновесия нет —
/// обход по кривизне должен их перешагивать, а не останавливать историю.
/// </summary>
public sealed class LoadHistoryCurvatureBridgeTests
{
    [Theory]
    [InlineData(CalcType.N)]
    [InlineData(CalcType.NL)]
    public void ServiceCalc_WithTension_PassesCracking(CalcType calc)
    {
        var (_, _, staged) = LoadHistoryDemoDbManualTests.CreateSections();
        var cs = CalcSettings.Default;
        var s = (StagedSection)staged.CloneForCalc();
        s.ResolveAndBuildDiagramms(cs.Sp63DescEtaMin, null, cs.RebarDifferentialDiagram, cs.EkbDescEtaMin);
        var o = new LoadHistoryOptions
        {
            Calc = calc, Ten = true, Tol = cs.NewtonTolerance, MaxIter = cs.NewtonMaxIter, H = cs.NewtonDeltaH,
            Fallback = LoadStepFallback.CurvatureControl,
        };
        HistoryStage[] stages =
        [
            new("Монтаж", StageKind.Load, null, new Load { Mx = -60 }, 6),
            new("Эксплуатация", StageKind.Load, 1, new Load { Mx = -110 }, 6),
        ];

        var res = new LoadHistorySolver(s, o).Run(stages, compareWithoutHistory: true);

        foreach (var run in new[] { res.WithHistory, res.WithoutHistory! })
        {
            Assert.Equal("ok", run.Status);
            Assert.Equal(12, run.Steps.Count);
            foreach (var st in run.Steps)
            {
                Assert.True(st.Converged);
                Assert.Equal(st.Target.Mx, st.Result.Mx, 0);
                Assert.Equal(st.Target.N, st.Result.N, 0);
            }
        }
    }
}
