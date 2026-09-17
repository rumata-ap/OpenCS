using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>
/// Оракулы формул приложения Д СП 63.13330.2018. Значения получены независимым
/// Python-скриптом по растровым формулам Д.1–Д.9 HTML-копии нормы.
/// </summary>
public sealed class Sp63CircularFormulasTests
{
    const double Rb = 14_500.0;
    const double Rs = 340_000.0;
    static readonly double RingArea = Math.PI * (0.30 * 0.30 - 0.20 * 0.20);
    const double RingAs = 0.0016;
    static readonly double CircleArea = Math.PI * 0.25 * 0.25;
    const double CircleAs = 0.0024;

    static Sp63AnnularCapacity Ring(double n) =>
        Sp63CircularFormulas.Annular(n, Rb, Rs, Rs, RingArea, RingAs, 0.20, 0.30, 0.25);

    static Sp63CircularCapacity Circle(double n) =>
        Sp63CircularFormulas.Circular(n, Rb, Rs, CircleArea, CircleAs, 0.25, 0.20);

    [Fact]
    public void Ring_PureBending_UsesBranchB_D3()
    {
        var c = Ring(0.0);
        Assert.Equal(Sp63AnnularBranch.B, c.Branch);
        Assert.Equal(0.1452039454251791, c.XiCir, 12);
        Assert.Equal(0.14459600736433473, c.XiCir1, 12);
        Assert.Equal(138.64800365787437, c.Mult, 6);
        Assert.False(c.Overloaded);
    }

    [Fact]
    public void Ring_ModerateCompression_UsesBranchA_D2()
    {
        var c = Ring(500.0);
        Assert.Equal(Sp63AnnularBranch.A, c.Branch);
        Assert.Equal(0.2786634540880275, c.XiCir, 12);
        Assert.Equal(212.65147536768285, c.Mult, 6);
    }

    [Fact]
    public void Ring_HighCompression_UsesBranchC_D4()
    {
        var c = Ring(2000.0);
        Assert.Equal(Sp63AnnularBranch.C, c.Branch);
        Assert.Equal(0.7088039576683075, c.XiCir2, 12);
        Assert.Equal(177.9373794881961, c.Mult, 6);
        Assert.False(c.Overloaded);
    }

    [Theory]
    [InlineData(3000.0, 1.0632059365024613)]
    // ξcir2 > 2: sin(πξ) снова положителен — перегрузка обязана определяться по ξcir2.
    [InlineData(6000.0, 2.1264118730049226)]
    public void Ring_Overload_IsDetectedByXi2NotBySine(double n, double xi2)
    {
        var c = Ring(n);
        Assert.Equal(Sp63AnnularBranch.C, c.Branch);
        Assert.Equal(xi2, c.XiCir2, 12);
        Assert.True(c.Overloaded);
        Assert.Equal(0.0, c.Mult);
    }

    [Fact]
    public void Circle_PureBending_UsesD8()
    {
        var c = Circle(0.0);
        Assert.True(c.ConditionD7);
        Assert.Equal(0.2574399137106411, c.XiCir, 10);
        Assert.Equal(0.24754109519209674, c.Phi, 10);
        Assert.Equal(135.1678629239722, c.Mult, 6);
    }

    [Fact]
    public void Circle_ModerateCompression_UsesD8()
    {
        var c = Circle(800.0);
        Assert.True(c.ConditionD7);
        Assert.Equal(0.3876032177399793, c.XiCir, 10);
        Assert.Equal(213.9228641423497, c.Mult, 6);
    }

    [Fact]
    public void Circle_HighCompression_UsesD9WithZeroPhi()
    {
        var c = Circle(3000.0);
        Assert.False(c.ConditionD7);
        Assert.Equal(0.7010818380199453, c.XiCir, 10);
        Assert.Equal(0.0, c.Phi);
        Assert.Equal(121.30839861002943, c.Mult, 6);
    }

    [Fact]
    public void Circle_Overload_WhenNExceedsRbAPlusRsAs()
    {
        var c = Circle(4000.0);
        Assert.True(c.Overloaded);
        Assert.Equal(0.0, c.Mult);
        Assert.True(double.IsNaN(c.XiCir));
    }

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(800.0, true)]
    [InlineData(3000.0, false)]
    public void SolveXi_SatisfiesEquation(double n, bool d7)
    {
        double xi = Sp63CircularFormulas.SolveXi(n, Rb, Rs, CircleArea, CircleAs, d7)!.Value;
        double k = d7 ? 2.55 : 1.0;
        double c = d7 ? n + Rs * CircleAs : n;
        double rhs = (c + Rb * CircleArea * Math.Sin(2 * Math.PI * xi) / (2 * Math.PI)) /
                     (Rb * CircleArea + k * Rs * CircleAs);
        Assert.Equal(rhs, xi, 10);
    }
}
