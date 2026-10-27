using CScore.History;
using CScore.Hysteresis;
using Xunit;

namespace CScore.Tests.History;

public class LoadHistoryHysteresisTests
{
    static LoadHistoryOptions Opt(bool hyst, params PartRoles[] roles) => new()
    {
        Calc = CalcType.C, Ten = false, Tol = 0.01, MaxIter = 100, Roles = roles,
        Failure = new FailureOptions { Steps = 30 },
        Hysteresis = hyst ? new HysteresisOptions() : null,
    };

    static StagedSection Beam() => StagedTestSections.TwoPartBeam(nx: 6, ny: 20);

    [Fact]
    public void Compression_NoUnloading_SameAsElasticFibers()
    {
        HistoryStage[] st = [new("сжатие", StageKind.Load, null, new Load { N = -1500 }, 6)];
        var a = new LoadHistorySolver(Beam(), Opt(true)).Run(st, false).WithHistory;
        var plainSection = Beam(); plainSection.StrainMode = StageStrainMode.Fibers; plainSection.PrepareFiberMode();
        var b = new LoadHistorySolver(plainSection, Opt(false)).Run(st, false).WithHistory;
        for (int i = 0; i < a.Steps.Count; i++)
            Assert.Equal(b.Steps[i].K.e0, a.Steps[i].K.e0, 9);
    }

    [Fact]
    public void LoadUnload_LeavesResidualCurvature_OnlyWithHysteresis()
    {
        HistoryStage[] st =
        [
            new("нагрузка", StageKind.Load, null, new Load { Mx = -158 }, 10),
            new("разгрузка", StageKind.Load, null, new Load { Mx = 0 }, 8),
        ];
        var h = new LoadHistorySolver(Beam(), Opt(true)).Run(st, false).WithHistory;
        var e = new LoadHistorySolver(Beam(), Opt(false)).Run(st, false).WithHistory;
        Assert.Equal("ok", h.Status);
        // цель — пластика стали: в конце нагрузки ε арматуры за εy = Rs/Es ≈ 0,0022
        Assert.True(h.Steps[9].Parts[0].RebarEpsMax > 0.0022, $"ε арматуры {h.Steps[9].Parts[0].RebarEpsMax}");
        Assert.True(Math.Abs(h.Steps[^1].K.ky) > 5e-5, $"остаточная кривизна {h.Steps[^1].K.ky}");
        Assert.True(Math.Abs(e.Steps[^1].K.ky) < 1e-6);
        Assert.True(h.Steps[^1].Parts[0].RebarResidual > 0 || h.Steps[^1].Parts[0].ConcreteResidual > 0);
        // сжатая арматура: разгрузка — в сторону уменьшения |σ| (шаг «назад» давал σ/(b·E) и εост ~ 1e5)
        Assert.All(h.Steps.SelectMany(x => x.Parts), p => Assert.InRange(p.RebarResidual, 0, 0.01));
    }

    /// <summary>Частичная разгрузка в пределах упругого ядра билинейной стали и линии разгрузки бетона:
    /// повторное нагружение возвращает в ту же точку (у Menegotto–Pinto кривая скруглена — для этой проверки не годится).</summary>
    [Fact]
    public void Reload_ReturnsToUnloadingPoint()
    {
        HistoryStage[] st =
        [
            // у билинейной стали текучесть позже, чем у скруглённой MP: при −158 ε ≈ 0,00216 < εy
            new("1", StageKind.Load, null, new Load { Mx = -162 }, 8),
            new("2", StageKind.Load, null, new Load { Mx = -60 }, 5),
            new("3", StageKind.Load, null, new Load { Mx = -162 }, 5),
        ];
        var o = Opt(true);
        o.Hysteresis!.Steel = SteelHysteresisModel.Bilinear;
        var h = new LoadHistorySolver(Beam(), o).Run(st, false).WithHistory;
        Assert.True(h.Steps[7].Parts[0].RebarEpsMax > 0.0022, $"ε арматуры {h.Steps[7].Parts[0].RebarEpsMax}");
        Assert.Equal(h.Steps[7].K.ky, h.Steps[^1].K.ky, 4); // вернулись в ту же точку (повторное нагружение по линии разгрузки)
    }

