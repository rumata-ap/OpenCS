using CScore;
using CScore.Sp63Shear;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Сквозная проверка: заданные в GUI хомуты доходят до расчёта наклонных сечений.</summary>
public sealed class ShearInclinedWithStirrupsTests
{
    [Fact]
    public void Resolve_WithStirrupArea_GivesPositiveQswAndNoWarning()
    {
        var section = BeamWithoutStirrups();
        section.Areas.Add(StirrupArea(section));

        var data = StirrupResolver.Resolve(section, ShearPlane.Vy, CalcType.C);

        Assert.Equal(2 * 0.0000503, data.Asw, 12);
        Assert.True(data.Qsw > 0);
        Assert.Equal(0.15, data.Sw, 12);
        Assert.DoesNotContain(data.Warnings, w => w.Contains("не задана"));
    }

    [Fact]
    public void Resolve_WithoutStirrupArea_StillWarnsAboutConcreteOnly()
    {
        var section = BeamWithoutStirrups();

        var data = StirrupResolver.Resolve(section, ShearPlane.Vy, CalcType.C);

        Assert.Equal(0.0, data.Qsw, 12);
        Assert.Contains(data.Warnings, w => w.Contains("не задана"));
    }

    static CrossSection BeamWithoutStirrups()
    {
        var section = ShearInclinedFixtures.Beam();
        foreach (var area in section.Areas)
            area.Stirrups.Clear();
        return section;
    }

    static MaterialArea StirrupArea(CrossSection section)
    {
        var steel = section.Areas.First(a => a.Material?.Type is MatType.ReSteelF or MatType.ReSteelU).Material!;
        var area = new MaterialArea
        {
            Id = 900, Tag = "Хомуты", Category = AreaCategory.Stirrups,
            MaterialId = steel.Id, Material = steel
        };
        area.Stirrups =
        [
            new StirrupGroup
            {
                MaterialId = steel.Id, SpacingM = 0.15, OffsetM = 0.03,
                Elements =
                [
                    new StirrupElement
                    {
                        CenterlineContour = Contour.Polyline([-0.12, -0.12], [-0.22, 0.22], "срез"),
                        BarAreaM2 = 0.0000503, BarDiameterM = 0.008
                    },
                    new StirrupElement
                    {
                        CenterlineContour = Contour.Polyline([0.12, 0.12], [-0.22, 0.22], "срез"),
                        BarAreaM2 = 0.0000503, BarDiameterM = 0.008
                    }
                ]
            }
        ];
        return area;
    }
}
