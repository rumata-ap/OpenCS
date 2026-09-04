using CScore;

namespace OpenCS.Tests;

/// <summary>Общие расчётные данные для тестов отчётности: сечение с посчитанным НДС,
/// на котором строится карта для POC, рендереров и сквозного acceptance-теста.</summary>
internal static class ReportFixtures
{
    /// <summary>Балка 300 × 600: B25 с сеткой фибр 8 × 16 и два стержня A500 понизу.
    /// Своя фикстура, а не ShearInclinedFixtures.Beam(): та задаёт характеристики через
    /// сеттер Material.C, который наполняет только внутренний словарь, оставляя список
    /// materialChars пустым, — а карте нужны построенные диаграммы всех видов расчёта.</summary>
    internal static CrossSection BuildBeam()
    {
        var concrete = new Material
        {
            Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000.0,
            MaterialChars = AllCalcTypes(() => new MaterialChars(CalcType.C)
            {
                Type = MatType.Concrete,
                Fc = -14_500.0,          // сжатие — «минус»
                Ft = 1_050.0,
                E = 30_000_000.0,
                Ec1Red = -0.0015,
                Ec2 = -0.0035,
                Et1Red = 0.00008,
                Et2 = 0.00015
            })
        };

        var steel = new Material
        {
            Id = 2, Tag = "A500", Type = MatType.ReSteelF, E = 200_000_000.0,
            MaterialChars = AllCalcTypes(() => new MaterialChars(CalcType.C)
            {
                Type = MatType.ReSteelF,
                Class = 500.0,
                Fc = -435_000.0,
                Ft = 435_000.0,
                E = 200_000_000.0,
                // Без предельных деформаций узлы двухлинейной диаграммы совпадают
                // и D2L() падает на интерполяции.
                Ec2 = -0.0035,
                Et2 = 0.025
            })
        };

        var region = new MaterialArea
        {
            Tag = "Бетон",
            Category = AreaCategory.Region,
            Material = concrete,
            MaterialId = concrete.Id,
            DiagrammType = DiagrammType.L2
        };
        region.Contours.Add(new Contour(
            [-0.15, 0.15, 0.15, -0.15, -0.15],
            [-0.30, -0.30, 0.30, 0.30, -0.30], "hull") { Type = ContourType.Hull });
        region.SetWKT();
        region.SliceXY(nx: 8, ny: 16);

        var rebar = new MaterialArea
        {
            Tag = "Арматура",
            Category = AreaCategory.RebarGroup,
            Material = steel,
            MaterialId = steel.Id,
            DiagrammType = DiagrammType.L2
        };
        rebar.Fibers.Add(new Fiber { TypeFiber = FiberType.point, X = -0.08, Y = -0.25, Area = 0.000616 });
        rebar.Fibers.Add(new Fiber { TypeFiber = FiberType.point, X = 0.08, Y = -0.25, Area = 0.000616 });

        var section = new CrossSection { Tag = "Б-1" };
        section.Areas.Add(region);
        section.Areas.Add(rebar);
        section.ResolveAndBuildDiagramms();
        return section;
    }

    /// <summary>Одни и те же характеристики на все четыре вида расчёта: Material.GetD2L
    /// строит диаграммы сразу для C/CL/N/NL и падает, если хоть одного нет. Для POC
    /// длительные принимаются равными кратковременным — карта строится для CalcType.C.</summary>
    static List<MaterialChars> AllCalcTypes(Func<MaterialChars> factory)
    {
        var all = new List<MaterialChars>();
        foreach (var calcType in new[] { CalcType.C, CalcType.CL, CalcType.N, CalcType.NL })
        {
            var chars = factory();
            chars.TypeCalc = calcType;
            all.Add(chars);
        }
        return all;
    }
}
