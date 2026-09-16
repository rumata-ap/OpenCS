using System;
using System.Globalization;
using System.IO;
using Xunit;
using CScore;

namespace CScore.Tests;

// Коэффициент пластичности γ в упругопластическом моменте сопротивления Wpl = γ·Wred
// (момент образования трещин, п. 8.2.11 СП 63.13330).
//
// Источники значения γ для прямоугольного сечения:
//   СП 63.13330        — γ = 1,3;
//   СНиП 2.03.01-84*   — γ = 1,75 (Гвоздев А.А., Дмитриев С.А., 1957);
//   Радайкин О.В. 2018 — γ = 1,6 + 1/(100·√μs), μs = As/(b·h), по опытам Пирадова,
//                        Ватагина и Тошина; область применения В15…В35.
//
// Независимая точка отсчёта внутри OpenCS — деформационная модель: CrackWidthSolver ищет
// Mcrc бисекцией по реальной трёхлинейной диаграмме растянутого бетона, без всякого γ.
public class CrackingMomentGammaTests
{
    // ── Проверка 1: формула Радайкина воспроизводит значения из статьи ─────────────────────

    // Пары (μs; γ) из табл. 2 статьи. μs в таблице округлён до четырёх знаков, а γ посчитан
    // по неокруглённому, поэтому допуск 0,01 (для одного и того же табличного μs = 0,0109
    // в статье приведены и 1,696, и 1,705).
    [Theory]
    [InlineData(0.0055, 1.738)]
    [InlineData(0.0067, 1.722)]
    [InlineData(0.0109, 1.696)]
    [InlineData(0.0134, 1.686)]
    [InlineData(0.0209, 1.669)]
    [InlineData(0.0306, 1.657)]
    [InlineData(0.0481, 1.652)]
    public void RadaykinGamma_MatchesPublishedValues(double mu, double expected)
    {
        double gamma = ShellSimplSolver.ResolveWplGamma(
            WplGammaMethod.Radaykin2018, asT: mu, b: 1.0, h: 1.0);

        Assert.InRange(gamma, expected - 0.01, expected + 0.01);
    }

    // Контрольная точка автора: γ = 1,75 достигается ровно при μs = 0,00444.
    [Fact]
    public void RadaykinGamma_Equals175_AtControlReinforcementRatio()
    {
        double gamma = ShellSimplSolver.ResolveWplGamma(
            WplGammaMethod.Radaykin2018, asT: 0.00444, b: 1.0, h: 1.0);

        Assert.Equal(1.75, gamma, 3);
    }

    [Fact]
    public void Sp63AndSnipGammas_AreConstants()
    {
        Assert.Equal(1.3, ShellSimplSolver.ResolveWplGamma(WplGammaMethod.Sp63, 0.006, 1.0, 0.2), 9);
        Assert.Equal(1.75, ShellSimplSolver.ResolveWplGamma(WplGammaMethod.Snip2030184, 0.006, 1.0, 0.2), 9);
    }

    // ── Проверка 2: Mcrc полосы при разных γ против деформационной модели ──────────────────

    const double H = 0.2, H0 = 0.165, APrime = 0.035, B = 1.0;
    const double AsBot = 1131e-6, AsTop = 565.5e-6, Ds = 0.012;
    const double M = 45.831;   // длительное сочетание в критическом направлении, кН·м/м

    static ShellSimplStripResult Strip(WplGammaMethod gamma,
        SigmaSCrcMethod sigma = SigmaSCrcMethod.ReleasedConcrete8137, double phi1 = 1.4) =>
        ShellSimplSolver.ComputeStripSls(
            M_des: M, N_des: 0.0, h: H, h0: H0, a_prime: APrime,
            As_t: AsBot, As_c: AsTop, ds: Ds,
            concrete: ConcreteN(), rebar: RebarN(),
            phi1: phi1, phi2: 0.5, acrcLimMm: 0.3,
            sigmaSCrcMethod: sigma, wplGamma: gamma);

