using Xunit;

namespace CScore.Tests;

/// <summary>
/// η (п. 8.1.15 СП63.13330) в LimitForceSolver.MomentFactor — при фиксированном
/// N (единственный режим, где η пока реализована для поиска предельных усилий,
/// см. CalcTaskPropsDialog.SupportsEta) η пересчитывается на каждой пробной
/// точке бисекции и усиливает пробный момент перед проверкой вместимости сечения.
/// </summary>
public class LimitForceSolverEtaTests
{
    static LimitForceParams FormulaEta(double l0) => new()
    {
        EtaEnabled = true,
        EtaIterative = false,
        EtaL = l0,
        EtaMuX = 1.0,
        EtaMuY = 1.0,
        EtaPsiX = 1.0,
        EtaPsiY = 1.0,
    };

    [Fact]
    public void MomentFactor_EtaEnabled_ReducesRawMomentCapacity_ForSlenderColumn()
    {
        // l0x/hx = 10/0.6 ≈ 16.7 > 14 — гибко, η должна применяться.
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.3, 0.6);

        var baseline = LimitForceSolver.ForCrossSection(section, CalcType.C)
            .MomentFactor(n: -800, mx: 10, my: 0);
        Assert.True(baseline.Converged);
        Assert.Null(baseline.Eta);

        var withEta = LimitForceSolver.ForCrossSection(section, CalcType.C, etaParams: FormulaEta(10))
            .MomentFactor(n: -800, mx: 10, my: 0);
        Assert.True(withEta.Converged);
        Assert.NotNull(withEta.Eta);
        Assert.True(withEta.Eta!.Value.X.Slender);
        Assert.True(withEta.Eta.Value.X.Eta > 1.0);

        // Усиление пробного момента перед проверкой вместимости → раньше
        // достигается предел → найденный (неусиленный) предельный момент меньше.
        Assert.True(withEta.MxLimit < baseline.MxLimit,
            $"MxLimit с η ({withEta.MxLimit}) должен быть меньше базового ({baseline.MxLimit})");
    }

    [Fact]
    public void MomentFactor_EtaEnabled_NotSlender_MatchesBaseline()
    {
        // l0x/hx = 3/0.6 = 5 ≤ 14 — не гибко, η не применяется (η=1) и результат
        // должен совпасть с обычным (безеta) поиском.
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.3, 0.6);

        var baseline = LimitForceSolver.ForCrossSection(section, CalcType.C)
            .MomentFactor(n: -800, mx: 10, my: 0);

        var withEta = LimitForceSolver.ForCrossSection(section, CalcType.C, etaParams: FormulaEta(3))
            .MomentFactor(n: -800, mx: 10, my: 0);

        Assert.NotNull(withEta.Eta);
        Assert.False(withEta.Eta!.Value.X.Slender);
        Assert.Equal(baseline.MxLimit, withEta.MxLimit, precision: 3);
    }

    [Fact]
    public void MomentFactor_EtaDisabled_LeavesResultEtaNull()
    {
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.3, 0.6);
        var res = LimitForceSolver.ForCrossSection(section, CalcType.C,
                etaParams: new LimitForceParams { EtaEnabled = false })
            .MomentFactor(n: -800, mx: 10, my: 0);

        Assert.Null(res.Eta);
    }

    [Fact]
    public void AllFactor_IgnoresEtaParams_Phase1Scope()
    {
        // AllFactor/AxialFactor (N — искомая величина) пока не поддерживают η —
        // передача etaParams не должна влиять на результат (Phase 2, отдельно).
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.3, 0.6);

        var baseline = LimitForceSolver.ForCrossSection(section, CalcType.C)
            .AllFactor(n: -800, mx: 10, my: 0);
        var withEta = LimitForceSolver.ForCrossSection(section, CalcType.C, etaParams: FormulaEta(10))
            .AllFactor(n: -800, mx: 10, my: 0);

        Assert.Null(withEta.Eta);
        Assert.Equal(baseline.MxLimit, withEta.MxLimit, precision: 3);
    }
}
