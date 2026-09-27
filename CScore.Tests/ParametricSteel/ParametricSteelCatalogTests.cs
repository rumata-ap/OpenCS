using Xunit;
using CScore.ParametricSteel;
using static CScore.Tests.ParametricSteel.ParametricSteelTestData;
using D = CScore.ParametricSteel.ParametricSteelSectionDefinition;

namespace CScore.Tests.ParametricSteel;

/// <summary>Геометрия сгенерированных профилей против справочных A, Ix, Iy сортамента (Sortamenty.db3).</summary>
public class ParametricSteelCatalogTests
{
    public static TheoryData<string, D, double, double, double> Catalog() => new()
    {
        // Имя, определение, A (см²), Ix, Iy (см⁴); Iy = 0 — не проверяется.
        { "30Б1 Р 57837", D.RolledIBeam(298 * Mm, 149 * Mm, 5.5 * Mm, 8 * Mm, 13 * Mm), 40.8, 6318, 442 },
        { "60Б1 Р 57837", D.RolledIBeam(596 * Mm, 199 * Mm, 10 * Mm, 15 * Mm, 22 * Mm), 120.5, 68720, 1980 },
        // Справочные величины ГОСТ 8239 соответствуют уклону 12 %, ГОСТ 8240 «У» — 10 % (колонка «i» базы — не уклон).
        { "10 ГОСТ 8239", D.RolledIBeam(100 * Mm, 55 * Mm, 4.5 * Mm, 7.2 * Mm, 7 * Mm, 2.5 * Mm, 0.12), 12.0, 198, 17.9 },
        { "20 ГОСТ 8239", D.RolledIBeam(200 * Mm, 100 * Mm, 5.2 * Mm, 8.4 * Mm, 9.5 * Mm, 4 * Mm, 0.12), 26.8, 1840, 115 },
        { "27 ГОСТ 8239", D.RolledIBeam(270 * Mm, 125 * Mm, 6 * Mm, 9.8 * Mm, 11 * Mm, 4.5 * Mm, 0.12), 40.2, 5010, 260 },
        { "60 ГОСТ 8239", D.RolledIBeam(600 * Mm, 190 * Mm, 12 * Mm, 17.8 * Mm, 20 * Mm, 8 * Mm, 0.12), 138, 76810, 1725 },
        { "20П ГОСТ 8240", D.RolledChannel(200 * Mm, 76 * Mm, 5.2 * Mm, 9 * Mm, 9.5 * Mm, 5.5 * Mm), 23.4, 1530, 134 },
        { "5У ГОСТ 8240", D.RolledChannel(50 * Mm, 32 * Mm, 4.4 * Mm, 7 * Mm, 6 * Mm, 2.5 * Mm, 0.10), 6.16, 22.8, 5.61 },
        { "18аУ ГОСТ 8240", D.RolledChannel(180 * Mm, 74 * Mm, 5.1 * Mm, 9.3 * Mm, 9 * Mm, 3.5 * Mm, 0.10), 22.2, 1190, 105 },
        { "20У ГОСТ 8240", D.RolledChannel(200 * Mm, 76 * Mm, 5.2 * Mm, 9 * Mm, 9.5 * Mm, 4 * Mm, 0.10), 23.4, 1520, 113 },
        { "40У ГОСТ 8240", D.RolledChannel(400 * Mm, 115 * Mm, 8 * Mm, 13.5 * Mm, 15 * Mm, 6 * Mm, 0.10), 61.5, 15220, 642 },
        { "гн. швеллер 200×80×6", D.BentChannel(200 * Mm, 80 * Mm, 6 * Mm, 9 * Mm), 20.26, 1175, 120.2 },
        { "L100×100×8", D.RolledAngle(100 * Mm, 100 * Mm, 8 * Mm, 12 * Mm, 4 * Mm), 15.6, 147.2, 147.2 },
        { "L100×63×8", D.RolledAngle(100 * Mm, 63 * Mm, 8 * Mm, 10 * Mm, 3.3 * Mm), 12.57, 127, 39.21 },
        { "гн. L100×6", D.BentAngle(100 * Mm, 100 * Mm, 6 * Mm, 9 * Mm), 11.33, 112.2, 112.2 },
        { "□200×100×6", D.BentBox(200 * Mm, 100 * Mm, 6 * Mm, 6 * Mm), 33.63, 1703, 576.9 },
        { "□200×100×6,5", D.BentBox(200 * Mm, 100 * Mm, 6.5 * Mm, 9.75 * Mm), 35.86, 1783, 605.5 },
        { "○159×6", D.Pipe(159 * Mm, 6 * Mm), 28.84, 845.2, 845.2 },
    };

    [Theory]
    [MemberData(nameof(Catalog))]
    public void GeometryMatchesCatalog(string name, D definition, double aCm2, double ixCm4, double iyCm4)
    {
        var poly = Polygon(Generate(definition).Section);
        Assert.True(Math.Abs(poly.A * 1e4 / aCm2 - 1) <= 0.01, $"{name}: A = {poly.A * 1e4:F3} см² против {aCm2}");
        Assert.True(Math.Abs(poly.Ix * 1e8 / ixCm4 - 1) <= 0.015, $"{name}: Ix = {poly.Ix * 1e8:F1} см⁴ против {ixCm4}");
        if (iyCm4 > 0)
            Assert.True(Math.Abs(poly.Iy * 1e8 / iyCm4 - 1) <= 0.03, $"{name}: Iy = {poly.Iy * 1e8:F1} см⁴ против {iyCm4}");
    }

    [Fact]
    public void PipeAndRoundHaveExactArea()
    {
        var pipe = Polygon(Generate(D.Pipe(0.159, 0.006)).Section);
        Assert.Equal(Math.PI / 4 * (0.159 * 0.159 - 0.147 * 0.147), pipe.A, 12);
        var round = Polygon(Generate(D.RoundBar(0.05)).Section);
        Assert.Equal(Math.PI / 4 * 0.05 * 0.05, round.A, 12);
        Assert.True(Math.Abs(round.Ix / (Math.PI * Math.Pow(0.05, 4) / 64) - 1) < 1e-3);
    }
}
