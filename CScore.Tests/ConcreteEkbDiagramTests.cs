using CScore;
using Xunit;

namespace CScore.Tests;

/// <summary>
/// Диаграмма ЕКБ (кривая Сарджина) с нисходящей ветвью по CEB-FIP MC90,
/// уравнения (2.1-18) … (2.1-21).
/// Фикстура соответствует классу C30 табл. 2.1.7 MC90 (в МПа):
/// fcm = 38 МПа, εc1 = -0.0022, Eci = 33.5·10³ МПа.
/// Код строит k = 1.05·E·εc1/Rb, поэтому E задан как Eci/1.05.
/// </summary>
public class ConcreteEkbDiagramTests
{
    const double Fcm = 38.0;      // МПа, fcm для C30 (fck + 8)
    const double Eci = 33_500.0;  // МПа, касательный модуль, табл. 2.1.7
    const double Ec1 = -0.0022;   // деформация в вершине кривой

    /// <summary>
    /// Вершина кривой: σ(εc1) = -fcm. Восходящая ветвь не меняется.
    /// </summary>
    [Fact]
    public void Peak_AtEc1_EqualsCompressiveStrength()
    {
        var d = ConcreteC30().DEKB();

        Assert.Equal(-Fcm, d.Sig(Ec1, out _), precision: 1);
    }

    /// <summary>
    /// MC90 (2.1-19): нисходящая ветвь проходит через σ = -0.5·fcm при εc,lim.
    /// Для C30 табл. 2.1.7 даёт -3.7·10⁻³.
    /// </summary>
    [Fact]
    public void DescendingBranch_ReachesHalfStrength_AtMc90TableStrain()
    {
        var d = ConcreteC30().DEKB();

        double eps = FindStrainAtStressLevel(d, 0.5);

        Assert.InRange(eps, -3.7e-3 * 1.05, -3.7e-3 * 0.95);
    }

    /// <summary>
    /// На всей ветви сжатия напряжение не должно становиться растягивающим.
    /// Ловит выброс кубического сплайна за концом кривой.
    /// </summary>
    [Fact]
    public void CompressionBranch_NeverReturnsTensileStress()
    {
        var d = ConcreteC30().DEKB();

        for (int i = 0; i <= 4000; i++)
        {
            double eps = -i * 1e-5;   // до -0.04
            double sig = d.Sig(eps, out _);
            Assert.True(sig <= 1e-9, $"σ({eps:F5}) = {sig:F3} > 0 — сжатое волокно растягивает");
        }
    }

    /// <summary>
    /// После вершины |σ| обязано только убывать — вплоть до обнуления.
    /// </summary>
    [Fact]
    public void CompressionBranch_DecreasesMonotonically_AfterPeak()
    {
        var d = ConcreteC30().DEKB();
        double prev = -d.Sig(Ec1, out _);

        for (int i = 1; i <= 4000; i++)
        {
            double eps = Ec1 - i * 1e-5;
            double sig = -d.Sig(eps, out _);
            Assert.True(sig <= prev + 1e-6,
                $"|σ| выросло на нисходящей ветви: {prev:F3} → {sig:F3} при ε={eps:F5}");
            prev = sig;
        }
    }

    /// <summary>
    /// MC90 (2.1-20) на деформации далеко за Ec2 = -3.5‰: для C30 при -5‰
    /// уравнение даёт σ ≈ 0.148·fcm. Раньше кривая здесь обрывалась.
    /// </summary>
    [Fact]
    public void DescendingBranch_FollowsMc90Equation_BeyondEc2()
    {
        var d = ConcreteC30().DEKB();

        double ratio = -d.Sig(-5.0e-3, out _) / Fcm;

        Assert.InRange(ratio, 0.12, 0.18);
    }

