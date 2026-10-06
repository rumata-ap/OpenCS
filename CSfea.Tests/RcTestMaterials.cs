using CScore;

namespace CSfea.Tests;

/// <summary>Бетон B25 и арматура A500 — числа справочников OpenCS (как TestMaterials в CScore.Tests), кПа.</summary>
internal static class RcTestMaterials
{
    static MaterialChars ConcreteChars(CalcType ct) => new()
    {
        Type = MatType.Concrete, TypeCalc = ct, Fc = -18500, Ft = 1550, E = 30_000_000,
        Ec0 = -0.002, Ec1 = -0.00029, Ec1Red = -0.0015, Ec2 = -0.0035,
        Et0 = 0.0001, Et1 = 0.000021, Et1Red = 0.00008, Et2 = 0.00015,
    };

    static MaterialChars RebarChars(CalcType ct) => new()
    {
        Type = MatType.ReSteelF, TypeCalc = ct, Fc = -500000, Ft = 500000, E = 200_000_000,
        Ec2 = -0.0035, Et2 = 0.025,
    };

    public static Material ConcreteB25() => new()
    {
        Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000,
        MaterialChars = [ConcreteChars(CalcType.C), ConcreteChars(CalcType.CL), ConcreteChars(CalcType.N), ConcreteChars(CalcType.NL)],
    };

    public static Material RebarA500() => new()
    {
        Id = 2, Tag = "A500", Type = MatType.ReSteelF, E = 200_000_000,
        MaterialChars = [RebarChars(CalcType.C), RebarChars(CalcType.CL), RebarChars(CalcType.N), RebarChars(CalcType.NL)],
    };

    /// <summary>Трёхлинейная диаграмма бетона B25 для II группы (CalcType.N).</summary>
    public static Diagramm ConcreteN() => ConcreteB25().GetDiagramms(DiagrammType.L3)![CalcType.N];

    /// <summary>Двухлинейная диаграмма арматуры A500 для II группы (CalcType.N).</summary>
    public static Diagramm RebarN() => RebarA500().GetDiagramms(DiagrammType.L2)![CalcType.N];

    /// <summary>
    /// Прямоугольное сечение b×h (оси X — ширина, Y — высота, начало в центре) из B25 с точечной
    /// арматурой A500: по <paramref name="nBars"/> стержней внизу и вверху на расстоянии
    /// <paramref name="a"/> от граней. <paramref name="mesh"/> = false — бетон без сетки (контурный путь).
    /// </summary>
    public static CrossSection Rect(double b, double h, double a, double asBot, double asTop,
        int nBars = 3, int nx = 8, int ny = 40, bool mesh = true)
    {
        var concreteMat = ConcreteB25();
        var rebarMat = RebarA500();
        var concrete = new MaterialArea
        {
            Id = 1, Tag = "бетон", Category = AreaCategory.Region,
            Material = concreteMat, MaterialId = concreteMat.Id, DiagrammType = DiagrammType.L3,
            Hull = new Contour([-b / 2, b / 2, b / 2, -b / 2, -b / 2], [-h / 2, -h / 2, h / 2, h / 2, -h / 2], "hull"),
        };
        concrete.SetWKT();
        if (mesh) concrete.SliceXY(nx: nx, ny: ny);

        MaterialArea Bars(int id, double area, double y)
        {
            var r = new MaterialArea
            {
                Id = id, Tag = $"арматура {id}", Category = AreaCategory.RebarGroup,
                Material = rebarMat, MaterialId = rebarMat.Id, DiagrammType = DiagrammType.L2,
                HostArea = concrete, HostAreaId = concrete.Id,
            };
            for (int i = 0; i < nBars; i++)
            {
                double x = nBars == 1 ? 0.0 : -b / 2 + a + i * (b - 2 * a) / (nBars - 1);
                var bar = Fiber.CreatePoint(0.016, x, y);
                bar.Area = area / nBars;
                r.Fibers.Add(bar);
            }
            return r;
        }

        var section = new CrossSection
        {
            Id = 1, Tag = "прямоугольник",
            Areas = [concrete, Bars(2, asBot, -h / 2 + a), Bars(3, asTop, h / 2 - a)],
        };
        section.ResolveAndBuildDiagramms(rebarDifferentialDiagram: false);
        return section;
    }
}
