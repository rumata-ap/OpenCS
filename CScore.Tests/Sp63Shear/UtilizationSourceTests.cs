using CScore.Sp63Shear;
using Xunit;

namespace CScore.Tests.Sp63Shear;

/// <summary>
/// Итоговый коэффициент использования против коэффициента по точным проверкам.
/// Упрощённые условия (8.60) и (8.63′) дают нижнюю оценку несущей способности и
/// нередко жёстче точного расчёта — вердикт по ним идёт в запас, но должен быть отличим.
/// </summary>
public sealed class UtilizationSourceTests
{
    [Fact]
    public void UtilizationExact_IgnoresSimplifiedConditions()
    {
        var input = new ShearInclinedInput(
            B: 0.30, H0: 0.55, Rb: 14_500.0, Rbt: 1_050.0,
            Qsw: 115.4, Sw: 0.15, Ns: 535.9,
            Kind: ElementKind.BendingUnstressed, AnchorageFactor: 1.0,
            StationStep: 0.0, ProjectionStep: 0.0,
            MomentZoneLength: 0.0, BarCutoffs: [], CheckMoment: true, PhiNOverride: null);
        var geometry = new InclinedSectionGeometryPair(Side(true), Side(false));
        var profile = new ConstantProfile(q: 150.0, m: -120.0, n: 0.0, supportDistance: 0.0);

        var result = ShearInclinedChecker.Check(input, profile, geometry, direction: -1);

        double simplified = result.Details.Single(d => d.Formula == "8.60").Ratio;
        double exact = result.Details.Single(d => d.Formula == "8.56").Ratio;

        Assert.True(simplified > exact);                       // упрощённое условие жёстче
        Assert.Equal(result.Utilization, simplified, 9);        // вердикт — по нему, в запас
        Assert.True(result.UtilizationExact < result.Utilization);
        Assert.Equal(0.666, exact, 3);                          // контрольный пример
    }

    static InclinedSectionGeometry Side(bool tensionOnPositive) => new(
        B: 0.30, H0: 0.55, Ns: 535.9, As: 0.001232, Rb: 14_500.0, Rbt: 1_050.0,
        Ab: 0.18, AsTotal: 0.001232, Eb: 30_000_000.0, Eb0: 0.002, Ebt0: 0.0001,
        Plane: ShearPlane.Vy, TensionOnPositiveSide: tensionOnPositive, Warnings: []);
}
