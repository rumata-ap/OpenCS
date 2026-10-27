using CScore.Hysteresis;
using Xunit;

namespace CScore.Tests.Hysteresis;

public class BilinearSteelTests
{
    const double E = 2e8, Fy = 400000, B = 0.01, Ey = Fy / E;

    static double Go(IHysteresisModel m, ref HystState s, double eps)
    { double sig = m.Trial(s, eps, true, out _, out var n); s = n; return sig; }

    [Fact]
    public void Monotonic_ElasticThenHardening()
    {
        var m = new BilinearSteel(E, Fy, Fy, B);
        var s = m.Initial;
        Assert.Equal(0.5 * Fy, Go(m, ref s, 0.5 * Ey), 1e-6);
        Assert.Equal(Fy + B * E * (3 * Ey - Ey), Go(m, ref s, 3 * Ey), 1e-3);
        m.Trial(s, 3 * Ey + 1e-6, true, out double et, out _);
        Assert.Equal(B * E, et, 1e-3);
    }

    [Fact]
    public void Reversal_ElasticRangeIsTwoFy_Kinematic()
    {
        var m = new BilinearSteel(E, Fy, Fy, B);
        var s = m.Initial;
        double top = Go(m, ref s, 4 * Ey);
        // упругая разгрузка на 2fy/E: в точке 4εy − 2εy напряжение top − 2·fy... без упрочнения внутри ядра
        Assert.Equal(top - 2 * Fy + 1e-6 * 0, Go(m, ref s, 4 * Ey - 2 * Ey), 1e-3);
        // дальше — текучесть сжатия по сдвинутой линии с наклоном bE
        double s1 = Go(m, ref s, 4 * Ey - 2 * Ey - Ey);
        Assert.Equal(top - 2 * Fy - B * E * Ey, s1, 1e-2);
    }

    [Fact]
    public void ClosedLoop_ReturnsToSameStress()
    {
        var m = new BilinearSteel(E, Fy, Fy, B);
        var s = m.Initial;
        double a = Go(m, ref s, 3 * Ey);
        Go(m, ref s, -3 * Ey);
        double b = Go(m, ref s, 3 * Ey);
        Assert.Equal(a, b, 1e-3);
    }

    [Fact]
    public void Asymmetric_YieldInCompressionAtFyC()
    {
        var m = new BilinearSteel(E, Fy, 0.5 * Fy, 0);
        var s = m.Initial;
        Assert.Equal(-0.5 * Fy, Go(m, ref s, -2 * Ey), 1e-3);
    }
}
