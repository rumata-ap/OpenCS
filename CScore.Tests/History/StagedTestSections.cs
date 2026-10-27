namespace CScore.Tests.History;

/// <summary>Стадийные сечения для тестов истории нагружения (B25 L2, A500 L2).</summary>
internal static class StagedTestSections
{
    /// <summary>Прямоугольная бетонная область по контуру (без сетки) или с сеткой nx×ny.</summary>
    public static MaterialArea ConcreteRect(int id, double x0, double x1, double y0, double y1,
        Material concrete, int nx = 0, int ny = 0)
    {
        var area = new MaterialArea
        {
            Id = id, Tag = $"concrete{id}", Category = AreaCategory.Region,
            Material = concrete, MaterialId = concrete.Id, DiagrammType = DiagrammType.L2,
            Hull = new Contour([x0, x1, x1, x0, x0], [y0, y0, y1, y1, y0], "outer"),
        };
        area.SetWKT();
        if (nx > 0 && ny > 0) area.SliceXY(nx, ny);
        return area;
    }

    /// <summary>Группа точечных стержней.</summary>
    public static MaterialArea Bars(int id, Material steel, params (double X, double Y, double D)[] bars) => new()
    {
        Id = id, Tag = $"rebar{id}", Category = AreaCategory.RebarGroup,
        Material = steel, MaterialId = steel.Id, DiagrammType = DiagrammType.L2,
        Fibers = bars.Select(p => Fiber.CreatePoint(p.D, p.X, p.Y)).ToList(),
    };

    /// <summary>Часть 0 — балка 0,3×0,5 с нижней арматурой; часть 1 — надбетонка 0,3×0,1 с арматурой.</summary>
    public static StagedSection TwoPartBeam(int nx = 0, int ny = 0)
    {
        var b25 = TestMaterials.Concrete("B25");
        var a500 = TestMaterials.Rebar("A500");
        var part0 = new SectionPart
        {
            Index = 0, Tag = "балка",
            Areas =
            [
                ConcreteRect(1, -0.15, 0.15, -0.25, 0.25, b25, nx, ny),
                Bars(2, a500, (-0.1, -0.2, 0.020), (0.0, -0.2, 0.020), (0.1, -0.2, 0.020)),
            ],
        };
        var part1 = new SectionPart
        {
            Index = 1, Tag = "надбетонка",
            Areas =
            [
                ConcreteRect(3, -0.15, 0.15, 0.25, 0.35, b25, nx, ny > 0 ? Math.Max(2, ny / 5) : 0),
                Bars(4, a500, (-0.1, 0.3, 0.012), (0.1, 0.3, 0.012)),
            ],
        };
        var s = new StagedSection { Id = 100, Tag = "staged", Parts = [part0, part1] };
        s.ResolveAndBuildDiagramms(0.85, pool: null, rebarDifferentialDiagram: false);
        return s;
    }

    /// <summary>Одна часть 0,3×0,5 (B25 L3) с 3⌀20 A500 сверху и снизу (y = ±0,2) — для циклов знака момента.</summary>
    public static StagedSection SymmetricBeam(int nx = 0, int ny = 0)
    {
        var b25 = TestMaterials.Concrete("B25");
        var a500 = TestMaterials.Rebar("A500");
        var concrete = ConcreteRect(1, -0.15, 0.15, -0.25, 0.25, b25, nx, ny);
        concrete.DiagrammType = DiagrammType.L3;
        var part = new SectionPart
        {
            Index = 0, Tag = "балка",
            Areas =
            [
                concrete,
                Bars(2, a500, (-0.1, -0.2, 0.020), (0.0, -0.2, 0.020), (0.1, -0.2, 0.020)),
                Bars(3, a500, (-0.1, 0.2, 0.020), (0.0, 0.2, 0.020), (0.1, 0.2, 0.020)),
            ],
        };
        var s = new StagedSection { Id = 102, Tag = "symmetric", Parts = [part] };
        s.ResolveAndBuildDiagramms(0.85, pool: null, rebarDifferentialDiagram: false);
        return s;
    }

    /// <summary>Одночастное стадийное сечение из прямоугольника 0,3×0,5 с нижней арматурой.</summary>
    public static StagedSection SinglePart()
    {
        var two = TwoPartBeam();
        var s = new StagedSection { Id = 101, Tag = "single", Parts = [two.Parts[0]] };
        s.ResolveAndBuildDiagramms(0.85, pool: null, rebarDifferentialDiagram: false);
        return s;
    }
}
