using CScore;
using CScore.Sp63Shear;
using Xunit;

namespace CScore.Tests.Sp63Shear;

/// <summary>Извлечение расчётной геометрии наклонного сечения из CrossSection.</summary>
public sealed class InclinedSectionGeometryTests
{
    [Fact]
    public void Resolve_NegativeMoment_TakesBottomReinforcementAsTension()
    {
        // Балка 0,30 × 0,60; арматура снизу на y = −0,25. Отрицательный Mx растягивает низ.
        var section = Sp63ShearFixtures.Beam(bottomRebarY: -0.25, topRebarY: 0.25);

        var geom = InclinedSectionGeometry.Resolve(section, ShearPlane.Vy, -150.0, CalcType.C);

        Assert.False(geom.TensionOnPositiveSide);
        Assert.Equal(0.30, geom.B, 9);
        Assert.Equal(0.55, geom.H0, 9);          // от сжатой грани y = +0,30 до y = −0,25
    }

    [Fact]
    public void Resolve_PositiveMoment_TakesTopReinforcementAsTension()
    {
        var section = Sp63ShearFixtures.Beam(bottomRebarY: -0.25, topRebarY: 0.25);

        var geom = InclinedSectionGeometry.Resolve(section, ShearPlane.Vy, 150.0, CalcType.C);

        Assert.True(geom.TensionOnPositiveSide);
        Assert.Equal(0.55, geom.H0, 9);          // от сжатой грани y = −0,30 до y = +0,25
    }

    [Fact]
    public void Resolve_MixedRebarGrades_SumsForcesInsteadOfAveragingStrength()
    {
        // Два стержня одинаковой площади 0,001 м², но с Rs = 435 000 и 355 000 кПа
        var section = Sp63ShearFixtures.Beam(bottomRebarY: -0.25, topRebarY: 0.25);
        Sp63ShearFixtures.AddRebar(section, x: 0.05, y: -0.25, area: 0.001, rs: 355_000.0);

        var geom = InclinedSectionGeometry.Resolve(section, ShearPlane.Vy, -150.0, CalcType.C);

        Assert.Equal(0.002, geom.As, 9);
        Assert.Equal(435_000.0 * 0.001 + 355_000.0 * 0.001, geom.Ns, 6);
    }

    [Fact]
    public void Resolve_ZeroMoment_TakesSmallerWorkingDepth()
    {
        // Арматура снизу на y = −0,25 (h0 = 0,55) и сверху на y = 0,10 (h0 = 0,40)
        var section = Sp63ShearFixtures.Beam(bottomRebarY: -0.25, topRebarY: 0.10);

        var geom = InclinedSectionGeometry.Resolve(section, ShearPlane.Vy, 0.0, CalcType.C);

        Assert.Equal(0.40, geom.H0, 9);
        Assert.Contains(geom.Warnings, w => w.Contains("нулев", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Pair_ProvidesGeometryForBothTensionSides()
    {
        var pair = InclinedSectionGeometryPair.Resolve(
            Sp63ShearFixtures.Beam(bottomRebarY: -0.25, topRebarY: 0.10), ShearPlane.Vy, CalcType.C);

        Assert.True(pair.SidesDiffer);
        Assert.Equal(0.55, pair.For(-150.0).H0, 9);      // растянут низ
        Assert.Equal(0.40, pair.For(+150.0).H0, 9);      // растянут верх
        Assert.Equal(0.40, pair.For(0.0).H0, 9);         // нулевой момент — меньшая высота
    }

    [Fact]
    public void Pair_SymmetricDepths_GiveSameWorkingDepth()
    {
        var pair = InclinedSectionGeometryPair.Resolve(
            Sp63ShearFixtures.Beam(bottomRebarY: -0.25, topRebarY: 0.25), ShearPlane.Vy, CalcType.C);

        Assert.Equal(0.55, pair.For(-150.0).H0, 9);
        Assert.Equal(0.55, pair.For(+150.0).H0, 9);
    }

    [Fact]
    public void ResolveForTensionSide_IgnoresMomentSign()
    {
        var section = Sp63ShearFixtures.Beam(bottomRebarY: -0.25, topRebarY: 0.10);

        var geom = InclinedSectionGeometry.ResolveForTensionSide(
            section, ShearPlane.Vy, tensionOnPositiveSide: true, CalcType.C);

        Assert.True(geom.TensionOnPositiveSide);
        Assert.Equal(0.40, geom.H0, 9);
    }

    [Fact]
    public void Resolve_HeterogeneousConcrete_TakesMinimumAndWarns()
    {
        var section = Sp63ShearFixtures.Beam(bottomRebarY: -0.25, topRebarY: 0.25);
        section.Areas.Add(Sp63ShearFixtures.ConcreteRegion(
            Sp63ShearFixtures.Concrete(8_500, 8_500.0, 750.0),
            [(-0.15, 0.30), (0.15, 0.30), (0.15, 0.40), (-0.15, 0.40)]));

        var geom = InclinedSectionGeometry.Resolve(section, ShearPlane.Vy, -150.0, CalcType.C);

        Assert.Equal(8_500.0, geom.Rb, 6);
        Assert.Equal(750.0, geom.Rbt, 6);
        Assert.Contains(geom.Warnings, w => w.Contains("неоднород", StringComparison.OrdinalIgnoreCase));
    }
}
