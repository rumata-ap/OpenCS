using CScore;
using CScore.Sp63Shear;
using Xunit;

namespace CScore.Tests.Sp63Shear;

/// <summary>Приведение замкнутых хомутов к погонному усилию qsw.</summary>
public sealed class StirrupResolverTests
{
    const double BarArea = 0.0000503;   // ⌀8, м²
    const double Rs = 435_000.0;        // кПа

    [Fact]
    public void Resolve_RectangularStirrup_CountsTwoVerticalBranches()
    {
        var section = SectionWithStirrup(Rectangle(-0.12, -0.27, 0.12, 0.27), spacing: 0.15);

        var data = StirrupResolver.Resolve(section, ShearPlane.Vy, CalcType.C);

        Assert.Equal(2.0 * BarArea, data.Asw, 12);
        Assert.Equal(300_000.0, data.Rsw, 6);
        Assert.Equal(300_000.0 * 2.0 * BarArea / 0.15, data.Qsw, 6);
        Assert.Equal(0.15, data.Sw, 12);
    }

    [Theory]
    [InlineData(240.0, 170_000.0)]
    [InlineData(400.0, 280_000.0)]
    [InlineData(500.0, 300_000.0)]
    public void Resolve_UsesTable615ByRebarClass(double materialClass, double expectedRsw)
    {
        var section = SectionWithStirrup(
            Rectangle(-0.12, -0.27, 0.12, 0.27), spacing: 0.15, materialClass: materialClass);

        var data = StirrupResolver.Resolve(section, ShearPlane.Vy, CalcType.C);

        Assert.Equal(expectedRsw, data.Rsw, 6);
    }

    [Fact]
    public void Resolve_UnknownClass_IsCappedAtTable615Maximum()
    {
        var section = SectionWithStirrup(
            Rectangle(-0.12, -0.27, 0.12, 0.27), spacing: 0.15, materialClass: 600.0);

        var data = StirrupResolver.Resolve(section, ShearPlane.Vy, CalcType.C);

        Assert.Equal(300_000.0, data.Rsw, 6);
        Assert.Contains(data.Warnings, w => w.Contains("табл. 6.15"));
    }

    [Fact]
    public void Resolve_RectangularStirrup_HorizontalPlaneAlsoCountsTwoBranches()
    {
        var section = SectionWithStirrup(Rectangle(-0.12, -0.27, 0.12, 0.27), spacing: 0.15);

        var data = StirrupResolver.Resolve(section, ShearPlane.Vx, CalcType.C);

        Assert.Equal(2.0 * BarArea, data.Asw, 12);
    }

    [Fact]
    public void Resolve_InclinedBranches_AreCountedByProjection()
    {
        // Ромб: каждая из четырёх ветвей наклонена под 45°, проекция на Y равна √2/2
        var loop = new List<(double X, double Y)>
        {
            (0.0, -0.20), (0.20, 0.0), (0.0, 0.20), (-0.20, 0.0), (0.0, -0.20)
        };
        var section = SectionWithStirrup(loop, spacing: 0.20);

        var data = StirrupResolver.Resolve(section, ShearPlane.Vy, CalcType.C);

        Assert.Equal(4.0 * BarArea * Math.Sqrt(2.0) / 2.0, data.Asw, 12);
    }

    [Fact]
    public void Resolve_TwoGroupsWithDifferentSpacing_SumsQswAndTakesMaxSpacing()
    {
        var section = SectionWithStirrup(Rectangle(-0.12, -0.27, 0.12, 0.27), spacing: 0.15);
        var area = section.Areas[0];
        var second = Group(Rectangle(-0.06, -0.27, 0.06, 0.27), spacing: 0.30);
        second.MaterialId = 2;
        area.Stirrups.Add(second);

        var data = StirrupResolver.Resolve(section, ShearPlane.Vy, CalcType.C);

        double expected = 300_000.0 * 2.0 * BarArea / 0.15
                        + 300_000.0 * 2.0 * BarArea / 0.30;
        Assert.Equal(expected, data.Qsw, 6);
        Assert.Equal(0.30, data.Sw, 12);
    }

    [Fact]
    public void Resolve_NoStirrups_ReturnsZeroWithoutThrowing()
    {
        var section = SectionWithStirrup(Rectangle(-0.12, -0.27, 0.12, 0.27), spacing: 0.15);
        section.Areas[0].Stirrups.Clear();

        var data = StirrupResolver.Resolve(section, ShearPlane.Vy, CalcType.C);

        Assert.Equal(0.0, data.Qsw, 12);
        Assert.Equal(0.0, data.Asw, 12);
    }

    [Fact]
    public void Resolve_UnknownStirrupMaterial_WarnsAndSkipsGroup()
    {
        var section = SectionWithStirrup(Rectangle(-0.12, -0.27, 0.12, 0.27), spacing: 0.15);
        section.Areas[0].Stirrups[0].MaterialId = 999;

        var data = StirrupResolver.Resolve(section, ShearPlane.Vy, CalcType.C);

        Assert.Equal(0.0, data.Qsw, 12);
        Assert.Contains(data.Warnings, w => w.Contains("999"));
    }

    static List<(double X, double Y)> Rectangle(double x0, double y0, double x1, double y1) =>
    [
        (x0, y0), (x1, y0), (x1, y1), (x0, y1), (x0, y0)
    ];

    static StirrupGroup Group(List<(double X, double Y)> loop, double spacing)
    {
        var xs = loop.Select(p => p.X).ToList();
        var ys = loop.Select(p => p.Y).ToList();
        return new StirrupGroup
        {
            MaterialId = 2,
            SpacingM = spacing,
            Elements =
            [
                new StirrupElement
                {
                    CenterlineContour = new Contour(xs, ys, "stirrup"),
                    BarAreaM2 = BarArea,
                    BarDiameterM = 0.008
                }
            ]
        };
    }

    static CrossSection SectionWithStirrup(
        List<(double X, double Y)> loop, double spacing, double materialClass = 500.0)
    {
        var concrete = Sp63ShearFixtures.Concrete(1, 11_500.0, 900.0);
        var steel = Sp63ShearFixtures.Rebar(2, Rs, materialClass);

        var area = Sp63ShearFixtures.ConcreteRegion(concrete,
            [(-0.15, -0.30), (0.15, -0.30), (0.15, 0.30), (-0.15, 0.30)]);

        var group = Group(loop, spacing);
        group.MaterialId = steel.Id;
        area.Stirrups.Add(group);

        var section = new CrossSection();
        section.Areas.Add(area);
        section.Areas.Add(new MaterialArea
        {
            Category = AreaCategory.RebarGroup,
            Material = steel,
            MaterialId = steel.Id
        });
        return section;
    }
}
