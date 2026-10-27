using CSmath;

namespace CScore.Tests.Hysteresis;

/// <summary>Огибающие для тестов моделей с состоянием.</summary>
internal static class HystTestDiagrams
{
    /// <summary>
    /// Огибающая Кента–Парка (как у OpenSees Concrete01): парабола до (εc0, fpc), линейный спад до (εU, fpcu), далее
    /// площадка; растяжения нет. Напряжения в кПа (fpc, fpcu &lt; 0), плотная кусочно-линейная выборка.
    /// </summary>
    public static Diagramm KentPark(double fpc, double ec0, double fpcu, double ecu)
    {
        double lo = 4 * ecu;
        int n = 2000;
        // узлы ровно в εc0 и εU, чтобы пик и излом огибающей были точными
        var grid = Enumerable.Range(0, n + 1).Select(i => lo + (0 - lo) * i / n)
            .Where(e => Math.Abs(e - ec0) > 1e-7 && Math.Abs(e - ecu) > 1e-7)
            .Append(ec0).Append(ecu).Append(0.0).Distinct().OrderBy(e => e).ToArray();
        var xs = grid;
        var ys = grid.Select(e => e >= ec0 ? fpc * (2 * e / ec0 - (e / ec0) * (e / ec0))
                  : e >= ecu ? fpc + (fpcu - fpc) * (e - ec0) / (ecu - ec0)
                  : fpcu).ToArray();
        ys[^1] = 0;
        var ic = new LSpline(xs, ys);
        var it = new LSpline([0, 1e-9], [0, 0]);
        return new Diagramm(ic, it, DiagrammType.Custom, MatType.Concrete, "KentPark");
    }

    /// <summary>Бетон B25, L3 (огибающая СП 63), вид расчёта C.</summary>
    public static Diagramm Concrete()
    {
        var area = new MaterialArea
        {
            Category = AreaCategory.Region, Material = TestMaterials.Concrete("B25"), DiagrammType = DiagrammType.L3,
            Hull = new Contour([-0.1, 0.1, 0.1, -0.1, -0.1], [-0.1, -0.1, 0.1, 0.1, -0.1], "h"),
        };
        area.MaterialId = area.Material.Id;
        area.SetWKT();
        area.ResolveAndBuildDiagramms(0.85, pool: null, rebarDifferentialDiagram: false);
        return area.Diagramms[CalcType.C];
    }

    /// <summary>Бетон B25 L3 для вида N (растяжение есть у диаграммы).</summary>
    public static Diagramm ConcreteN()
    {
        var area = new MaterialArea
        {
            Category = AreaCategory.Region, Material = TestMaterials.Concrete("B25"), DiagrammType = DiagrammType.L3,
            Hull = new Contour([-0.1, 0.1, 0.1, -0.1, -0.1], [-0.1, -0.1, 0.1, 0.1, -0.1], "h"),
        };
        area.MaterialId = area.Material.Id;
        area.SetWKT();
        area.ResolveAndBuildDiagramms(0.85, pool: null, rebarDifferentialDiagram: false);
        return area.Diagramms[CalcType.N];
    }
}
