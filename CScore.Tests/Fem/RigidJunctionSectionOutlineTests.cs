using CScore.Fem;
using Xunit;

namespace CScore.Tests.Fem;

public sealed class RigidJunctionSectionOutlineTests
{
    static MaterialArea Region(double x0, double y0, double x1, double y1, Contour? hole = null)
    {
        var area = new MaterialArea
        {
            Tag = "concrete", Category = AreaCategory.Region,
            Hull = new Contour(new[] { x0, x1, x1, x0, x0 }, new[] { y0, y0, y1, y1, y0 }, "outer")
        };
        if (hole != null) { hole.Type = ContourType.Hole; area.Contours.Add(hole); }
        return area;
    }

    static void AssertSameSet(IEnumerable<(double Y, double Z)> expected, IReadOnlyList<(double Y, double Z)> actual)
    {
        var exp = expected.ToList();
        Assert.Equal(exp.Count, actual.Count);
        foreach (var e in exp)
            Assert.Contains(actual, a => Math.Abs(a.Y - e.Y) < 1e-12 && Math.Abs(a.Z - e.Z) < 1e-12);
    }

    [Fact]
    public void CenteredRectangle_YAlongContourY_ZAlongContourX()
    {
        // b = 0,3 по x сечения, h = 0,4 по y сечения.
        var section = new CrossSection { Areas = [Region(-0.15, -0.2, 0.15, 0.2)] };

        var outline = RigidJunctionSectionOutline.From(section)!;

        AssertSameSet([(-0.2, -0.15), (0.2, -0.15), (0.2, 0.15), (-0.2, 0.15)], outline);
    }

    [Fact]
    public void OffsetRectangle_IsShiftedToCentroid()
    {
        var section = new CrossSection { Areas = [Region(0.85, 2.0, 1.15, 2.4)] };

        var outline = RigidJunctionSectionOutline.From(section)!;

        AssertSameSet([(-0.2, -0.15), (0.2, -0.15), (0.2, 0.15), (-0.2, 0.15)], outline);
    }

    [Fact]
    public void HoleShiftsCentroid_HullStaysConvex()
    {
        // Квадрат 1×1 с отверстием 0,5×0,5 в правом верхнем углу: ц. т. смещается влево-вниз.
        var hole = new Contour(new[] { 0.5, 1.0, 1.0, 0.5, 0.5 }, new[] { 0.5, 0.5, 1.0, 1.0, 0.5 }, "hole");
        var section = new CrossSection { Areas = [Region(0, 0, 1, 1, hole)] };

        var outline = RigidJunctionSectionOutline.From(section)!;

        // Ц. т.: (0,5·1 − 0,75·0,25) / 0,75 = 5/12 по обеим осям.
        double c = 5.0 / 12.0;
        AssertSameSet([(-c, -c), (-c, 1 - c), (1 - c, 1 - c), (1 - c, -c)], outline);
    }

    [Fact]
    public void RebarOnlySection_ReturnsNull()
    {
        var rebar = new MaterialArea { Tag = "rebar", Category = AreaCategory.RebarGroup };

        Assert.Null(RigidJunctionSectionOutline.From(new CrossSection { Areas = [rebar] }));
    }
}
