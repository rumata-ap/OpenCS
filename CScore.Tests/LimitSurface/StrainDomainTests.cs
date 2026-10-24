using CScore.LimitSurface;
using Xunit;

namespace CScore.Tests.LimitSurface;

public class StrainDomainTests
{
    const double Eb0 = -0.002, Eb2 = -0.0035;

    static StrainControlPoint Conc(double x, double y, double eb0 = Eb0, double eb2 = Eb2, int area = 0) =>
        new(x, y, StrainPointKind.Concrete, eb2, double.PositiveInfinity, eb0, eb2, area);

    static StrainControlPoint Bar(double x, double y, double epsMax, int area = 1) =>
        new(x, y, StrainPointKind.Rebar, double.NegativeInfinity, epsMax, double.NaN, double.NaN, area);

    static StrainDomain Rect() => new(
    [
        Conc(-0.15, -0.25), Conc(0.15, -0.25), Conc(0.15, 0.25), Conc(-0.15, 0.25),
        Bar(-0.1, -0.2, 0.025), Bar(0.1, -0.2, 0.025),
    ]);

    const double Ebt0 = 0.0001, Ebt2 = 0.00015;

    static StrainControlPoint CrackConc(double x, double y) =>
        new(x, y, StrainPointKind.Concrete, Eb2, Ebt2, Eb0, Eb2, 0, Ebt0);

    /// <summary>Область ПС2: только бетон 0,3×0,5 с пределами растяжения.</summary>
    static StrainDomain CrackRect() => new(
    [
        CrackConc(-0.15, -0.25), CrackConc(0.15, -0.25), CrackConc(0.15, 0.25), CrackConc(-0.15, 0.25),
    ]);

    [Fact]
    public void Crack_UniformTension_LimitIsEpsBt0()
    {
        var (lam, act) = CrackRect().Boundary(1, 0, 0);
        Assert.Equal(Ebt0, lam, 12);
        Assert.Equal(1, act.Side);
        Assert.True(act.SingleSign);
    }

    [Fact]
    public void Crack_ZeroAtOppositeFace_LimitIsEpsBt2()
    {
        // v = 0.25 + y: y=−0.25 → 0, y=+0.25 → 0.5 ⇒ r = 0 ⇒ εbt,ult = εbt2
        var (lam, act) = CrackRect().Boundary(0.25, 1, 0);
        Assert.Equal(Ebt2 / 0.5, lam, 12);
        Assert.Equal(1, act.Side);
    }

    [Fact]
    public void Crack_IntermediateRatio_Interpolates854()
    {
        // v = 0.75 + y: 0.5 и 1.0 ⇒ r = 0.5 ⇒ εbt,ult = 1,5e-4 − 0,5e-4·0,5 = 1,25e-4
        var (lam, act) = CrackRect().Boundary(0.75, 1, 0);
        Assert.Equal(1.25e-4, lam * 1.0, 12);
        Assert.True(act.SingleSign);
    }

    [Fact]
    public void Crack_TwoSign_UsesEpsBt2()
    {
        var (lam, act) = CrackRect().Boundary(0, 1, 0);
        Assert.Equal(Ebt2 / 0.25, lam, 12);
        Assert.False(act.SingleSign);
        Assert.Equal(1, act.Side);
    }

    [Fact]
    public void Crack_RuleOff_UsesEpsBt2Everywhere()
    {
        var (lam, _) = CrackRect().WithSingleSignRule(false).Boundary(1, 0, 0);
        Assert.Equal(Ebt2, lam, 12);
    }

    [Fact]
    public void Crack_UniformCompression_ClosedBy853()
    {
        var (lam, act) = CrackRect().Boundary(-1, 0, 0);
        Assert.Equal(0.002, lam, 12);
        Assert.Equal(-1, act.Side);
    }

    [Fact]
    public void Uls_TensionSingleSign_FlagUnchanged()
    {
        var (_, act) = Rect().Boundary(1, 0, 0);
        Assert.False(act.SingleSign);
        Assert.Equal(StrainPointKind.Rebar, Rect().Points[act.PointIndex].Kind);
    }

    [Fact]
    public void UniformCompression_UsesEpsB0()
    {
        var (lam, act) = Rect().Boundary(-1, 0, 0);
        Assert.Equal(0.002, lam, 12);
        Assert.True(act.SingleSign);
    }

    [Fact]
    public void UniformCompression_RuleOff_UsesEpsB2()
    {
        var (lam, _) = Rect().WithSingleSignRule(false).Boundary(-1, 0, 0);
        Assert.Equal(0.0035, lam, 12);
    }

