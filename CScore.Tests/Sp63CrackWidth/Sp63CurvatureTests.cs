using CScore.Sp63.CrackWidth;
using Xunit;

namespace CScore.Tests.Sp63CrackWidth;

/// <summary>Кривизна по формулам пп. 8.2.23–8.2.30 СП 63 (прямоугольное сечение).</summary>
public sealed class Sp63CurvatureTests
{
    [Theory]
    [InlineData(15, Sp63Humidity.From40To75, 3.4)]
    [InlineData(25, Sp63Humidity.From40To75, 2.5)]
    [InlineData(25, Sp63Humidity.Above75, 1.8)]
    [InlineData(25, Sp63Humidity.Below40, 3.6)]
    [InlineData(27.5, Sp63Humidity.From40To75, 2.5)]   // промежуточный — по меньшему классу
    [InlineData(80, Sp63Humidity.Above75, 1.0)]        // столбец B60–B100
    public void PhiBCr_MatchesTable612(double concreteClass, Sp63Humidity humidity, double expected) =>
        Assert.Equal(expected, Sp63Curvature.PhiBCr(concreteClass, humidity)!.Value, 9);

    [Theory]
    [InlineData(25, Sp63Humidity.Above75, 0.0024)]
    [InlineData(25, Sp63Humidity.From40To75, 0.0028)]
    [InlineData(25, Sp63Humidity.Below40, 0.0034)]
    [InlineData(100, Sp63Humidity.From40To75, 0.0028 * 170.0 / 210.0)]
    public void EpsB1RedLong_MatchesTable610(double concreteClass, Sp63Humidity humidity, double expected) =>
        Assert.Equal(expected, Sp63Curvature.EpsB1RedLong(concreteClass, humidity), 12);

    [Fact]
    public void Cracked_Example49_LongTermCurvatureByGeneralFormulas()
    {
        // Пример 49 Пособия к СП 63: плита b = 1000, h = 200, h0 = 173 мм, B15 (Eb = 24000,
        // Rb,ser = 11 МПа), A400, As = 769 мм², M = Ml = 25,5 кН·м, Mcrc = 13,51 кН·м (γ = 1,75),
        // влажность 40–75 % (αs1 = 560/Rb,ser, т.е. εb1,red = 0,0028).
        // По общим формулам: ψs = 0,5762, αs2 = 88,36, xm = 99,76 мм, Ired = 6,954·10⁸ мм⁴,
        // 1/r = 9,33·10⁻⁶ 1/мм. Пособие по приближённой формуле (4.48) с табличными φ1, φ2
        // получает 9,99·10⁻⁶ 1/мм (−6,6 %).
        var input = new Sp63CurvatureInput(1.0, 0.2, 0.173, 0.027, 769e-6, 0.0,
            24_000_000, 11_000, 200_000_000, 15, Sp63Humidity.From40To75);

        var result = Sp63Curvature.Compute(input, 25.5, 0.0, 1.0, cracked: true, 13.51, 13.51);

        Assert.NotNull(result);
        Assert.True(result!.Cracked);
        var t3 = result.Terms.Single(t => t.Index == 3);
        Assert.True(t3.LongTerm);
        Assert.Equal(0.5762, t3.PsiS, 4);
        Assert.Equal(0.09976, t3.Xm, 4);
        Assert.Equal(6.954e-4, t3.IRed, 7);
        Assert.False(t3.LimitedByUncracked);
        // ψ = 1: (1/r)1 = (1/r)2, полная кривизна равна (1/r)3.
        Assert.Equal(t3.Curvature, result.Total, 12);
        Assert.Equal(9.33e-3, result.Total, 5);
        Assert.InRange(result.Total / 9.99e-3, 0.90, 1.0);
    }

    [Fact]
    public void Uncracked_ShortTermPart_UsesReducedModulus085()
    {
        // Без арматуры (пренебрежимо малая площадь) и ψ = 0: 1/r = M / (0,85·Eb·b·h³/12).
        var input = new Sp63CurvatureInput(0.3, 0.6, 0.55, 0.05, 1e-12, 1e-12,
            30_000_000, 18_500, 200_000_000, 25, Sp63Humidity.From40To75);

        var result = Sp63Curvature.Compute(input, 50.0, 0.0, 0.0, cracked: false, 100, 100);

        double expected = 50.0 / (0.85 * 30_000_000 * 0.3 * 0.216 / 12.0);
        Assert.Equal(expected, result!.Total, 9);
        Assert.Equal(0.0, result.Terms[1].Curvature, 12);
    }

    [Fact]
    public void Uncracked_LongTermPart_UsesCreepModulus()
    {
        // ψ = 1: 1/r = M / (Eb/(1 + φb,cr)·I); B25, 40–75 % → φb,cr = 2,5.
        var input = new Sp63CurvatureInput(0.3, 0.6, 0.55, 0.05, 1e-12, 1e-12,
            30_000_000, 18_500, 200_000_000, 25, Sp63Humidity.From40To75);

        var result = Sp63Curvature.Compute(input, 50.0, 0.0, 1.0, cracked: false, 100, 100);

        double expected = 50.0 / (30_000_000 / 3.5 * 0.3 * 0.216 / 12.0);
        Assert.Equal(expected, result!.Total, 9);
        Assert.Equal(2.5, result.PhiBCr, 9);
    }

    [Theory]
    [InlineData(10.0, 9.99)]
    [InlineData(10.0, 12.0)]
    [InlineData(120.0, 30.0)]
    public void Cracked_StiffnessNotAboveUncracked(double m, double mcrc)
    {
        // П. 8.2.27: жёсткость с трещинами не более жёсткости без трещин при той же
        // продолжительности действия нагрузки (ψ = 0: (1/r)1 обоих расчётов — от полной M).
        var input = new Sp63CurvatureInput(0.3, 0.6, 0.55, 0.05, 40e-4, 5e-4,
            30_000_000, 18_500, 200_000_000, 25, Sp63Humidity.From40To75);

        var cracked = Sp63Curvature.Compute(input, m, 0.0, 0.0, cracked: true, mcrc, mcrc)!;
        var uncracked = Sp63Curvature.Compute(input, m, 0.0, 0.0, cracked: false, mcrc, mcrc)!;

        Assert.True(cracked.Terms[0].D <= uncracked.Terms[0].D + 1e-6);
        Assert.True(cracked.Total >= uncracked.Total - 1e-12);
    }

    [Fact]
    public void MissingConcreteClass_ReturnsNull()
    {
        var input = new Sp63CurvatureInput(0.3, 0.6, 0.55, 0.05, 20e-4, 5e-4,
            30_000_000, 18_500, 200_000_000, 0, Sp63Humidity.From40To75);

        Assert.Null(Sp63Curvature.Compute(input, 100.0, 0.0, 0.5, true, 30, 30));
    }
}
