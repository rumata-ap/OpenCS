using System;
using CScore;
using Xunit;

namespace CScore.Tests.CrackWidth;

// Геометрия п. 8.2.17 по направлению плоскости трещинообразования (обобщение на косой изгиб):
// s = n·(x, y), n = (kz, ky)/|g|, ε = e0 + |g|·s; xt = s_t − s0, Abt — полоса s ≥ s_t − xt,eff,
// xt,eff в пределах [2a; 0,5h]; сечение растянуто целиком — Abt = вся площадь бетона.
public class CrackSectionGeometryTests
{
    static readonly Kurvature NoPlane = new() { e0 = 0.0, ky = 0.0, kz = 0.0 };

    static CrossSection Rect03x05() => TestSections.Rect(0.3, 0.5, 6, 10,
        bars: [(-0.10, -0.21, 0.016), (0.10, -0.21, 0.016)]);

    [Fact]
    public void Rectangle_SmallTensionZone_StripOfHeightXt()
    {
        // ky < 0 — растянут низ, n = (0, −1), s = −y: s_t = 0,25, s0 = −e0/|g| = 0,1, xt = 0,15.
        var plane = new Kurvature { e0 = -0.0001, ky = -0.001, kz = 0.0 };
        var g = CrackSectionGeometry.Compute(Rect03x05(), plane, plane, (0.0, -0.21));

        Assert.Equal(0.5, g.H, 9);
        Assert.Equal(0.04, g.A, 9);
        Assert.Equal(0.46, g.H0, 9);
        Assert.Equal(0.15, g.Xt, 9);
        Assert.Equal(0.15, g.XtEff, 9);
        Assert.Equal(0.3 * 0.15, g.Abt, 9);
        Assert.False(g.WholeSectionInTension);
    }

    [Fact]
    public void Rectangle_UpperLimitIsHalfH_NotHalfH0()
    {
        // xt = 0,35 > 0,5h = 0,25 (прежде ограничивалось 0,5h0 = 0,23).
        var plane = new Kurvature { e0 = 0.0001, ky = -0.001, kz = 0.0 };
        var g = CrackSectionGeometry.Compute(Rect03x05(), plane, plane, (0.0, -0.21));

        Assert.Equal(0.35, g.Xt, 9);
        Assert.Equal(0.25, g.XtEff, 9);
        Assert.Equal(0.3 * 0.25, g.Abt, 9);
    }

    [Fact]
    public void Rectangle_LowerLimitIsTwoA()
    {
        var plane = new Kurvature { e0 = -0.0002, ky = -0.001, kz = 0.0 };   // xt = 0,05 < 2a = 0,08
        var g = CrackSectionGeometry.Compute(Rect03x05(), plane, plane, (0.0, -0.21));

        Assert.Equal(0.05, g.Xt, 9);
        Assert.Equal(0.08, g.XtEff, 9);
        Assert.Equal(0.3 * 0.08, g.Abt, 9);
    }

    [Fact]
    public void RotatedRectangle_SameValues()
    {
        var rotated = TestSections.Rect(0.5, 0.3, 10, 6,
            bars: [(-0.21, -0.10, 0.016), (-0.21, 0.10, 0.016)]);
        var plane = new Kurvature { e0 = -0.0001, ky = 0.0, kz = -0.001 };
        var g = CrackSectionGeometry.Compute(rotated, plane, plane, (-0.21, 0.0));

        Assert.Equal(0.5, g.H, 9);
        Assert.Equal(0.04, g.A, 9);
        Assert.Equal(0.15, g.Xt, 9);
        Assert.Equal(0.3 * 0.15, g.Abt, 9);
    }

    [Fact]
    public void Square_ObliquePlane_CornerTriangle()
    {
        // n = (−1, −1)/√2, s0 = 0,1: xt = 0,2√2 − 0,1; полоса у угла — треугольник площадью xt².
        var square = TestSections.Rect(0.4, 0.4, 8, 8, bars: [(-0.15, -0.15, 0.016)]);
        double gMag = 0.001 * Math.Sqrt(2.0);
        var plane = new Kurvature { e0 = -gMag * 0.1, ky = -0.001, kz = -0.001 };
        var g = CrackSectionGeometry.Compute(square, plane, plane, (-0.15, -0.15));

        double xt = 0.2 * Math.Sqrt(2.0) - 0.1;
        Assert.Equal(0.4 * Math.Sqrt(2.0), g.H, 9);
        Assert.Equal(0.05 * Math.Sqrt(2.0), g.A, 9);
        Assert.Equal(xt, g.Xt, 9);
        Assert.Equal(xt * xt, g.Abt, 9);
    }

    [Fact]
    public void HollowSection_HoleAreaExcluded()
    {
        // Коробка 0,6×0,6 с отверстием 0,3×0,3; ky < 0, s0 = 0: xt = 0,3 = 0,5h — нижняя половина.
        var hollow = SectionCutFixtures.BuildHollowRectangle(0.6, 0.3);
        var plane = new Kurvature { e0 = 0.0, ky = -0.001, kz = 0.0 };
        var g = CrackSectionGeometry.Compute(hollow, plane, plane, (0.0, -0.25));

        Assert.Equal(0.6, g.H, 9);
        Assert.Equal(0.3, g.XtEff, 9);
        Assert.Equal(0.6 * 0.3 - 0.3 * 0.15, g.Abt, 9);
    }

    [Fact]
    public void UniformTension_WholeArea()
    {
        var plane = new Kurvature { e0 = 0.0002, ky = 0.0, kz = 0.0 };
        var g = CrackSectionGeometry.Compute(Rect03x05(), plane, NoPlane, (0.0, 0.0));

        Assert.True(g.WholeSectionInTension);
        Assert.Equal(0.15, g.Abt, 12);
    }

    [Fact]
    public void WholeSectionInTension_WholeArea()
    {
        // s0 = −e0/|g| = −10 — растянуто всё сечение (внецентренное растяжение с малым эксцентриситетом).
        var plane = new Kurvature { e0 = 0.001, ky = -0.0001, kz = 0.0 };
        var g = CrackSectionGeometry.Compute(Rect03x05(), plane, plane, (0.0, -0.21));

        Assert.True(g.WholeSectionInTension);
        Assert.Equal(0.15, g.Abt, 12);
        Assert.Equal(0.5, g.H, 9);
    }
}
