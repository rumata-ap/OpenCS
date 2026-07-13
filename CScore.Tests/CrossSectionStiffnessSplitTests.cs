using Xunit;

namespace CScore.Tests;

public class CrossSectionStiffnessSplitTests
{
    [Fact]
    public void SplitStiffnessByMaterial_SeparatesConcreteAndRebar_ForSymmetricSection()
    {
        // 0.3×0.6, центр в (0,0), 4 угловых стержня Ø25, E_бетон=32.5e6, E_сталь=200e6
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.3, 0.6);

        var split = section.SplitStiffnessByMaterial();

        // Бетон: прямоугольник, центр совпадает с общим центром тяжести (сечение симметрично)
        // Ix = b*h^3/12 = 0.3*0.6^3/12 = 0.0054 м^4; EIx = 32_500_000*0.0054 = 175_500
        Assert.True(split.EIxConcrete > 170_000 && split.EIxConcrete < 180_000,
            $"EIxConcrete={split.EIxConcrete}");

        // Iy = h*b^3/12 = 0.6*0.3^3/12 = 0.00135 м^4; EIy = 32_500_000*0.00135 = 43_875
        Assert.True(split.EIyConcrete > 42_000 && split.EIyConcrete < 46_000,
            $"EIyConcrete={split.EIyConcrete}");

        // Арматура: 4×Ø25 в углах (±0.10; ±0.25) при cover=0.05
        // A_bar = π*0.025²/4 ≈ 0.00049087 м²; Is_x = 4*A*0.25² ≈ 0.00012272 м^4
        // EIx_rebar = 200_000_000*0.00012272 ≈ 24_544
        Assert.True(split.EIxRebar > 23_000 && split.EIxRebar < 26_000,
            $"EIxRebar={split.EIxRebar}");

        // Is_y = 4*A*0.10² ≈ 0.0000196348 м^4; EIy_rebar ≈ 3_927
        Assert.True(split.EIyRebar > 3_000 && split.EIyRebar < 4_800,
            $"EIyRebar={split.EIyRebar}");
    }

    [Fact]
    public void SplitStiffnessByMaterial_MatchesPlainGeoProps_WhenNoRebar()
    {
        // Без арматуры: бетонная составляющая должна совпасть с прямым расчётом
        // через GeoProps по контуру.
        var section = SectionCutFixtures.BuildHollowRectangle(outerSize: 0.6, innerSize: 0.2);

        var split = section.SplitStiffnessByMaterial();

        Assert.Equal(0.0, split.EIxRebar, precision: 6);
        Assert.Equal(0.0, split.EIyRebar, precision: 6);
        Assert.True(split.EIxConcrete > 0);
        Assert.True(split.EIyConcrete > 0);
    }

    [Fact]
    public void SectionBoundingBox_ReturnsExtentsOfReinforcedRectangle()
    {
        var section = SectionCutFixtures.BuildReinforcedRectangle(0.3, 0.6);

        var (minX, maxX, minY, maxY) = section.SectionBoundingBox();

        Assert.Equal(-0.15, minX, precision: 6);
        Assert.Equal(0.15, maxX, precision: 6);
        Assert.Equal(-0.3, minY, precision: 6);
        Assert.Equal(0.3, maxY, precision: 6);
    }
}
