using System;
using CScore.Sp63;
using Xunit;

namespace CScore.Tests;

public class EccentricityAmplifierTests
{
    [Fact]
    public void Ncr_ComputesEulerLikeFormula()
    {
        // D=1000 кН·м², l0=3 м → Ncr = π²·1000/9 ≈ 1096.62
        double ncr = EccentricityAmplifier.Ncr(1000, 3);
        Assert.Equal(1096.62, ncr, precision: 1);
    }

    [Theory]
    [InlineData(0.5, 1.5)]  // ψ=0.5 → φl=1.5
    [InlineData(1.0, 2.0)]  // ψ=1.0 → φl=2 (клэмп сверху)
    [InlineData(0.0, 1.0)]  // ψ=0   → φl=1 (клэмп снизу)
    public void PhiL_ClampsToRange(double psi, double expected)
    {
        Assert.Equal(expected, EccentricityAmplifier.PhiL(psi), precision: 6);
    }

    [Theory]
    [InlineData(0.01, 1.0, 0.15)]   // e0/h=0.01 → клэмп снизу 0.15
    [InlineData(5.0,  1.0, 1.5)]    // e0/h=5.0  → клэмп сверху 1.5
    [InlineData(0.3,  1.0, 0.3)]    // в диапазоне — без изменений
    public void DeltaE_ClampsToRange(double e0, double h, double expected)
    {
        Assert.Equal(expected, EccentricityAmplifier.DeltaE(e0, h), precision: 6);
    }

    [Fact]
    public void Kb_MatchesFormula()
    {
        // kb = 0.15/(phiL*(0.3+deltaE)) = 0.15/(2*(0.3+0.15)) = 0.15/0.9 = 0.16667
        double kb = EccentricityAmplifier.Kb(2.0, 0.15);
        Assert.Equal(0.166667, kb, precision: 5);
    }

    [Fact]
    public void Aitken_ExactOnGeometricSequence()
    {
        // x_n = 10 - 10*0.5^n сходится к 10; из трёх точек Эйткен даёт точный предел
        double x0 = 10 - 10 * Math.Pow(0.5, 0);
        double x1 = 10 - 10 * Math.Pow(0.5, 1);
        double x2 = 10 - 10 * Math.Pow(0.5, 2);
        double result = EccentricityAmplifier.Aitken(x0, x1, x2);
        Assert.Equal(10.0, result, precision: 6);
    }

    [Fact]
    public void Aitken_ReturnsNaNOnDegenerateSequence()
    {
        // Постоянные приращения (арифметическая, не геометрическая, прогрессия) →
        // знаменатель Эйткена (d2-d1) равен нулю
        double result = EccentricityAmplifier.Aitken(0, 1, 2);
        Assert.True(double.IsNaN(result));
    }

    [Fact]
    public void ShouldSkip_TrueWhenTension()
    {
        Assert.True(EccentricityAmplifier.ShouldSkip(n: 100, l0: 6, h: 0.3, out _));
    }

    [Fact]
    public void ShouldSkip_TrueWhenNotSlender()
    {
        // l0/h = 3/0.3 = 10 ≤ 14
        bool skip = EccentricityAmplifier.ShouldSkip(n: -500, l0: 3, h: 0.3, out bool slender);
        Assert.True(skip);
        Assert.False(slender);
    }

    [Fact]
    public void ShouldSkip_FalseWhenCompressedAndSlender()
    {
        // l0/h = 6/0.3 = 20 > 14
        bool skip = EccentricityAmplifier.ShouldSkip(n: -500, l0: 6, h: 0.3, out bool slender);
        Assert.False(skip);
        Assert.True(slender);
    }

    [Fact]
    public void ShouldSkip_CustomThreshold_OverridesDefault14()
    {
        // l0/h = 6/0.3 = 20 — гибко относительно нормативного порога 14, но НЕ
        // относительно пользовательского порога 25.
        bool skip = EccentricityAmplifier.ShouldSkip(n: -500, l0: 6, h: 0.3, out bool slender, threshold: 25);
        Assert.True(skip);
        Assert.False(slender);
    }

    [Fact]
    public void AmplifyFormula_CustomThreshold_SkipsBelowRaisedLimit()
    {
        // Та же гибкая колонна, что и в AmplifyFormula_AmplifiesSlenderCompressedColumn
        // (l0/h=20>14), но с пользовательским порогом 25 поправка не требуется.
        var r = EccentricityAmplifier.AmplifyFormula(
            n: -800, m0: 80, l0: 6, h: 0.3,
            eiConcrete: 175_500, eiRebar: 24_544, psi: 0.5,
            slendernessThreshold: 25);

        Assert.False(r.Slender);
        Assert.Equal(1.0, r.Eta);
        Assert.Equal(80, r.MEff);
    }

    [Fact]
    public void AmplifyIterative_CustomThreshold_SkipsBelowRaisedLimit()
    {
        var r = EccentricityAmplifier.AmplifyIterative(
            n: -800, m0: 80, l0: 6, h: 0.3, solveCurvature: m => m / 200_000,
            passes: 3, slendernessThreshold: 25);

        Assert.False(r.Slender);
        Assert.Equal(1.0, r.Eta);
        Assert.Equal(0, r.Iterations);
    }

