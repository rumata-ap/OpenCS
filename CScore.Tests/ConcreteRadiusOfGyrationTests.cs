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
    public void SectionWithoutConcrete_ReturnsNull() =>
        Assert.Null(ConcreteRadiusOfGyration.Compute(new CrossSection()));
}
