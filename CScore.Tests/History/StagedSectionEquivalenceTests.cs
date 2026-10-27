using Xunit;

namespace CScore.Tests.History;

public class StagedSectionEquivalenceTests
{
    static readonly Kurvature K1 = new() { e0 = -2.0e-4, ky = 6.0e-4, kz = 0.0 };
    static readonly Kurvature Inc = new() { e0 = -1.5e-4, ky = 9.0e-4, kz = 2.0e-5 };

    /// <summary>
    /// Тест 4 спеки: при двух частях формула K − K_1 совпадает с прежней TwoStageSection
    /// (baseK + Stage1Kurvature). Эталон снят с живого TwoStageSection 26.10 до его удаления.
    /// </summary>
    [Theory]
    [InlineData(CalcType.C, false, -625.2005080512656, 71.2971624777695, 0.2444305280710602)]
    [InlineData(CalcType.N, true, -722.944160902291, 96.18296774217215, 0.31262461127513236)]
    public void TwoParts_MatchLegacyTwoStageSection(CalcType calc, bool ten, double n, double mx, double my)
    {
        var staged = StagedTestSections.TwoPartBeam();
        staged.ActivatePart(1, K1);
        var f = staged.Integral(Inc + K1, calc, ten);
        Assert.Equal(n, f.N, 6);
        Assert.Equal(mx, f.Mx, 6);
        Assert.Equal(my, f.My, 6);
    }

    /// <summary>Тест 3 спеки: включение части не меняет усилий, напряжения новой части нулевые.</summary>
    [Fact]
    public void Activation_DoesNotChangeForces_NewPartUnstressed()
    {
        var s = StagedTestSections.TwoPartBeam();
        var before = s.Integral(K1, CalcType.C, ten: false);
        s.ActivatePart(1, K1);
        var after = s.Integral(K1, CalcType.C, ten: false);

        Assert.Equal(before.N, after.N, 9);
        Assert.Equal(before.Mx, after.Mx, 9);
        Assert.Equal(before.My, after.My, 9);

        s.SetEps(K1, CalcType.C, ten: false);
        foreach (var area in s.Parts[1].Areas)
        {
            foreach (var f in area.Fibers) Assert.Equal(0.0, f.Sig, 12);
            if (area.Hull != null)
                foreach (var pt in area.Hull.Points) Assert.Equal(0.0, pt.Sig, 12);
        }
    }
}
