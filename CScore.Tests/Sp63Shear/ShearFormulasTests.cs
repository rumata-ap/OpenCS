using CScore.Sp63Shear;
using Xunit;

namespace CScore.Tests.Sp63Shear;

/// <summary>Формулы прочности наклонных сечений по поперечной силе (8.55–8.62).</summary>
public sealed class ShearFormulasTests
{
    // Балка b = 0,30 м, h0 = 0,55 м, B25: Rb = 14 500 кПа, Rbt = 1 050 кПа
    const double B = 0.30;
    const double H0 = 0.55;
    const double Rb = 14_500.0;
    const double Rbt = 1_050.0;

    [Fact]
    public void StripCapacity_Formula855()
    {
        double capacity = ShearFormulas.StripCapacity(Input(), phiN: 1.0, applyToStrip: false);

        Assert.Equal(0.3 * Rb * B * H0, capacity, 6);   // 717,75 кН
    }

    [Fact]
    public void StripCapacity_CompressionAppliesPhiN()
    {
        double capacity = ShearFormulas.StripCapacity(Input(), phiN: 1.4, applyToStrip: true);

        Assert.Equal(1.4 * 0.3 * Rb * B * H0, capacity, 6);
    }

    [Fact]
    public void StripCapacity_TensionDoesNotApplyPhiN()
    {
        double capacity = ShearFormulas.StripCapacity(Input(), phiN: 0.6, applyToStrip: false);

        Assert.Equal(0.3 * Rb * B * H0, capacity, 6);
    }

    [Fact]
    public void ConcreteShear_MidRange_UsesMainFormula()
    {
        double qb = ShearFormulas.ConcreteShear(Input(), projectionC: 1.5 * H0, phiN: 1.0);

        Assert.Equal(1.5 * Rbt * B * H0 * H0 / (1.5 * H0), qb, 6);
    }

    [Fact]
    public void ConcreteShear_SmallQsw_UsesSpecialFormulaForConcrete()
    {
        var input = Input(qsw: 50.0, sw: 0.15);
        double qb = ShearFormulas.ConcreteShear(
            input, projectionC: H0, phiN: 1.0, appliedShear: 150.0);

        Assert.Equal(4.0 * 1.5 * H0 * 50.0, qb, 6);
    }

    [Fact]
    public void ConcreteShear_SmallQswWithExcessiveSpacing_UsesMainFormula()
    {
        var input = Input(qsw: 50.0, sw: 0.30);
        double qb = ShearFormulas.ConcreteShear(
            input, projectionC: H0, phiN: 1.0, appliedShear: 400.0);

        Assert.Equal(1.5 * Rbt * B * H0, qb, 6);
    }

    [Fact]
    public void ConcreteShear_SmallProjection_IsCappedByUpperLimit()
    {
        double qb = ShearFormulas.ConcreteShear(Input(), projectionC: 0.1 * H0, phiN: 1.0);

        Assert.Equal(2.5 * Rbt * B * H0, qb, 6);
    }

    [Fact]
    public void ConcreteShear_LargeProjection_IsCappedByLowerLimit()
    {
        double qb = ShearFormulas.ConcreteShear(Input(), projectionC: 10.0 * H0, phiN: 1.0);

        Assert.Equal(0.5 * Rbt * B * H0, qb, 6);
    }

    [Fact]
    public void ConcreteShear_PhiNScalesBothFormulaAndCaps()
    {
        double qb = ShearFormulas.ConcreteShear(Input(), projectionC: 0.1 * H0, phiN: 1.4);

        Assert.Equal(1.4 * 2.5 * Rbt * B * H0, qb, 6);
    }

    [Fact]
    public void StirrupShear_SufficientQsw_UsesFormula858()
    {
        var input = Input(qsw: 200.0, sw: 0.15);   // 0,25·Rbt·b = 78,75 кН/м — условие выполнено
        double qsw = ShearFormulas.StirrupShear(
            input, projectionC: H0, phiN: 1.0, out string? note, appliedShear: 150.0);

        Assert.Equal(0.75 * 200.0 * H0, qsw, 6);
        Assert.Null(note);
    }

