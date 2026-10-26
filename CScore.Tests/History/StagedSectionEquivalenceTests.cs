using Xunit;

namespace CScore.Tests.History;

public class StagedSectionEquivalenceTests
{
    static readonly Kurvature K1 = new() { e0 = -2.0e-4, ky = 6.0e-4, kz = 0.0 };
    static readonly Kurvature Inc = new() { e0 = -1.5e-4, ky = 9.0e-4, kz = 2.0e-5 };

    /// <summary>
    /// Тест 4 спеки: при двух частях формула K − K_1 совпадает с прежней baseK + Stage1Kurvature
    /// (baseK — приращение, полная плоскость части 0 — baseK + κ1).
    /// Эталон живой, пока TwoStageSection существует; в срезе 4 заменяется зафиксированными числами.
    /// </summary>
    [Theory]
    [InlineData(CalcType.C, false)]
    [InlineData(CalcType.N, true)]
    public void TwoParts_MatchLegacyTwoStageSection(CalcType calc, bool ten)
    {
        var staged = StagedTestSections.TwoPartBeam();
        var legacy = new TwoStageSection
        {
            Stage1 = new CrossSection { Areas = staged.Parts[0].Areas.Select(a => a.CloneForCalc()).ToList() },
            Areas = staged.Parts[1].Areas.Select(a => a.CloneForCalc()).ToList(),
            Stage1Kurvature = K1,
        };
        legacy.Stage1.ResolveAndBuildDiagramms(0.85, null, rebarDifferentialDiagram: false);
        legacy.ResolveAndBuildDiagramms(0.85, null, rebarDifferentialDiagram: false);

        staged.ActivatePart(1, K1);
        var fStaged = staged.Integral(Inc + K1, calc, ten);
        var fLegacy = legacy.Integral(Inc, calc, ten);

        Assert.Equal(fLegacy.N, fStaged.N, 6);
        Assert.Equal(fLegacy.Mx, fStaged.Mx, 6);
        Assert.Equal(fLegacy.My, fStaged.My, 6);
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
