using CScore.Hysteresis;
using Xunit;

namespace CScore.Tests.History;

public class StagedHysteresisTests
{
    static StagedSection Section()
    {
        var s = StagedTestSections.TwoPartBeam(nx: 6, ny: 20);
        s.EnableHysteresis(new HysteresisOptions(), CalcType.C, _ => null, 0.0005);
        return s;
    }

    [Fact]
    public void Enable_SwitchesToFibersMode()
    {
        var s = Section();
        Assert.True(s.HysteresisEnabled);
        Assert.Equal(StageStrainMode.Fibers, s.StrainMode);
        Assert.All(s.Parts.SelectMany(p => p.Areas).Where(MaterialArea.IsCalcActive), a => Assert.NotEmpty(a.Fibers));
    }

    [Fact]
    public void FirstLoading_EqualsElasticNonlinear()
    {
        // До первой фиксации состояние начальное: пробный интеграл = огибающая = нелинейно-упругий расчёт по волокнам.
        var h = Section();
        var plain = StagedTestSections.TwoPartBeam(nx: 6, ny: 20);
        plain.StrainMode = StageStrainMode.Fibers;
        plain.PrepareFiberMode();
        var k = new Kurvature { e0 = -2e-4, ky = 8e-4 };
        var a = h.Integral(k, CalcType.C, ten: false);
        var b = plain.Integral(k, CalcType.C, ten: false);
        Assert.Equal(b.N, a.N, 6); Assert.Equal(b.Mx, a.Mx, 6); Assert.Equal(b.My, a.My, 6);
    }

    [Fact]
    public void Integral_IsPure_CommitChangesResponse()
    {
        var s = Section();
        // арматура при y = −0,2 течёт (ε = 0,0024 > εy): после разгрузки в K = 0 в ней остаётся σ
        var k1 = new Kurvature { ky = -1.2e-2 };
        var f1 = s.Integral(k1, CalcType.C, false);
        Assert.Equal(f1.Mx, s.Integral(k1, CalcType.C, false).Mx, 12);   // без фиксации повтор тот же
        s.CommitState(k1, false);
        var f0 = s.Integral(new Kurvature(), CalcType.C, false);         // разгрузка в ноль кривизны
        Assert.True(Math.Abs(f0.N) > 1 || Math.Abs(f0.Mx) > 0.5, "после пластики при K = 0 остаются усилия");
        s.ResetHistory();
        Assert.Equal(0.0, s.Integral(new Kurvature(), CalcType.C, false).Mx, 9);
    }

    [Fact]
    public void SetEps_WritesFiberStresses_FromState()
    {
        var s = Section();
        var k = new Kurvature { ky = -3e-3 };
        var f = s.Integral(k, CalcType.C, false);
        s.SetEps(k, CalcType.C, false);
        double mx = s.Parts.Take(s.ActivePartCount).SelectMany(p => p.Areas).Where(MaterialArea.IsCalcActive)
            .SelectMany(a => a.Fibers).Sum(fb => fb.Mx);
        Assert.Equal(f.Mx, mx, 6);
    }
}
