using CScore.Fem;
using CScore.ShellSimpl;
using Xunit;
using Xunit.Abstractions;
using static CScore.Tests.ShellSimpl.StripNdmFixture;

namespace CScore.Tests.ShellSimpl;

/// <summary>ПС2 полосы по деформационной модели: <see cref="StripNdm.CrackWidth"/>.</summary>
public sealed class StripNdmSlsTests(ITestOutputHelper output)
{
    const double Es = 200_000_000.0;
    const double RbtSer = 1_550.0;
    static readonly double[] Ds2 = [Ds, Ds];

    static StripNdmSls Sls(double n, double m, SigmaSCrcMethod method = SigmaSCrcMethod.ReleasedConcrete8137) =>
        StripNdm.CrackWidth(Strip(CalcType.N), n, m, Ds2, Es, 1.0, 0.5, method);

    // Упругий момент сопротивления приведённого сечения для растянутой верхней грани (п. 8.2.12,
    // αs = Es/Eb) и эксцентриситет ядра ex = Wred/Ared — как в ShellSimplSolver.ComputeStripSls.
    static (double Wred, double Ex) Elastic()
    {
        double h0 = H / 2 + Z, a = H / 2 - Z, alpha = Es / 30_000_000.0;
        ShellSimplSolver.FullSectionProps(H, h0, a, AsPerMeter, AsPerMeter, alpha, out double ared, out double ired);
        double sred = H * H / 2 + alpha * AsPerMeter * h0 + alpha * AsPerMeter * a;
        double yt = H - sred / ared;
        double w = ired / yt;
        return (w, w / ared);
    }

    // γ_экв = (M_crc + N·ex)/(Rbt,ser·Wred): тот γ, при котором формула п. 8.2.10–8.2.12 дала бы
    // M_crc деформационной модели. Дорожная карта трещиностойкости пластин: 1,81 при изгибе.
    // 18.10.2026 для этой полосы (L3, N-диаграммы): 1,724 при N = 0; 1,584 / 1,401 / 1,088 при
    // N = 100 / 200 / 350 кН/м (при 350 сечение трещит от одной продольной силы, M_crc = 0).
    [Fact]
    public void Mcrc_Bending_EquivalentGammaAbout1_8_AndDecreasesWithTension()
    {
        var (w, ex) = Elastic();
        double prev = double.MaxValue;
        foreach (double n in new[] { 0.0, 100.0, 200.0, 350.0 })
        {
            var r = Sls(n, 40.0);
            double gamma = (r.Mcrc + n * ex) / (RbtSer * w);
            output.WriteLine($"N = {n}: M_crc = {r.Mcrc:F3} кН·м/м, γ_экв = {gamma:F3}, h_bt = {r.HBt * 1000:F1} мм");
            if (n == 0.0) Assert.InRange(gamma, 1.70, 1.90);
            Assert.True(gamma < prev);
            prev = gamma;
        }
    }

    [Fact]
    public void BelowMcrc_NoCrack()
    {
        var r = Sls(0.0, 5.0);
        Assert.False(r.Cracked);
        Assert.Equal(0.0, r.AcrcMm);
        Assert.True(r.Mcrc > 5.0);
    }

    [Fact]
    public void CrackingMomentVariant8138_PsiFromMomentRatio()
    {
        var r = Sls(0.0, 30.0, SigmaSCrcMethod.CrackingMoment8138);
        var top = r.Rows[0];
        Assert.True(r.Cracked);
        Assert.Equal(1.0 - 0.8 * r.Mcrc / 30.0, top.PsiS, 9);
    }

    // Одноосный случай (только Mx): слоистая модель с деформационным M_crc (пробник
    // ShellCrackingSolver) должна дать ту же ширину раскрытия до погрешности интегрирования по слоям.
    [Theory]
    [InlineData(0.0, 30.0)]
    [InlineData(-100.0, 32.0)]
    [InlineData(80.0, 25.0)]
    public void Acrc_MatchesLayeredModel_Uniaxial(double n, double m)
    {
        var concrete = Concrete();
        var rebar = Rebar();
        var plate = Plate(layers: 200);
        var cDiag = concrete.GetDiagramms(DiagrammType.L3)![CalcType.N];
        var rDiag = rebar.GetDiagramms(DiagrammType.L2)![CalcType.N];

        var solver = new ShellStrainSolver(plate, cDiag, rDiag, tolRes: 1e-7, tensionOverride: false);
        var res = solver.Solve([n, 0, 0, m, 0, 0]);
        Assert.True(res.Converged);
        var probe = new ShellCrackingSolver(plate, cDiag, rDiag).CachedProbe();
        var shell = new ShellLoadItem { Nx = n, Mx = m };
        var layered = ShellLayeredCrackWidth.ComputeAll(plate, shell, res.StrainState,
            concrete.chars[CalcType.N], rebar.chars[CalcType.N], 1.0, 0.5,
            SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63, probe);
        var lTop = layered.Single(x => x.Direction == "x" && x.IsTop);

        var r = Sls(n, m);
        var top = r.Rows[0];
        output.WriteLine($"N={n}, M={m}: НДМ полосы M_crc={r.Mcrc:F3}, σs={top.SigmaS / 1000:F2}, σs,crc={top.SigmaSCrc / 1000:F2}, " +
                         $"ψs={top.PsiS:F4}, ls={top.LsM * 1000:F1}, acrc={top.AcrcMm:F4}");
        output.WriteLine($"          слоистая     M_crc={lTop.Mcrc:F3}, σs={lTop.SigmaS / 1000:F2}, σs,crc={lTop.SigmaSCrc / 1000:F2}, " +
                         $"ψs={lTop.PsiS:F4}, ls={lTop.LsM * 1000:F1}, acrc={lTop.AcrcMm:F4}");

        Assert.True(r.Cracked && lTop.Cracked);
        Assert.Equal(lTop.Mcrc, r.Mcrc, 0.01 * lTop.Mcrc);
        Assert.Equal(lTop.SigmaS, top.SigmaS, 0.01 * lTop.SigmaS);
        Assert.Equal(lTop.AcrcMm, top.AcrcMm, 0.02 * lTop.AcrcMm);
    }

    [Fact]
    public void ThroughTension_AcrcForBothRows()
    {
        var r = Sls(300.0, 5.0);
        Assert.True(r.Cracked);
        Assert.Equal(0.0, r.State!.CompressionHeight(H));
        Assert.All(r.Rows, row => Assert.True(row.AcrcMm > 0.0));
        Assert.True(r.Rows[0].AcrcMm > r.Rows[1].AcrcMm);   // момент догружает верхний ряд
        Assert.Equal(r.Rows.Max(x => x.AcrcMm), r.AcrcMm);
        Assert.All(r.Rows, row => Assert.Equal(1.2, row.Phi3));
    }

    [Fact]
    public void NegativeMoment_CracksBottomFace()
    {
        var r = Sls(0.0, -30.0);
        Assert.False(r.CrackFaceTop);
        Assert.True(r.Rows[1].AcrcMm > 0.0);
        Assert.Equal(0.0, r.Rows[0].AcrcMm);
    }
}