    // Mcrc по деформационной модели — бисекцией по трёхлинейной диаграмме, без γ.
    static double NdmMcrc()
    {
        var res = new CrackWidthSolver(ShellSimplExternalPlateTests.Strip(),
                calcCrc: CalcType.N, calcService: CalcType.N, calcServiceLong: CalcType.N,
                phi2: 0.5, acrcUltLong: 0.3, acrcUltShort: 0.4)
            .Compute(N: 0.0, mxLong: -M, mxTotal: -M);
        Assert.True(res.Cracked);
        return res.Mcrc;
    }

    [Fact]
    public void Sp63Gamma_UnderestimatesCrackingMoment_AgainstDeformationModel()
    {
        double mcrcSp63 = Strip(WplGammaMethod.Sp63).Mcrc;
        double mcrcNdm = NdmMcrc();

        // Формульный Mcrc при γ = 1,3 ниже деформационного примерно на четверть.
        Assert.True(mcrcSp63 < mcrcNdm);
        Assert.InRange(mcrcSp63 / mcrcNdm, 0.70, 0.75);
    }

    [Fact]
    public void Snip175AndRadaykinGammas_AreCloseToDeformationModel()
    {
        double mcrcNdm = NdmMcrc();

        foreach (var g in new[] { WplGammaMethod.Snip2030184, WplGammaMethod.Radaykin2018 })
        {
            double mcrc = Strip(g).Mcrc;
            Assert.InRange(mcrc / mcrcNdm, 0.95, 1.05);
        }
    }

    // Эквивалентная γ деформационной модели: Mcrc пропорционален γ, поэтому
    // γ_экв = 1,3 · Mcrc(НДМ)/Mcrc(γ = 1,3).
    [Fact]
    public void EquivalentGammaOfDeformationModel_MatchesPublishedTheoreticalValue()
    {
        double gammaEquiv = 1.3 * NdmMcrc() / Strip(WplGammaMethod.Sp63).Mcrc;

        // Табл. 1 статьи, В25, μ = 0,5655 % — интерполяция между 1,785 (0,5 %) и 1,813 (1 %).
        Assert.InRange(gammaEquiv, 1.75, 1.82);
    }

    // ── Проверка 3: влияние γ на ширину раскрытия трещин ───────────────────────────────────

    [Fact]
    public void Gamma_AffectsAcrc_OnlyThroughMomentRoute()
    {
        // При замыкании ψs через момент (ф. 8.138) больший γ снижает acrc.
        double acrcSp63 = Strip(WplGammaMethod.Sp63, SigmaSCrcMethod.CrackingMoment8138).Acrc_mm;
        double acrcSnip = Strip(WplGammaMethod.Snip2030184, SigmaSCrcMethod.CrackingMoment8138).Acrc_mm;
        Assert.True(acrcSnip < acrcSp63);

        // При замыкании по напряжениям (ф. 8.137) γ на acrc не влияет вовсе: σs,crc
        // определяется сбросом растянутого бетона, а не моментом образования трещин.
        double byStressSp63 = Strip(WplGammaMethod.Sp63).Acrc_mm;
        double byStressSnip = Strip(WplGammaMethod.Snip2030184).Acrc_mm;
        Assert.Equal(byStressSp63, byStressSnip, 9);
    }

    // ── Проверка 4: FEM-проверка плиты использует тот же переключатель ─────────────────────

