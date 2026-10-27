using CScore.Hysteresis;
using Xunit;

namespace CScore.Tests.Hysteresis;

public class KarsanJirsaConcreteTests
{
    const double Fpc = -30000, Ec0 = -0.002, Fpcu = -6000, Ecu = -0.0035;
    static KarsanJirsaConcrete Model() => new(HystTestDiagrams.KentPark(Fpc, Ec0, Fpcu, Ecu));

    /// <summary>Прогон программы деформаций с фиксацией после каждой точки; возвращает σ.</summary>
    static List<double> Run(IHysteresisModel m, IEnumerable<double> strains, bool ten = false)
    {
        var s = m.Initial; var res = new List<double>();
        foreach (var e in strains) { res.Add(m.Trial(s, e, ten, out _, out var n)); s = n; }
        return res;
    }

    [Fact]
    public void Monotonic_FollowsEnvelope()
    {
        var m = Model();
        var env = HystTestDiagrams.KentPark(Fpc, Ec0, Fpcu, Ecu);
        var strains = Enumerable.Range(1, 50).Select(i => -0.0001 * i).ToList();
        var sig = Run(m, strains);
        for (int i = 0; i < strains.Count; i++)
            Assert.Equal(env.SigValue(strains[i], false), sig[i], 1e-6);
    }

    [Theory]
    [InlineData(-0.001)]   // η = 0,5
    [InlineData(-0.003)]   // η = 1,5
    [InlineData(-0.006)]   // η = 3 — вторая ветвь
    public void Residual_MatchesKarsanJirsa(double epsMin)
    {
        double eta = epsMin / Ec0;
        double expected = eta < 2 ? Ec0 * (0.145 * eta * eta + 0.13 * eta) : Ec0 * (0.707 * (eta - 2) + 0.834);
        Assert.Equal(expected, KarsanJirsaConcrete.ResidualStrain(epsMin, Ec0), 12);
        // разгрузка: напряжение обращается в ноль ровно в εp и линейно между εp и εmin
        var m = Model();
        var s = m.Initial;
        for (double e = -0.0001; e > epsMin; e -= 0.0001) { m.Trial(s, e, false, out _, out var n); s = n; }
        { m.Trial(s, epsMin, false, out _, out var n); s = n; }
        double sMin = s.Sig;
        Assert.Equal(0.0, m.Trial(s, expected, false, out _, out _), 1e-6);
        double mid = 0.5 * (expected + epsMin);
        Assert.Equal(0.5 * sMin, m.Trial(s, mid, false, out double et, out _), 1e-3);
        Assert.Equal(sMin / (epsMin - expected), et, 1e-3);
        Assert.Equal(0.0, m.Trial(s, 0.0005, false, out _, out _), 12);   // выше εp сжатия нет
    }

    [Fact]
    public void Reload_ReturnsToEnvelopeAtEpsMin()
    {
        var m = Model();
        var env = HystTestDiagrams.KentPark(Fpc, Ec0, Fpcu, Ecu);
        var sig = Run(m, [-0.001, -0.0015, -0.0005, 0.0, -0.0015, -0.0018]);
        Assert.Equal(env.SigValue(-0.0015, false), sig[4], 1e-3); // вернулся в точку разворота
        Assert.Equal(env.SigValue(-0.0018, false), sig[5], 1e-3); // и пошёл по огибающей
        Assert.Equal(0.0, sig[3], 12);
    }

    [Fact]
    public void Trial_DoesNotChangeCommitted()
    {
        var m = Model();
        m.Trial(m.Initial, -0.003, false, out _, out var s);
        var copy = s;
        for (int i = 0; i < 10; i++) m.Trial(s, -0.001 * i, false, out _, out _);
        Assert.Equal(copy, s);
    }

    [Fact]
    public void Tension_CracksIrreversibly_AndClosesInCompression()
    {
        var env = HystTestDiagrams.ConcreteN();
        var m = new KarsanJirsaConcrete(env);
        double et2 = env.It.X[^1];
        var s = m.Initial;
        double sUp = m.Trial(s, 0.5 * et2, true, out _, out var s1);
        Assert.True(sUp > 0);
        m.Trial(s1, 2 * et2, true, out _, out s);                             // трещина
        Assert.Equal(0.0, m.Trial(s, 0.5 * et2, true, out _, out var s2), 12); // растяжения больше нет
        Assert.True(m.Trial(s2, -0.0005, true, out _, out _) < 0);            // при сжатии работает
        Assert.Equal(0.0, m.Trial(m.Initial, 0.5 * et2, ten: false, out _, out var s3), 12);
        Assert.Equal(m.Initial, s3 with { Eps = 0, Sig = 0 });                // ten = false не трогает растяжение
    }

    [Fact]
    public void Tension_BeforeCrack_UnloadsBySecant()
    {
        var env = HystTestDiagrams.ConcreteN();
        var m = new KarsanJirsaConcrete(env);
        double et2 = env.It.X[^1];
        var s = m.Initial;
        double sTop = m.Trial(s, 0.8 * et2, true, out _, out var s1);
        s = s1;
        Assert.Equal(0.5 * sTop, m.Trial(s, 0.4 * et2, true, out _, out _), 1e-6);
    }
}
