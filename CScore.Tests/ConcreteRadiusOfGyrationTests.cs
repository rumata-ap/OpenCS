using CScore;
using CScore.Sp63;
using CScore.Tests.Sp63Normal;
using Xunit;

namespace CScore.Tests;

/// <summary>Радиусы инерции бетонного сечения брутто для условия гибкости l0/i (п. 8.1.2 СП 63).</summary>
public sealed class ConcreteRadiusOfGyrationTests
{
    [Fact]
    public void Rectangle_GivesSideOverSqrt12()
    {
        var r = ConcreteRadiusOfGyration.Compute(Sp63NormalFixtures.Rectangle(0.3, 0.6));

        Assert.NotNull(r);
        Assert.Equal(0.6 / Math.Sqrt(12.0), r.Value.RadiusX, 12);
        Assert.Equal(0.3 / Math.Sqrt(12.0), r.Value.RadiusY, 12);
    }

    [Fact]
    public void RebarAreas_AreIgnored()
    {
        var r = ConcreteRadiusOfGyration.Compute(
            Sp63NormalFixtures.TwoLayerRectangle(0.3, 0.6, 0.004, 0.004));

        Assert.NotNull(r);
        Assert.Equal(0.6 / Math.Sqrt(12.0), r.Value.RadiusX, 12);
    }

    [Fact]
    public void ShiftedSection_IsMeasuredAboutItsOwnCentroid()
    {
        var section = new CrossSection();
        section.Areas.Add(Sp63NormalFixtures.ConcreteRegion(Sp63NormalFixtures.Concrete(),
            [(1.0, 2.0), (1.3, 2.0), (1.3, 2.6), (1.0, 2.6)]));

        var r = ConcreteRadiusOfGyration.Compute(section);

        Assert.NotNull(r);
        Assert.Equal(0.6 / Math.Sqrt(12.0), r.Value.RadiusX, 12);
        Assert.Equal(0.3 / Math.Sqrt(12.0), r.Value.RadiusY, 12);
    }

    [Fact]
    public void Circle_GivesQuarterDiameter()
    {
        var r = ConcreteRadiusOfGyration.Compute(Sp63NormalFixtures.CircleSection(0.25, 128));

        // 128-угольник вписан в окружность — допуск на аппроксимацию 0,1 %.
        Assert.NotNull(r);
        Assert.InRange(r.Value.RadiusX, 0.125 * 0.999, 0.125 * 1.001);
        Assert.InRange(r.Value.RadiusY, 0.125 * 0.999, 0.125 * 1.001);
    }

    [Fact]
    public void Ring_SubtractsHole()
    {
        var r = ConcreteRadiusOfGyration.Compute(Sp63NormalFixtures.RingSection(0.30, 0.20, 128));

        double expected = Math.Sqrt(0.30 * 0.30 + 0.20 * 0.20) / 2.0;
        Assert.NotNull(r);
        Assert.InRange(r.Value.RadiusX, expected * 0.999, expected * 1.001);
    }

    [Fact]
    public void ArbitraryContourWithCenteredHole_UsesSqrtIA()
    {
        // Квадрат 0,6×0,6 с квадратным отверстием 0,2×0,2 по центру:
        // I = (0,6⁴ − 0,2⁴)/12, A = 0,36 − 0,04, i = √(I/A).
        var section = new CrossSection();
        var area = Sp63NormalFixtures.ConcreteRegion(Sp63NormalFixtures.Concrete(),
            [(-0.3, -0.3), (0.3, -0.3), (0.3, 0.3), (-0.3, 0.3)]);
        area.Contours.Add(new Contour(
            [-0.1, 0.1, 0.1, -0.1, -0.1],
            [-0.1, -0.1, 0.1, 0.1, -0.1],
            "hole") { Type = ContourType.Hole });
        area.SetWKT();
        section.Areas.Add(area);

        double expectedArea = 0.6 * 0.6 - 0.2 * 0.2;
        double expectedI = (Math.Pow(0.6, 4) - Math.Pow(0.2, 4)) / 12.0;
        double expected = Math.Sqrt(expectedI / expectedArea);

        var r = ConcreteRadiusOfGyration.Compute(section);

        Assert.NotNull(r);
        Assert.Equal(expectedArea, r.Value.Area, 12);
        Assert.Equal(expected, r.Value.RadiusX, 12);
        Assert.Equal(expected, r.Value.RadiusY, 12);
    }

    [Fact]
    public void OffsetHole_IsSubtractedAboutCombinedCentroid()
    {
        // Оракул — прямые формулы прямоугольников (площадь, статический момент и
        // момент инерции относительно начала координат), независимые от формул Грина.
        const double side = 0.6, hole = 0.2, offset = 0.1;
        double areaHull = side * side, areaHole = hole * hole;
        double area = areaHull - areaHole;

        double sy = -offset * areaHole;                       // ∫x dA: отверстие смещено по X
        double xc = sy / area;
        double iyAtOrigin = Math.Pow(side, 4) / 12.0
            - (Math.Pow(hole, 4) / 12.0 + areaHole * offset * offset);
        double iyCentroid = iyAtOrigin - area * xc * xc;

        double ixAtOrigin = Math.Pow(side, 4) / 12.0 - Math.Pow(hole, 4) / 12.0;

        var section = new CrossSection();
        var region = Sp63NormalFixtures.ConcreteRegion(Sp63NormalFixtures.Concrete(),
            [(-0.3, -0.3), (0.3, -0.3), (0.3, 0.3), (-0.3, 0.3)]);
        region.Contours.Add(new Contour(
            [offset - 0.1, offset + 0.1, offset + 0.1, offset - 0.1, offset - 0.1],
            [-0.1, -0.1, 0.1, 0.1, -0.1],
            "hole") { Type = ContourType.Hole });
        region.SetWKT();
        section.Areas.Add(region);

        var r = ConcreteRadiusOfGyration.Compute(section);

        Assert.NotNull(r);
        Assert.Equal(area, r.Value.Area, 12);
        Assert.Equal(Math.Sqrt(ixAtOrigin / area), r.Value.RadiusX, 12);
        Assert.Equal(Math.Sqrt(iyCentroid / area), r.Value.RadiusY, 12);
    }

    [Fact]
    public void SectionWithoutConcrete_ReturnsNull() =>
        Assert.Null(ConcreteRadiusOfGyration.Compute(new CrossSection()));
}