    [Fact]
    public void StirrupShear_SmallQsw_StillUsesFormula858()
    {
        var input = Input(qsw: 50.0, sw: 0.15);    // 50 < 0,25·Rbt·b = 78,75 кН/м
        double qsw = ShearFormulas.StirrupShear(
            input, projectionC: H0, phiN: 1.0, out string? note, appliedShear: 150.0);

        Assert.Equal(0.75 * 50.0 * H0, qsw, 6);
        Assert.Contains("Qb", note);
    }

    [Fact]
    public void StirrupShear_SpacingWithinNormativeLimit_IsIncluded()
    {
        var input = Input(qsw: 200.0, sw: 0.30);   // smax = 0,635 м при Q = 150 кН
        double qsw = ShearFormulas.StirrupShear(
            input, projectionC: H0, phiN: 1.0, out string? note, appliedShear: 150.0);

        Assert.Equal(0.75 * 200.0 * H0, qsw, 6);
        Assert.Null(note);
    }

    [Fact]
    public void StirrupShear_SpacingAboveNormativeLimit_IsZero()
    {
        var input = Input(qsw: 200.0, sw: 0.30);   // smax = 0,238 м при Q = 400 кН
        double qsw = ShearFormulas.StirrupShear(
            input, projectionC: H0, phiN: 1.0, out string? note, appliedShear: 400.0);

        Assert.Equal(0.0, qsw, 12);
        Assert.Contains("s_w,max", note);
    }

    [Fact]
    public void StirrupShear_NoStirrups_IsZero()
    {
        var input = Input(qsw: 0.0, sw: 0.0);
        double qsw = ShearFormulas.StirrupShear(input, projectionC: H0, phiN: 1.0, out _);

        Assert.Equal(0.0, qsw, 12);
    }

    [Fact]
    public void MinConcreteShear_FarFromSupport_HasNoCorrection()
    {
        double value = ShearFormulas.MinConcreteShear(Input(), phiN: 1.0, supportDistance: 2.0);

        Assert.Equal(0.5 * Rbt * B * H0, value, 6);
    }

    [Fact]
    public void MinConcreteShear_CloseToSupport_IsIncreasedAndCapped()
    {
        double d = 0.5 * H0;
        double value = ShearFormulas.MinConcreteShear(Input(), phiN: 1.0, supportDistance: d);

        Assert.Equal(2.5 * Rbt * B * H0, value, 6);
    }

    [Fact]
    public void MinStirrupShear_CloseToSupport_IsScaledByDistanceRatio()
    {
        var input = Input(qsw: 200.0, sw: 0.15);
        double d = 0.5 * H0;
        double value = ShearFormulas.MinStirrupShear(
            input, supportDistance: d, out string? note, appliedShear: 200.0);

        Assert.Equal(200.0 * H0 * (d / H0), value, 6);
        Assert.Null(note);
    }

    [Fact]
    public void MinStirrupShear_AppliesPhiNToWholeRightSideOf861()
    {
        double value = ShearFormulas.MinStirrupShear(
            Input(), supportDistance: 2.0, out _, appliedShear: 150.0, phiN: 1.4);

        Assert.Equal(1.4 * 200.0 * H0, value, 6);
    }

    [Fact]
    public void MinStirrupShear_WeakStirrups_IsZeroWithNote()
    {
        // 50 < 0,25·Rbt·b = 78,75 кН/м — специальная формула меняет только Qb в (8.56)
        var input = Input(qsw: 50.0, sw: 0.15);
        double value = ShearFormulas.MinStirrupShear(input, supportDistance: 2.0, out string? note);

        Assert.Equal(0.0, value, 12);
        Assert.NotNull(note);
    }

    [Fact]
    public void MinStirrupShear_SpacingAboveNormativeLimit_IsZeroWithNote()
    {
        var input = Input(qsw: 200.0, sw: 0.30);
        double value = ShearFormulas.MinStirrupShear(
            input, supportDistance: 2.0, out string? note, appliedShear: 400.0);

        Assert.Equal(0.0, value, 12);
        Assert.Contains("s_w,max", note);
    }

    static ShearInclinedInput Input(double qsw = 200.0, double sw = 0.15) => new(
        B: B, H0: H0, Rb: Rb, Rbt: Rbt, Qsw: qsw, Sw: sw, Ns: 435.0,
        Kind: ElementKind.BendingUnstressed, AnchorageFactor: 1.0,
        StationStep: 0.0, ProjectionStep: 0.0,
        MomentZoneLength: 0.0, BarCutoffs: [], CheckMoment: true, PhiNOverride: null);
}
