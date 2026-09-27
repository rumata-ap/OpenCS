using Xunit;
using CScore.ParametricSteel;
using CScore.Sp16;

namespace CScore.Tests.ParametricSteel;

/// <summary>Общие данные тестов параметрических МК-сечений.</summary>
static class ParametricSteelTestData
{
    public const double Mm = 0.001;
    public static readonly SteelMaterialProps C245 = new(240000, 360000, 245000, 370000, 2.06e8);

    /// <summary>Контур сгенерированного сечения как полигон СП 16.</summary>
    public static PolygonSection Polygon(CrossSection section)
    {
        var area = Assert.Single(section.Areas);
        return new PolygonSection(area.Hull!.Points.Select(p => (p.X, p.Y)),
            area.Holes.Select(h => h.Points.Select(p => (p.X, p.Y))));
    }

    /// <summary>Генерирует сечение и требует отсутствия диагностики.</summary>
    public static ParametricSteelGenerationResult Generate(ParametricSteelSectionDefinition d)
    {
        var r = ParametricSteelSectionGenerator.Generate(d);
        Assert.Empty(r.Diagnostics);
        return r;
    }
}
