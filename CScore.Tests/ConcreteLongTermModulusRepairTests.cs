using Xunit;

namespace CScore.Tests;

/// <summary>
/// Ремонт характеристик NL бетона из ошибочного справочника: Eb,τ = Eb/(1+φb,cr),
/// п. 6.1.15 СП 63 (обращение пользователя 27.09.2026, B30, влажность 40–75 %).
/// </summary>
public sealed class ConcreteLongTermModulusRepairTests
{
    const double EbB30 = 32.5e6;               // кПа
    const double EbTauB30 = 32.5e6 / 3.3;      // φb,cr = 2,3 → 9,848 ГПа

    static MaterialChars N() => new()
    {
        Type = MatType.Concrete, TypeCalc = CalcType.N, Class = 30,
        Fc = -22000, Ft = 1750, E = EbB30
    };

    static MaterialChars Nl(double e, double ec1, double et1, Dampness d = Dampness.от40_до70) => new()
    {
        Type = MatType.Concrete, TypeCalc = CalcType.NL, Class = 30, Dampness = d,
        Fc = -22000, Ft = 1750, E = e, Ec1 = ec1, Et1 = et1
    };

    [Fact]
    public void HeavyCatalogBug_FixesModulusKeepsEps()
    {
        // Справочник: E = Rb/εb1 (16,41 ГПа), εb1 верная.
        var nl = Nl(16414141.41, -0.001340308, 0.000106615);

        Assert.True(ConcreteLongTermModulusRepair.TryRepair(N(), nl));

        Assert.Equal(EbTauB30, nl.E, 1);
        Assert.Equal(0.6 * -22000 / EbTauB30, nl.Ec1, 9);
        Assert.Equal(0.6 * 1750 / EbTauB30, nl.Et1, 9);
    }

    [Fact]
    public void HeavyBugAfterDampnessRefresh_FixesModulusAndEps()
    {
        // Случай из обращения: смена влажности пересчитала εb1, εbt1 по неверному модулю.
        var nl = Nl(16414141.41, -0.0008041846155875174, 6.39692307853707E-05);

        Assert.True(ConcreteLongTermModulusRepair.TryRepair(N(), nl));

        Assert.Equal(EbTauB30, nl.E, 1);
        Assert.Equal(-0.00134030769, nl.Ec1, 9);
        Assert.Equal(0.00010661538, nl.Et1, 9);
    }

    [Fact]
    public void FineGrainedSwappedHumidity_Fixed()
    {
        // Мелкозернистый, «ниже 40 %», но φb,cr взят из строки «выше 75 %» (1,6 вместо 3,2).
        var n = N();
        double wrong = EbB30 / 2.6;
        var nl = Nl(wrong, 0.6 * -22000 / wrong, 0.6 * 1750 / wrong, Dampness.ниже_40);

        Assert.True(ConcreteLongTermModulusRepair.TryRepair(n, nl));

        Assert.Equal(EbB30 / 4.2, nl.E, 1);
        Assert.Equal(0.6 * -22000 / (EbB30 / 4.2), nl.Ec1, 9);
    }

    [Theory]
    [InlineData(Dampness.ниже_40, 3.2)]
    [InlineData(Dampness.от40_до70, 2.3)]
    [InlineData(Dampness.свыше_70, 1.6)]
    public void CorrectModulus_Untouched(Dampness d, double phi)
    {
        double e = EbB30 / (1 + phi);
        var nl = Nl(e, 0.6 * -22000 / e, 0.6 * 1750 / e, d);

        Assert.False(ConcreteLongTermModulusRepair.TryRepair(N(), nl));
        Assert.Equal(e, nl.E);
    }

    [Fact]
    public void ManuallyEditedModulus_Untouched()
    {
        var nl = Nl(12e6, -0.0011, 0.0000875);

        Assert.False(ConcreteLongTermModulusRepair.TryRepair(N(), nl));
        Assert.Equal(12e6, nl.E);
    }

    [Fact]
    public void ManuallyEditedEps_KeptWhenModulusFixed()
    {
        var nl = Nl(16414141.41, -0.0015, 0.00012);

        Assert.True(ConcreteLongTermModulusRepair.TryRepair(N(), nl));

        Assert.Equal(EbTauB30, nl.E, 1);
        Assert.Equal(-0.0015, nl.Ec1);
        Assert.Equal(0.00012, nl.Et1);
    }

    [Fact]
    public void NonConcreteOrAnyDampness_Untouched()
    {
        var rebar = Nl(16414141.41, 0, 0);
        rebar.Type = MatType.ReSteelF;
        Assert.False(ConcreteLongTermModulusRepair.TryRepair(N(), rebar));

        var any = Nl(16414141.41, -0.001340308, 0.000106615, Dampness.any);
        Assert.False(ConcreteLongTermModulusRepair.TryRepair(N(), any));
    }

    [Fact]
    public void MaterialCharsList_FindsNAndNl()
    {
        var nl = Nl(16414141.41, -0.001340308, 0.000106615);
        var chars = new List<MaterialChars> { N(), nl };

        Assert.True(ConcreteLongTermModulusRepair.TryRepair(chars));
        Assert.Equal(EbTauB30, nl.E, 1);
    }
}
