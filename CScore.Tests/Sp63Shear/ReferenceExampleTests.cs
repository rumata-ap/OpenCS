using CScore.Sp63Shear;
using Xunit;

namespace CScore.Tests.Sp63Shear;

/// <summary>Контрольный пример приёмки: балка 300×600, B25, хомуты ⌀8 A240 шагом 150 мм.</summary>
public sealed class ReferenceExampleTests
{
    const double B = 0.30, H0 = 0.55, Rb = 14_500.0, Rbt = 1_050.0;
    const double Qsw = 172_000.0 * 1.006e-4 / 0.15;      // ≈ 115,4 кН/м

    [Fact]
    public void StripCapacity_MatchesManualCalculation()
    {
        Assert.Equal(717.75, ShearFormulas.StripCapacity(Input(), phiN: 1.0, applyToStrip: false), 2);
    }

    [Fact]
    public void StirrupsSatisfyThreshold_SoFormula858Applies()
    {
        Assert.True(Qsw > 0.25 * Rbt * B);               // 115,4 > 78,75
    }

    [Fact]
    public void CriticalProjectionIsCappedByTwoWorkingDepths()
    {
        double cStar = H0 * Math.Sqrt(
            ShearFormulas.PhiB2 * Rbt * B / (ShearFormulas.PhiSw * Qsw));

        Assert.True(cStar > 2.0 * H0);                   // 1,285 > 1,10 — критическим будет 2h0
    }

    [Fact]
    public void CapacityAtTwoWorkingDepths_MatchesManualCalculation()
    {
        double c = 2.0 * H0;
        double qb = ShearFormulas.ConcreteShear(Input(), c, phiN: 1.0);
        double qsw = ShearFormulas.StirrupShear(Input(), c, phiN: 1.0, out string? note);

        Assert.Equal(129.9, qb, 1);
        Assert.Equal(95.2, qsw, 1);
        Assert.Equal(225.1, qb + qsw, 1);
        Assert.Equal(0.666, 150.0 / (qb + qsw), 3);
        Assert.Null(note);
    }

    static ShearInclinedInput Input() => new(
        B: B, H0: H0, Rb: Rb, Rbt: Rbt, Qsw: Qsw, Sw: 0.15, Ns: 535.9,
        Kind: ElementKind.BendingUnstressed, AnchorageFactor: 1.0,
        StationStep: 0.0, ProjectionStep: 0.0,
        MomentZoneLength: 0.0, BarCutoffs: [], CheckMoment: true, PhiNOverride: null);
}
