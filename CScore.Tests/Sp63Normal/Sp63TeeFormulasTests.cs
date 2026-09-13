using CScore.Sp63.Normal;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>Численные проверки формул таврового сечения (п. 8.1.11 СП 63).</summary>
public sealed class Sp63TeeFormulasTests
{
    const double Rb = 14_500.0;
    const double Rs = 435_000.0;
    const double Bw = 0.4;
    const double BfEff = 2.5;
    const double Hf = 0.2;
    const double H0 = 0.65;
    const double APrime = 0.05;

    [Fact]
    public void CaseA_NeutralAxisInFlange_UsesFlangeWidth()
    {
        double x = Sp63NormalFormulas.BendingX(Rs, 0.004, Rs, 0.001, Rb, BfEff);
        double moment = Sp63NormalFormulas.BendingMoment(Rb, BfEff, x, H0, Rs,
            0.001, APrime);

        Assert.True(Sp63TeeFormulas.IsNeutralAxisInFlange(Rs, 0.004, Rs, 0.001,
            Rb, BfEff, Hf));
        Assert.Equal(0.036, x, precision: 12);
        Assert.Equal(1085.76, moment, precision: 6);
    }

    [Fact]
    public void CaseB_NeutralAxisInWeb_UsesEquations87And88()
    {
        bool inFlange = Sp63TeeFormulas.IsNeutralAxisInFlange(Rs, 0.02, Rs, 0.002,
            Rb, BfEff, Hf);
        double x = Sp63TeeFormulas.BendingX_CaseB(Rs, 0.02, Rs, 0.002, Rb, Bw,
            BfEff, Hf);
        double moment = Sp63TeeFormulas.BendingMoment_CaseB(Rb, Bw, BfEff, Hf,
            x, H0, Rs, 0.002, APrime);

        Assert.False(inFlange);
        Assert.Equal(0.3, x, precision: 12);
        Assert.Equal(4741.5, moment, precision: 6);
    }

    [Fact]
    public void BoundaryXEqualsHf_AgreesByBothFormulas()
    {
        double area = Rb * BfEff * Hf / Rs;

        double xFlange = Sp63NormalFormulas.BendingX(Rs, area, 0.0, 0.0, Rb, BfEff);
        double xWeb = Sp63TeeFormulas.BendingX_CaseB(Rs, area, 0.0, 0.0, Rb, Bw,
            BfEff, Hf);

        Assert.True(Sp63TeeFormulas.IsNeutralAxisInFlange(Rs, area, 0.0, 0.0,
            Rb, BfEff, Hf));
        Assert.Equal(Hf, xFlange, precision: 12);
        Assert.Equal(Hf, xWeb, precision: 12);
    }

    [Fact]
    public void SymmetricBranch_X0UsesFlangeWidth_AndXOverTwoAprime()
    {
        double x = Sp63NormalFormulas.BendingX(Rs, 0.005, Rs, 0.005, Rb, BfEff);
        double x0 = Sp63NormalFormulas.BendingX(Rs, 0.005, 0.0, 0.0, Rb, BfEff);
        double moment = Sp63NormalFormulas.SymmetricMoment(Rs, 0.005, H0, APrime,
            x0, compressionRebarWasExcluded: true);

        Assert.Equal(0.0, x, precision: 12);
        Assert.Equal(0.06, x0, precision: 12);
        Assert.Equal(1348.5, moment, precision: 6);
    }

    [Fact]
    public void EffectiveFlangeWidth_ClampsBySpanAndOverhang()
    {
        Assert.Equal(2.5, Sp63TeeFormulas.EffectiveFlangeWidth(Bw, 2.5, Hf, 0.8, 6.3),
            precision: 12);
        Assert.Equal(1.4, Sp63TeeFormulas.EffectiveFlangeWidth(Bw, 2.5, Hf, 0.8, 3.0),
            precision: 12);
        Assert.Equal(0.4, Sp63TeeFormulas.EffectiveFlangeWidth(Bw, 2.5, 0.03, 0.8, 6.3),
            precision: 12);
    }

    [Fact]
    public void EffectiveFlangeWidth_RatioBoundariesAreInclusive()
    {
        Assert.Equal(0.64, Sp63TeeFormulas.EffectiveFlangeWidth(Bw, 2.5, 0.04, 0.8, 6.3),
            precision: 12);
        Assert.Equal(1.36, Sp63TeeFormulas.EffectiveFlangeWidth(Bw, 2.5, 0.08, 0.8, 6.3),
            precision: 12);
    }

    [Fact]
    public void EffectiveFlangeWidth_NarrowOrEqualFlange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Sp63TeeFormulas.EffectiveFlangeWidth(Bw, Bw, Hf, 0.8, 6.3));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Sp63TeeFormulas.EffectiveFlangeWidth(Bw, 0.2, Hf, 0.8, 6.3));
    }

    [Fact]
    public void CaseB_FlangeNotWiderThanWeb_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Sp63TeeFormulas.BendingX_CaseB(Rs, 0.02, Rs, 0.002, Rb, Bw, Bw, Hf));
    }

    [Fact]
    public void CaseB_InvalidSectionGeometry_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Sp63TeeFormulas.BendingMoment_CaseB(Rb, Bw, BfEff, Hf,
                x: 0.0, h0: Hf, Rs, asC: 0.0, APrime));
    }

    [Fact]
    public void EffectiveFlangeWidth_StaysWithinBounds()
    {
        for (double span = 1.0; span <= 10.0; span += 0.5)
        {
            double actual = Sp63TeeFormulas.EffectiveFlangeWidth(Bw, 2.5, Hf, 0.8, span);
            Assert.InRange(actual, Bw, 2.5);
        }
    }
}