    [Fact]
    public void FailsConcrete_DoesNotRecover()
    {
        var roles = new PartRoles(1, PartLimitRole.Fails, PartLimitRole.Limits);
        HistoryStage[] st =
        [
            new("1", StageKind.Load, null, new Load { Mx = -60 }, 3),
            new("2", StageKind.Load, 1, new Load { Mx = -100 }, 3),
            new("р", StageKind.Failure, null, new Load { Mx = -1 }, 1),
        ];
        var s = Beam();
        var run = new LoadHistorySolver(s, Opt(true, roles)).Run(st, false).WithHistory;
        Assert.DoesNotContain(run.Steps, x => x.Warnings.Contains("fails_beyond_limit"));
        // после завершения: σ волокон бетона части 1 при текущей плоскости и при повторной пробе — ноль у разрушенных
        var last = run.Steps[^1];
        s.SetEps(last.K, CalcType.C, false);
        Assert.Contains(s.Parts[1].Areas.Where(a => a.Material?.Type == MatType.Concrete).SelectMany(a => a.Fibers), f => f.Sig == 0);
    }

    [Fact]
    public void Replay_ReproducesSolverStresses()
    {
        HistoryStage[] st =
        [
            new("1", StageKind.Load, null, new Load { Mx = -120 }, 6),
            new("2", StageKind.Load, 1, new Load { Mx = -20 }, 6),
        ];
        var src = Beam();
        var opt = Opt(true);
        var res = new LoadHistorySolver((StagedSection)src.CloneForCalc(), opt).Run(st, false).WithHistory;
        var steps = res.Steps.Select(x => new LoadHistorySolver.ReplayStep(x.K, x.ActivePartCount, false)).ToList();
        int i = steps.Count - 1;
        var a = LoadHistorySolver.Replay(src, opt, steps, res.ActivationPlanes, withHistory: true, upTo: i);
        var f = a.Integral(res.Steps[i].K, CalcType.C, false);
        Assert.Equal(res.Steps[i].Result.Mx, f.Mx, 6);
        Assert.Equal(res.Steps[i].Result.N, f.N, 6);
    }

    /// <summary>
    /// По волокнам с ролью «Разрушается» N(e0) при фиксированной c пилообразна, и Ньютон этапа разрушения
    /// останавливается, хотя равновесие есть; при гистерезисе его продолжает брекетинг по e0.
    /// </summary>
    [Theory]
    [InlineData(40)]
    [InlineData(120)]
    public void Failure_WithFailsRoleInFibers_ReachesDropOrLimit(int failureSteps)
    {
        var roles = new PartRoles(1, PartLimitRole.Fails, PartLimitRole.Limits);
        HistoryStage[] st =
        [
            new("1", StageKind.Load, null, new Load { Mx = -60 }, 4),
            new("2", StageKind.Load, 1, new Load { Mx = -110 }, 5),
            new("р", StageKind.Failure, null, new Load { Mx = -1 }, 1),
        ];
        var o = Opt(true, roles);
        o.Tol = 0.1;
        o.Failure.Steps = failureSteps;
        var run = new LoadHistorySolver(Beam(), o).Run(st, false).WithHistory;
        Assert.NotEqual("failure_not_converged", run.StopReason);
    }

    [Fact]
    public void SignReversal_Passes()
    {
        var s = StagedTestSections.SymmetricBeam(nx: 6, ny: 20);
        HistoryStage[] st =
        [
            new("−", StageKind.Load, null, new Load { Mx = -60 }, 6),
            new("+", StageKind.Load, null, new Load { Mx = 60 }, 12),
            new("−", StageKind.Load, null, new Load { Mx = -60 }, 12),
        ];
        var run = new LoadHistorySolver(s, Opt(true)).Run(st, false).WithHistory;
        Assert.Equal("ok", run.Status);
        Assert.Equal(60, run.Steps[17].Result.Mx, 0);
    }
}