    /// <summary>
    /// Ветвь строится до уровня etaMin·Rb, дальше — ноль.
    /// Для C30 при etaMin = 0.05 (2.1-20) даёт ε ≈ -7.3‰; с учётом линейного
    /// завершения по касательной кривая гаснет около -10‰.
    /// </summary>
    [Fact]
    public void CompressionBranch_VanishesBeyondEtaMinLevel()
    {
        var d = ConcreteC30().DEKB();

        Assert.True(-d.Sig(-7.0e-3, out _) > 0.04 * Fcm, "до порога напряжение должно быть значимым");
        Assert.Equal(0.0, d.Sig(-0.015, out _), precision: 9);
        Assert.Equal(0.0, d.Sig(-0.030, out _), precision: 9);
    }

    /// <summary>
    /// etaMin ≥ 0.5 обрывает кривую ещё на участке Сарджина — участок (2.1-20)
    /// не строится, поскольку он начинается ровно при σ = 0.5·fcm.
    /// </summary>
    [Fact]
    public void EtaMinAboveHalf_TruncatesCurveOnSarginBranch()
    {
        var d = ConcreteC30().DEKB(etaMin: 0.6);

        Assert.Equal(0.0, d.Sig(-5.0e-3, out _), precision: 9);
        Assert.True(-d.Sig(-3.0e-3, out _) > 0.6 * Fcm, "до порога кривая должна работать");
    }

    /// <summary>
    /// Настройка расчёта доходит до диаграммы: Material.GetDiagramms передаёт
    /// ekbEtaMin в DEKB, а не строит кривую с умолчанием.
    /// </summary>
    [Fact]
    public void GetDiagramms_PassesEkbEtaMin_ToEkbDiagram()
    {
        var m = ConcreteMaterial();

        var wide   = m.GetDiagramms(DiagrammType.EKB, ekbEtaMin: 0.05)![CalcType.N];
        var narrow = m.GetDiagramms(DiagrammType.EKB, ekbEtaMin: 0.6)![CalcType.N];

        Assert.True(-wide.Sig(-5.0e-3, out _) > 0.1 * Fcm, "при 0.05 ветвь должна работать на -5‰");
        Assert.Equal(0.0, narrow.Sig(-5.0e-3, out _), precision: 9);
    }

    static Material ConcreteMaterial()
    {
        var m = new Material { Tag = "C30", Type = MatType.Concrete, E = Eci / 1.05 };
        foreach (var ct in new[] { CalcType.C, CalcType.CL, CalcType.N, CalcType.NL })
        {
            var ch = ConcreteC30();
            ch.TypeCalc = ct;
            switch (ct)
            {
                case CalcType.C:  m.C  = ch; break;
                case CalcType.CL: m.CL = ch; break;
                case CalcType.N:  m.N  = ch; break;
                default:          m.NL = ch; break;
            }
        }
        return m;
    }

    /// <summary>
    /// Находит деформацию сжатия, при которой |σ| падает до level·fcm
    /// на нисходящей ветви (после вершины).
    /// </summary>
    static double FindStrainAtStressLevel(Diagramm d, double level)
    {
        double target = level * Fcm;
        double prevEps = Ec1, prevSig = Fcm;

        for (int i = 1; i <= 200_000; i++)
        {
            double eps = Ec1 - i * 1e-7;
            double sig = -d.Sig(eps, out _);
            if (sig <= target)
                return prevEps + (prevSig - target) / (prevSig - sig) * (eps - prevEps);
            prevEps = eps; prevSig = sig;
        }
        return double.NaN;
    }

    static MaterialChars ConcreteC30() => new()
    {
        Type     = MatType.Concrete,
        TypeCalc = CalcType.N,
        Fc       = -Fcm,
        Ft       = 2.9,
        E        = Eci / 1.05,
        Ec0      = Ec1,
        Ec1      = -0.0006,
        Ec2      = -0.0035,
        Ec1Red   = -0.0015,
        Et0      = 2.9 / Eci,
        Et1      = 0.6 * 2.9 / Eci,
        Et2      = 0.00015,
        Et1Red   = 0.00008,
    };
}
