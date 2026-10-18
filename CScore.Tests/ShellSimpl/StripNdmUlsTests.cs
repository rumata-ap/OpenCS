using CScore.ShellSimpl;
using Xunit;
using Xunit.Abstractions;
using static CScore.Tests.ShellSimpl.StripNdmFixture;

namespace CScore.Tests.ShellSimpl;

/// <summary>ПС1 полосы по лучу усилий: <see cref="StripNdm.UltimateFactor"/>.</summary>
public sealed class StripNdmUlsTests(ITestOutputHelper output)
{
    static readonly StripNdmLimits Lim = Limits();

    static double FormulaEta(double n, double m)
    {
        var c = Concrete().chars[CalcType.C];
        var r = Rebar().chars[CalcType.C];
        // Полоса грани, растянутой моментом: h0 = h − 0,03, a' = 0,03.
        return ShellSimplSolver.ComputeStripUls(Math.Abs(m), n, H, H / 2 + Z, H / 2 - Z, AsPerMeter, AsPerMeter, c, r).Eta;
    }

    // 18.10.2026: η НДМ L2 = 0,4968, L3 = 0,4960 против формульной 0,5034 (−1,3…−1,5 %). Предел —
    // раздавливание сжатой (нижней) грани, шарнир B: у слабоармированной полосы x ≈ 25 мм, и к
    // εb2 = 0,0035 растянутая арматура доходит лишь до εs ≈ 0,016 < εs,ult = 0,025.
    [Theory]
    [InlineData(DiagrammType.L2, 0.4963, 0.4973)]
    [InlineData(DiagrammType.L3, 0.4955, 0.4965)]
    public void PureBending_CloseToFormulaEta(DiagrammType type, double lo, double hi)
    {
        var uls = StripNdm.UltimateFactor(Strip(CalcType.C, type), 0.0, 20.0, Lim);
        double formula = FormulaEta(0.0, 20.0);
        output.WriteLine($"{type}: η НДМ = {uls.Eta:F4}, η формулы = {formula:F4}, шарнир {uls.Pivot}, верх {uls.TopFace}");
        Assert.Equal(StripNdmPivot.B, uls.Pivot);
        Assert.False(uls.TopFace);
        Assert.InRange(uls.Eta, lo, hi);
        Assert.InRange(formula, 0.5029, 0.5039);
        double es = uls.Ultimate!.EpsAt(Z);
        Assert.InRange(es, 0.01, 0.025);
    }

    [Theory]
    [InlineData(0.0, 20.0)]
    [InlineData(0.0, -15.0)]
    [InlineData(-800.0, 20.0)]
    [InlineData(-1500.0, -10.0)]
    [InlineData(100.0, 15.0)]
    [InlineData(250.0, 3.0)]
    public void UltimateState_LiesOnBoundaryAndOnRay(double n, double m)
    {
        var s = Strip(CalcType.C);
        var uls = StripNdm.UltimateFactor(s, n, m, Lim);
        output.WriteLine($"N={n}, M={m}: λ = {uls.Lambda:F4}, η = {uls.Eta:F4}, шарнир {uls.Pivot}, верх {uls.TopFace}");
        Assert.NotNull(uls.Ultimate);

        // Одна из деформаций — на пределе.
        Assert.Equal(1.0, StripNdm.StrainUtilization(s, uls.Ultimate!, Lim), 1e-4);

        // Усилия предельного состояния лежат на луче λ·(N, M).
        var f = s.Integrate(uls.Ultimate!.Eps0, uls.Ultimate.Kappa, false);
        double scale = Math.Abs(n) * H / 6 + Math.Abs(m);
        Assert.Equal(uls.Lambda * n * H / 6, f.N * H / 6, 1e-6 * scale * uls.Lambda);
        Assert.Equal(uls.Lambda * m, f.M, 1e-6 * scale * uls.Lambda);

        // Ниже предела НДС существует и деформации в пределах; выше — нет либо за пределами.
        var below = StripNdm.Solve(s, 0.99 * uls.Lambda * n, 0.99 * uls.Lambda * m, false);
        Assert.True(below.Converged, below.Reason);
        Assert.True(StripNdm.StrainUtilization(s, below, Lim) <= 1.0);
        var above = StripNdm.Solve(s, 1.02 * uls.Lambda * n, 1.02 * uls.Lambda * m, false);
        Assert.True(!above.Converged || StripNdm.StrainUtilization(s, above, Lim) > 1.0);
    }

    [Fact]
    public void Pivots_ForCompressionAndTensionCases()
    {
        var s = Strip(CalcType.C);
        var tension = StripNdm.UltimateFactor(s, 100.0, 15.0, Lim);
        Assert.Equal(StripNdmPivot.A, tension.Pivot);
        Assert.True(tension.TopFace);   // растянут верхний ряд

        var compression = StripNdm.UltimateFactor(s, -1500.0, 10.0, Lim);
        Assert.Contains(compression.Pivot, new[] { StripNdmPivot.B, StripNdmPivot.C });
        Assert.True(compression.TopFace == false);   // M > 0 сжимает низ
    }

    [Fact]
    public void CentralCompression_LambdaIsSquashLoadWithEpsB0()
    {
        var s = Strip(CalcType.C);
        var uls = StripNdm.UltimateFactor(s, -2000.0, 0.0, Lim);
        // Равномерное сжатие −εb0 = −0,002: бетон на Rb, арматура 200 000·0,002 = 400 МПа < Rs.
        double nult = 14_500.0 * H + 2 * 400_000.0 * AsPerMeter;
        Assert.Equal(StripNdmPivot.C, uls.Pivot);
        Assert.Equal(nult / 2000.0, uls.Lambda, 1e-6);
    }

    [Fact]
    public void ZeroLoad_EtaIsZero()
    {
        var uls = StripNdm.UltimateFactor(Strip(CalcType.C), 0.0, 0.0, Lim);
        Assert.Equal(0.0, uls.Eta);
        Assert.Equal(StripNdmPivot.None, uls.Pivot);
    }

    // Переход внецентренного растяжения к сквозному: при росте M и неизменной растягивающей N η
    // меняется плавно, без скачка, который давала граница e0 = (h0 − a')/2 в формульной полосе.
    [Fact]
    public void Eta_IsContinuousAcrossThroughTensionTransition()
    {
        var s = Strip(CalcType.C);
        double prev = double.NaN;
        double maxJump = 0;
        for (double m = 0.0; m <= 20.0001; m += 0.25)
        {
            double eta = StripNdm.UltimateFactor(s, 200.0, m, Lim).Eta;
            if (!double.IsNaN(prev)) maxJump = Math.Max(maxJump, Math.Abs(eta - prev));
            prev = eta;
        }
        output.WriteLine($"наибольший скачок η на шаге 0,25 кН·м/м: {maxJump:F4}");
        Assert.True(maxJump < 0.03, $"скачок {maxJump}");
    }
}
