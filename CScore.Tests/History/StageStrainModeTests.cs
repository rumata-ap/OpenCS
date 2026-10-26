using CScore.History;
using Xunit;

namespace CScore.Tests.History;

public class StageStrainModeTests
{
    static HistoryStage[] Stages =>
    [
        new("1", StageKind.Load, null, new Load { Mx = -80 }, 4),
        new("усиление", StageKind.Load, 1, new Load { Mx = -140 }, 4),
        new("разрушение", StageKind.Failure, null, new Load { Mx = -1 }, 1),
    ];

    static LoadHistoryOptions Opt() => new() { Calc = CalcType.C, Ten = false, Tol = 0.01, MaxIter = 100,
        Failure = new FailureOptions { Steps = 40 } };

    /// <summary>Тест 7 спеки: режимы «плоскость включения» и «по волокнам» совпадают с точностью сетки.</summary>
    [Fact]
    public void FibersMode_MatchesPlaneMode_WithinMeshAccuracy()
    {
        var plane = new LoadHistorySolver(StagedTestSections.TwoPartBeam(), Opt()).Run(Stages, false);

        var sf = StagedTestSections.TwoPartBeam();
        sf.StrainMode = StageStrainMode.Fibers;
        sf.PrepareFiberMode();
        var fibers = new LoadHistorySolver(sf, Opt()).Run(Stages, false);

        var lp = plane.WithHistory.Steps.Where(s => s.Kind == StageKind.Load).ToList();
        var lf = fibers.WithHistory.Steps.Where(s => s.Kind == StageKind.Load).ToList();
        Assert.Equal(lp.Count, lf.Count);
        for (int i = 0; i < lp.Count; i++)
            Assert.Equal(lp[i].K.ky, lf[i].K.ky, Math.Abs(lp[i].K.ky) * 0.02);
        Assert.Equal(plane.WithHistory.MLim!.Value, fibers.WithHistory.MLim!.Value, plane.WithHistory.MLim.Value * 0.02);
    }

    [Fact]
    public void FibersMode_StoresActivationStrainInFibers()
    {
        var s = StagedTestSections.TwoPartBeam();
        s.StrainMode = StageStrainMode.Fibers;
        s.PrepareFiberMode();
        var k1 = new Kurvature { e0 = -1e-4, ky = 2e-3 };
        s.ActivatePart(1, k1);
        foreach (var f in s.Parts[1].Areas.SelectMany(a => a.Fibers))
            Assert.Equal(k1.e0 + k1.ky * f.Y + k1.kz * f.X, f.Eps_stage, 15);
        Assert.All(s.Parts[0].Areas.SelectMany(a => a.Fibers), f => Assert.Equal(0.0, f.Eps_stage));

        var c = (StagedSection)s.CloneForCalc(); // клон посреди истории сохраняет начальные деформации
        Assert.All(c.Parts[1].Areas.SelectMany(a => a.Fibers),
            f => Assert.Equal(k1.e0 + k1.ky * f.Y + k1.kz * f.X, f.Eps_stage, 15));

        s.ResetHistory();
        Assert.All(s.Parts[1].Areas.SelectMany(a => a.Fibers), f => Assert.Equal(0.0, f.Eps_stage));
    }
}