    // У FemCheckRunner своя цепочка п. 8.2.15-8.2.18 (σs берётся из НДС пластины, а не из
    // упругой модели полосы), поэтому переключатель нужно проверять и здесь отдельно.
    [Fact]
    public void FemPlateCheck_HonoursGammaAndSigmaSCrcMethod()
    {
        const double epsS = 282.051e3 / 200_000_000.0;   // σs = 282,05 МПа

        double Acrc(SigmaSCrcMethod sigma, WplGammaMethod gamma) =>
            CScore.Fem.ShellLayeredCrackWidth.ComputeAcrcStrip(
                eps_s: epsS, M_des: M, N_des: 0.0,
                h: H, h0: H0, aPrime: APrime, As_t: AsBot, ds: Ds,
                Rbt: 1_550.0, Rb_ser: 18_500.0, Es: 200_000_000.0, Rs_ser: 500_000.0,
                Eb_red: 18_500.0 / 0.0015, alphaFull: 200_000_000.0 / 30_000_000.0,
                alpha: 200_000_000.0 / (18_500.0 / 0.0015),
                phi1: 1.4, phi2: 0.5,
                sigmaSCrcMethod: sigma, wplGamma: gamma);

        // Через момент γ влияет: больший γ — меньшая ширина раскрытия.
        double byMomentSp63 = Acrc(SigmaSCrcMethod.CrackingMoment8138, WplGammaMethod.Sp63);
        double byMomentSnip = Acrc(SigmaSCrcMethod.CrackingMoment8138, WplGammaMethod.Snip2030184);
        Assert.True(byMomentSnip < byMomentSp63);

        // По напряжениям γ не влияет вовсе.
        Assert.Equal(Acrc(SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63),
                     Acrc(SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Snip2030184), 9);

        // Обе цепочки — полосовая и FEM — на одном сечении дают одно и то же:
        // σs задан так, чтобы совпасть с упругой моделью полосы.
        Assert.Equal(Strip(WplGammaMethod.Sp63).Acrc_mm,
                     Acrc(SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63), 3);
    }

    [Fact]
    public void Dump()
    {
        string? path = Environment.GetEnvironmentVariable("GAMMA_DUMP");
        if (string.IsNullOrEmpty(path)) return;

        var ci = CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();
        double mcrcNdm = NdmMcrc();
        double mcrc13 = Strip(WplGammaMethod.Sp63).Mcrc;

        sb.AppendLine($"mu_s={(AsBot / (B * H)).ToString("F6", ci)}");
        sb.AppendLine($"Wred*Rbt = {(mcrc13 / 1.3).ToString("F5", ci)} кН·м/м");
        foreach (var g in new[] { WplGammaMethod.Sp63, WplGammaMethod.Snip2030184, WplGammaMethod.Radaykin2018 })
        {
            var byStress = Strip(g);
            var byMoment = Strip(g, SigmaSCrcMethod.CrackingMoment8138);
            sb.AppendLine($"{g}: gamma={byStress.Gamma.ToString("F4", ci)} " +
                          $"Mcrc={byStress.Mcrc.ToString("F4", ci)} " +
                          $"Mcrc/Mcrc_ндм={(byStress.Mcrc / mcrcNdm).ToString("F4", ci)} | " +
                          $"ф.8137: psi={byStress.Psi_s.ToString("F5", ci)} acrc={byStress.Acrc_mm.ToString("F5", ci)} | " +
                          $"ф.8138: psi={byMoment.Psi_s.ToString("F5", ci)} acrc={byMoment.Acrc_mm.ToString("F5", ci)}");
        }
        sb.AppendLine($"НДМ: Mcrc={mcrcNdm.ToString("F4", ci)} " +
                      $"gamma_экв={(1.3 * mcrcNdm / mcrc13).ToString("F4", ci)}");
        sb.AppendLine($"sigma_s={Strip(WplGammaMethod.Sp63).Sigma_s_MPa.ToString("F3", ci)} " +
                      $"sigma_s_crc(8137)={Strip(WplGammaMethod.Sp63).Sigma_s_crc_MPa.ToString("F3", ci)}");
        File.WriteAllText(path, sb.ToString());
    }

    static MaterialChars ConcreteN() => new(CalcType.N)
    {
        Type = MatType.Concrete, E = 30_000_000.0, Fc = -18_500.0, Ft = 1_550.0,
        Ec0 = -0.002, Ec1 = -0.6 * 18_500.0 / 30_000_000.0, Ec2 = -0.0035, Ec1Red = -0.0015,
        Et0 = 0.0001, Et1 = 0.6 * 1_550.0 / 30_000_000.0, Et2 = 0.00015, Et1Red = 0.00008,
    };

    static MaterialChars RebarN() => new(CalcType.N)
    {
        Type = MatType.ReSteelF, E = 200_000_000.0, Fc = -500_000.0, Ft = 500_000.0,
        Ec2 = -0.025, Et2 = 0.025,
    };
}