    [Fact]
    public void AmplifyFormula_NoAmplification_WhenNotSlender()
    {
        var r = EccentricityAmplifier.AmplifyFormula(
            n: -500, m0: 50, l0: 3, h: 0.3,
            eiConcrete: 175_500, eiRebar: 24_544, psi: 1.0);

        Assert.Equal(1.0, r.Eta);
        Assert.Equal(50, r.MEff);
        Assert.False(r.Slender);
    }

    [Fact]
    public void AmplifyFormula_NoAmplification_WhenTension()
    {
        var r = EccentricityAmplifier.AmplifyFormula(
            n: 500, m0: 50, l0: 6, h: 0.3,
            eiConcrete: 175_500, eiRebar: 24_544, psi: 1.0);

        Assert.Equal(1.0, r.Eta);
    }

    [Fact]
    public void AmplifyFormula_AmplifiesSlenderCompressedColumn()
    {
        // Гибкая колонна: l0/h=6/0.3=20>14, N сжимающая, приличный эксцентриситет.
        var r = EccentricityAmplifier.AmplifyFormula(
            n: -800, m0: 80, l0: 6, h: 0.3,
            eiConcrete: 175_500, eiRebar: 24_544, psi: 0.5);

        Assert.True(r.Slender);
        Assert.True(r.Stable);
        Assert.True(r.Eta > 1.0, $"η={r.Eta} должно быть > 1");
        Assert.Equal(80 * r.Eta, r.MEff, precision: 6);
    }

    [Fact]
    public void AmplifyFormula_FlagsInstability_WhenNExceedsNcr()
    {
        // Малая жёсткость → Ncr мал → |N|>=Ncr
        var r = EccentricityAmplifier.AmplifyFormula(
            n: -800, m0: 80, l0: 6, h: 0.3,
            eiConcrete: 100, eiRebar: 10, psi: 0.5);

        Assert.False(r.Stable);
        Assert.True(double.IsPositiveInfinity(r.Eta));
    }

    [Fact]
    public void AmplifyIterative_ConvergesToClosedFormEta_ForConstantStiffness()
    {
        // Синтетический решатель: κ = M/D0 (постоянная жёсткость, без нелинейности) →
        // η должно за 3 прохода стабилизироваться и совпасть с замкнутой формулой
        // η = 1/(1-N/Ncr(D0)) с точностью, которую даёт экстраполяция.
        const double d0 = 175_500 + 24_544;
        const double n = -800, l0 = 6, h = 0.3, m0 = 80;

        double Solve(double mTrial) => mTrial / d0;

        var r = EccentricityAmplifier.AmplifyIterative(n, m0, l0, h, Solve);

        double ncrExpected = EccentricityAmplifier.Ncr(d0, l0);
        double etaExpected = 1.0 / (1.0 - Math.Abs(n) / ncrExpected);

        // D постоянна (нет нелинейности) → η уже точна после 1-го прохода,
        // последовательность (η0,η0,η0) вырождена для Эйткена (нулевые
        // приращения) — экстраполяция закономерно помечается неприменённой,
        // но итоговое η всё равно точно совпадает с замкнутой формулой.
        Assert.True(r.Stable);
        Assert.Equal(etaExpected, r.Eta, precision: 6);
        Assert.True(r.ExtrapolationFailed);
        Assert.Equal(3, r.EtaHistory.Length);
        Assert.All(r.EtaHistory, e => Assert.Equal(etaExpected, e, precision: 6));
    }

    [Fact]
    public void AmplifyIterative_NoAmplification_WhenNotSlender()
    {
        var r = EccentricityAmplifier.AmplifyIterative(
            n: -500, m0: 50, l0: 3, h: 0.3, solveCurvature: m => m / 200_000);

        Assert.Equal(1.0, r.Eta);
        Assert.Equal(0, r.Iterations);
    }

    [Fact]
    public void AmplifyIterative_FlagsInstability_WhenNExceedsNcr()
    {
        var r = EccentricityAmplifier.AmplifyIterative(
            n: -800, m0: 80, l0: 6, h: 0.3, solveCurvature: m => m / 50.0);

        Assert.False(r.Stable);
    }

    [Fact]
    public void AmplifyIterative_FallsBackToLastEta_WhenSequenceDiverges()
    {
        // Решатель, дающий немонотонно растущую жёсткость такую, что
        // приращения η не убывают (искусственно осциллирующая жёсткость) →
        // экстраполяция должна быть отклонена.
        double[] stiffness = [200_000, 190_000, 250_000];
        int call = 0;
        double Solve(double mTrial) => mTrial / stiffness[Math.Min(call++, stiffness.Length - 1)];

        var r = EccentricityAmplifier.AmplifyIterative(
            n: -800, m0: 80, l0: 6, h: 0.3, Solve);

        Assert.True(r.ExtrapolationFailed);
        Assert.Equal(3, r.EtaHistory.Length);
        // η финальный (без экстраполяции) должен совпасть с последним проходом истории
        Assert.Equal(r.EtaHistory[2], r.Eta, precision: 6);
    }
}