    [Fact]
    public void NeutralAxisAtFace_UsesEpsB2()
    {
        // v = −1 + 4y: y=+0.25 → 0, y=−0.25 → −2 ⇒ r = 0 ⇒ εb,ult = εb2
        var (lam, _) = Rect().Boundary(-1, 4, 0);
        Assert.Equal(0.0035 / 2, lam, 12);
    }

    [Fact]
    public void IntermediateRatio_Interpolates853()
    {
        // v = −1 + y: −1.25 и −0.75 ⇒ r = 0.6 ⇒ εb,ult = −0.0035 + 0.0015·0.6 = −0.0026
        var (lam, act) = Rect().Boundary(-1, 1, 0);
        Assert.Equal(0.0026 / 1.25, lam, 12);
        Assert.True(act.SingleSign);
        Assert.Equal(-1, act.Side);
    }

    [Fact]
    public void TwoConcreteAreas_EachOwnLimits()
    {
        var dom = new StrainDomain(
        [
            Conc(0, -0.25), Conc(0, 0.25, eb0: -0.0018, eb2: -0.0030, area: 2),
            Bar(0, -0.2, 0.025),
        ]);
        var (lam, act) = dom.Boundary(-1, 0, 0);
        Assert.Equal(0.0018, lam, 12);
        Assert.Equal(1, act.PointIndex);
    }

    [Fact]
    public void PureTension_GovernedByRebarLimitMinusPrestrain()
    {
        var dom = new StrainDomain([Conc(0, 0.25), Conc(0, -0.25), Bar(0, -0.2, 0.025 - 0.004)]);
        var (lam, act) = dom.Boundary(1, 0, 0);
        Assert.Equal(0.021, lam, 12);
        Assert.Equal(2, act.PointIndex);
        Assert.Equal(1, act.Side);
    }

    [Fact]
    public void PureTension_NoRebar_IsInfinite()
    {
        var dom = new StrainDomain([Conc(0, 0.25), Conc(0, -0.25)]);
        var (lam, act) = dom.Boundary(1, 0, 0);
        Assert.True(double.IsPositiveInfinity(lam));
        Assert.Equal(-1, act.PointIndex);
    }

    [Fact]
    public void Ctor_Throws_WithoutConcrete() =>
        Assert.Throws<ArgumentException>(() => new StrainDomain([Bar(0, 0, 0.025)]));

    [Fact]
    public void Ctor_Throws_WhenRebarLimitNotPositive() =>
        Assert.Throws<ArgumentException>(() => new StrainDomain([Conc(0, 0), Bar(0, 0, -0.001)]));

    /// <summary>λ в замкнутом виде против прямого поиска (шаг + бисекция) с независимой проверкой пределов.</summary>
    [Fact]
    public void Boundary_MatchesBruteForce_OnRandomRays()
    {
        var dom = Rect();
        var rnd = new Random(1);
        for (int it = 0; it < 500; it++)
        {
            double a = rnd.NextDouble() * 2 - 1, by = (rnd.NextDouble() * 2 - 1) * 10, bz = (rnd.NextDouble() * 2 - 1) * 10;
            var (lam, _) = dom.Boundary(a, by, bz);
            double lo = 0, hi = 1e-5;
            while (Inside(dom, hi, a, by, bz)) { lo = hi; hi *= 1.5; }
            for (int k = 0; k < 200; k++) { double m = 0.5 * (lo + hi); if (Inside(dom, m, a, by, bz)) lo = m; else hi = m; }
            Assert.Equal(lo, lam, 10);
        }
    }

    static bool Inside(StrainDomain d, double lam, double a, double by, double bz)
    {
        var eps = d.Points.Select(p => lam * (a + by * p.Y + bz * p.X)).ToArray();
        var conc = d.Points.Select((p, i) => (p, e: eps[i])).Where(t => t.p.Kind == StrainPointKind.Concrete).ToArray();
        double emax = conc.Max(t => t.e), emin = conc.Min(t => t.e);
        bool single = emax <= 0 && emin < 0;
        for (int i = 0; i < eps.Length; i++)
        {
            var p = d.Points[i];
            if (p.Kind == StrainPointKind.Concrete)
            {
                double ult = single ? p.EpsB2 - (p.EpsB2 - p.EpsB0) * (emax / emin) : p.EpsB2;
                if (eps[i] < ult) return false;
            }
            else if (eps[i] > p.EpsMax) return false;
        }
        return true;
    }
}
