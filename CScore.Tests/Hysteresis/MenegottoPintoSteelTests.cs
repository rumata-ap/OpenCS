using CScore.Hysteresis;
using Xunit;

namespace CScore.Tests.Hysteresis;

public class MenegottoPintoSteelTests
{
    const double E = 2e8, Fy = 400000, B = 0.01, Ey = Fy / E;

    static List<double> Run(IHysteresisModel m, IEnumerable<double> strains)
    {
        var s = m.Initial; var res = new List<double>();
        foreach (var e in strains) { res.Add(m.Trial(s, e, true, out _, out var n)); s = n; }
        return res;
    }

    static IEnumerable<double> Path(params double[] targets)
    {
        double cur = 0;
        foreach (var t in targets)
        {
            for (int i = 1; i <= 40; i++) yield return cur + (t - cur) * i / 40;
            cur = t;
        }
    }

    [Fact]
    public void Monotonic_ApproachesHardeningAsymptote()
    {
        var sig = Run(new MenegottoPintoSteel(E, Fy, Fy, B), Path(10 * Ey));
        Assert.Equal(Fy * (1 - B) + B * E * 10 * Ey, sig[^1], Fy * 0.01);
        Assert.InRange(sig[0], 0.2 * Fy, 0.26 * Fy); // в начале почти упруго: E·(εy/4)
    }

    [Fact]
    public void LargeR_MatchesBilinear()
    {
        var path = Path(3 * Ey, -3 * Ey, 3 * Ey).ToList();
        var mp = Run(new MenegottoPintoSteel(E, Fy, Fy, B, r0: 200, cr1: 0, cr2: 0.15), path);
        var bl = Run(new BilinearSteel(E, Fy, Fy, B), path);
        for (int i = 0; i < path.Count; i++) Assert.Equal(bl[i], mp[i], Fy * 0.02);
    }

    [Fact]
    public void SymmetricCycle_SymmetricLoop()
    {
        var sig = Run(new MenegottoPintoSteel(E, Fy, Fy, B), Path(4 * Ey, -4 * Ey, 4 * Ey, -4 * Ey));
        int n = 40;
        // пики второго цикла симметричны (в первом полуцикле сжатия ξ ≈ 0, т. к. εmin ещё 0, — он острее)
        Assert.Equal(-sig[4 * n - 1], sig[3 * n - 1], Fy * 0.01);
    }

    [Fact]
    public void Bauschinger_SofterThanBilinearAfterReversal()
    {
        var path = Path(4 * Ey, 0).ToList();
        var mp = Run(new MenegottoPintoSteel(E, Fy, Fy, B), path);
        var bl = Run(new BilinearSteel(E, Fy, Fy, B), path);
        // после разворота в точке ε = 4εy − 1,8εy билинейная ещё упруга, MP уже скругляется (по модулю меньше)
        int i = 40 + 18;
        Assert.True(mp[i] > bl[i] + 0.01 * Fy, $"MP {mp[i]} BL {bl[i]}");
    }

    [Fact]
    public void Trial_IsPure()
    {
        var m = new MenegottoPintoSteel(E, Fy, Fy, B);
        m.Trial(m.Initial, 3 * Ey, true, out _, out var s);
        var copy = s;
        for (int i = 0; i < 5; i++) m.Trial(s, -Ey * i, true, out _, out _);
        Assert.Equal(copy, s);
    }
}
