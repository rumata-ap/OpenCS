using CScore;

namespace CScore.Tests;

/// <summary>Тестовые сечения для модульных тестов CScore.</summary>
internal static class TestSections
{
    /// <summary>
    /// Прямоугольное ЖБ-сечение h×b с 2 стержнями понизу (защитный слой 40 мм).
    /// Бетонная область без сеточных фибр — работает через контурный интеграл (без триангуляции).
    /// </summary>
    public static CrossSection RectWithBottomRebar(double h = 0.5, double b = 0.3, double diam = 0.016)
    {
        var concreteMaterial = TestMaterials.Concrete("B25");
        var rebarMaterial = TestMaterials.Rebar("A500");

        double y0 = -h / 2.0, y1 = h / 2.0, x0 = -b / 2.0, x1 = b / 2.0;
        var hull = new Contour(
            new[] { x0, x1, x1, x0, x0 },
            new[] { y0, y0, y1, y1, y0 },
            "outer");

        var concrete = new MaterialArea
        {
            Tag = "concrete",
            Category = AreaCategory.Region,
            Material = concreteMaterial,
            MaterialId = concreteMaterial.Id,
            DiagrammType = DiagrammType.L2,
        };
        concrete.Hull = hull;
        concrete.SetWKT();
        concrete.SliceXY(nx: 12, ny: 20);

        var rebar = new MaterialArea
        {
            Tag = "rebar_bottom",
            Category = AreaCategory.RebarGroup,
            Material = rebarMaterial,
            MaterialId = rebarMaterial.Id,
            DiagrammType = DiagrammType.L2,
            Fibers =
            [
                Fiber.CreatePoint(diam, -b / 2.0 + 0.05, y0 + 0.04),
                Fiber.CreatePoint(diam, b / 2.0 - 0.05, y0 + 0.04),
            ],
        };

        var section = new CrossSection { Areas = [concrete, rebar] };
        section.ResolveAndBuildDiagramms(0.85, pool: null, rebarDifferentialDiagram: false);
        return section;
    }

    /// <summary>
    /// То же самое сечение, что и <see cref="RectWithBottomRebar"/>, но бетонная область
    /// НЕ триангулирована (нет вызова SliceXY/Triangulate) — Fibers пуст, есть только Hull.
    /// Воспроизводит режим "без сетки", в котором CrossSection.Integral идёт по контурному
    /// пути (теорема Грина).
    /// </summary>
    public static CrossSection RectWithBottomRebarNoMesh(double h = 0.5, double b = 0.3, double diam = 0.016)
    {
        var concreteMaterial = TestMaterials.Concrete("B25");
        var rebarMaterial = TestMaterials.Rebar("A500");

        double y0 = -h / 2.0, y1 = h / 2.0, x0 = -b / 2.0, x1 = b / 2.0;
        var hull = new Contour(
            new[] { x0, x1, x1, x0, x0 },
            new[] { y0, y0, y1, y1, y0 },
            "outer");

        var concrete = new MaterialArea
        {
            Tag = "concrete",
            Category = AreaCategory.Region,
            Material = concreteMaterial,
            MaterialId = concreteMaterial.Id,
            DiagrammType = DiagrammType.L2,
        };
        concrete.Hull = hull;
        concrete.SetWKT();
        // Намеренно без SliceXY/Triangulate: Fibers остаётся пустым.

        var rebar = new MaterialArea
        {
            Tag = "rebar_bottom",
            Category = AreaCategory.RebarGroup,
            Material = rebarMaterial,
            MaterialId = rebarMaterial.Id,
            DiagrammType = DiagrammType.L2,
            Fibers =
            [
                Fiber.CreatePoint(diam, -b / 2.0 + 0.05, y0 + 0.04),
                Fiber.CreatePoint(diam, b / 2.0 - 0.05, y0 + 0.04),
            ],
        };

        var section = new CrossSection { Areas = [concrete, rebar] };
        section.ResolveAndBuildDiagramms(0.85, pool: null, rebarDifferentialDiagram: false);
        return section;
    }

    /// <summary>
    /// Как <see cref="RectWithBottomRebar"/>, но с ДВУМЯ слоями арматуры одинакового
    /// диаметра: слой 1 — у самой растянутой (нижней) грани (как в RectWithBottomRebar),
    /// слой 2 — существенно ближе к нейтральной оси (y0+0.20 вместо y0+0.04), т.е. при
    /// изгибе он растянут заметно слабее слоя 1. Используется для проверки взвешенного
    /// приведения As_tens/ds_eq по отношению σi/σ_max (актуально при косом изгибе, где
    /// стержни в растянутой зоне напряжены существенно неравномерно).
    /// </summary>
    public static CrossSection RectWithTwoBottomRebarLayers(double h = 0.5, double b = 0.3, double diam = 0.016)
    {
        var concreteMaterial = TestMaterials.Concrete("B25");
        var rebarMaterial = TestMaterials.Rebar("A500");

        double y0 = -h / 2.0, y1 = h / 2.0, x0 = -b / 2.0, x1 = b / 2.0;
        var hull = new Contour(
            new[] { x0, x1, x1, x0, x0 },
            new[] { y0, y0, y1, y1, y0 },
            "outer");

        var concrete = new MaterialArea
        {
            Tag = "concrete",
            Category = AreaCategory.Region,
            Material = concreteMaterial,
            MaterialId = concreteMaterial.Id,
            DiagrammType = DiagrammType.L2,
        };
        concrete.Hull = hull;
        concrete.SetWKT();
        concrete.SliceXY(nx: 12, ny: 20);

        var rebar = new MaterialArea
        {
            Tag = "rebar_bottom_2layers",
            Category = AreaCategory.RebarGroup,
            Material = rebarMaterial,
            MaterialId = rebarMaterial.Id,
            DiagrammType = DiagrammType.L2,
            Fibers =
            [
                Fiber.CreatePoint(diam, -b / 2.0 + 0.05, y0 + 0.04),
                Fiber.CreatePoint(diam, b / 2.0 - 0.05, y0 + 0.04),
                Fiber.CreatePoint(diam, -b / 2.0 + 0.05, y0 + 0.20),
                Fiber.CreatePoint(diam, b / 2.0 - 0.05, y0 + 0.20),
            ],
        };

        var section = new CrossSection { Areas = [concrete, rebar] };
        section.ResolveAndBuildDiagramms(0.85, pool: null, rebarDifferentialDiagram: false);
        return section;
    }

    /// <summary>
    /// Прямоугольное сечение с НЕСИММЕТРИЧНЫМ армированием по ОБЕИМ осям (кластер из 4
    /// стержней у одного угла) — перекрёстный момент инерции сечения EIxy ненулевой.
    /// Используется для регрессии на рассогласование направления кривизны трещинообразования
    /// и луча момента при двухплоскостном изгибе (см.
    /// docs/superpowers/specs/2026-08-16-biaxial-moment-curvature-design.md, блокер ревью):
    /// для сечений без EIxy=0 упругая кривизна НЕ параллельна направлению момента.
    /// </summary>
    public static CrossSection RectWithCornerClusterRebar(double h = 0.5, double b = 0.4, double diam = 0.02)
    {
        var concreteMaterial = TestMaterials.Concrete("B25");
        var rebarMaterial = TestMaterials.Rebar("A500");

        double y0 = -h / 2.0, y1 = h / 2.0, x0 = -b / 2.0, x1 = b / 2.0;
        var hull = new Contour(
            new[] { x0, x1, x1, x0, x0 },
            new[] { y0, y0, y1, y1, y0 },
            "outer");

        var concrete = new MaterialArea
        {
            Tag = "concrete",
            Category = AreaCategory.Region,
            Material = concreteMaterial,
            MaterialId = concreteMaterial.Id,
            DiagrammType = DiagrammType.L2,
        };
        concrete.Hull = hull;
        concrete.SetWKT();
        concrete.SliceXY(nx: 16, ny: 20);

        // 4 стержня сконцентрированы у угла (x0,y0) — асимметрия по X и по Y одновременно.
        var rebar = new MaterialArea
        {
            Tag = "rebar_corner_cluster",
            Category = AreaCategory.RebarGroup,
            Material = rebarMaterial,
            MaterialId = rebarMaterial.Id,
            DiagrammType = DiagrammType.L2,
            Fibers =
            [
                Fiber.CreatePoint(diam, x0 + 0.05, y0 + 0.05),
                Fiber.CreatePoint(diam, x0 + 0.12, y0 + 0.05),
                Fiber.CreatePoint(diam, x0 + 0.05, y0 + 0.12),
                Fiber.CreatePoint(diam, x0 + 0.12, y0 + 0.12),
            ],
        };

        var section = new CrossSection { Areas = [concrete, rebar] };
        section.ResolveAndBuildDiagramms(0.85, pool: null, rebarDifferentialDiagram: false);
        return section;
    }

    /// <summary>
    /// Уникальное сечение "Пример 47" (1150×300 мм, 6Ø14 понизу) — общая fixture, ранее
    /// продублированная как приватный <c>BuildUniaxialSection</c> внутри
    /// <c>BiaxialCurvatureCurveSolverTests</c>; вынесено сюда для переиспользования новыми
    /// тестами пин-решателей (см. план 2026-08-19-biaxial-curve-pin-solvers.md, Task 2 Step 1a).
    /// </summary>
    public static CrossSection Example47()
    {
        const double Height = 0.300;
        const double Width = 1.150;
        const double RebarDepth = 0.042;
        const double RebarDiameter = 0.014;
        const double RebarArea = 923e-6;

        var concreteMaterial = new Material
        {
            Id = 101, Tag = "B15", Type = MatType.Concrete, E = 24_000_000.0,
            MaterialChars =
            [
                Example47ConcreteChars(CalcType.C, false), Example47ConcreteChars(CalcType.CL, true),
                Example47ConcreteChars(CalcType.N, false), Example47ConcreteChars(CalcType.NL, true),
            ]
        };

        var x = new[] { -Width / 2, Width / 2, Width / 2, -Width / 2, -Width / 2 };
        var y = new[] { -Height / 2, -Height / 2, Height / 2, Height / 2, -Height / 2 };
        var concrete = new MaterialArea
        {
            Id = 101, Tag = "Бетон B15", Category = AreaCategory.Region,
            Material = concreteMaterial, MaterialId = concreteMaterial.Id,
            DiagrammType = DiagrammType.L3, Hull = new Contour(x, y, "hull")
        };
        concrete.SetWKT();
        concrete.SliceXY(nx: 24, ny: 12);

        var steelMaterial = new Material
        {
            Id = 102, Tag = "A400", Type = MatType.ReSteelF, E = 200_000_000.0,
            MaterialChars =
            [
                Example47RebarChars(CalcType.C), Example47RebarChars(CalcType.CL),
                Example47RebarChars(CalcType.N), Example47RebarChars(CalcType.NL),
            ]
        };

        var rebar = new MaterialArea
        {
            Id = 102, Tag = "6Ø14 снизу", Category = AreaCategory.RebarGroup,
            Material = steelMaterial, MaterialId = steelMaterial.Id,
            DiagrammType = DiagrammType.L2, HostArea = concrete, HostAreaId = concrete.Id
        };
        double yBar = -Height / 2 + RebarDepth;
        double barArea = RebarArea / 6.0;
        for (int i = 0; i < 6; i++)
        {
            var bar = Fiber.CreatePoint(RebarDiameter, -0.45 + i * 0.18, yBar);
            bar.Area = barArea;
            rebar.Fibers.Add(bar);
        }

        var section = new CrossSection { Id = 101, Tag = "Пример 47", Areas = [concrete, rebar] };
        section.ResolveAndBuildDiagramms(rebarDifferentialDiagram: false);
        return section;
    }

    /// <summary>
    /// Двухстадийное сечение: этап 1 — прямоугольник 0.3×0.5 с нижней арматурой,
    /// этап 2 — полка 0.6×0.15 поверх него.
    /// </summary>
    public static TwoStageSection TwoStageRectWithFlange()
    {
        var stage1 = RectWithBottomRebar();
        var concreteMaterial = TestMaterials.Concrete("B25");
        var flange = new MaterialArea
        {
            Tag = "flange",
            Category = AreaCategory.Region,
            Material = concreteMaterial,
            MaterialId = concreteMaterial.Id,
            DiagrammType = DiagrammType.L2
        };
        flange.Hull = new Contour(
            [-0.3, 0.3, 0.3, -0.3, -0.3],
            [0.25, 0.25, 0.40, 0.40, 0.25], "flange");
        flange.SetWKT();
        flange.SliceXY(nx: 12, ny: 6);

        var section = new TwoStageSection
        {
            Stage1 = stage1,
            Areas = [flange],
            Stage1Kurvature = new Kurvature { e0 = 0.0, ky = 0.0, kz = 0.0 }
        };
        section.ResolveAndBuildDiagramms(0.85, pool: null, rebarDifferentialDiagram: false);
        return section;
    }

    /// <summary>
    /// Прямоугольник 0.3×0.5 (B25) с напрягаемой арматурой РОВНО в приведённом центре
    /// тяжести (y = 0, x = ±0.1). Обжатие получается центральным: упругий отклик на
    /// преднапряжение — чистое e0 = −P/EA без кривизны, что даёт детерминированное
    /// ожидание для начального приближения <see cref="CrossSection.Guess"/>.
    /// </summary>
    public static CrossSection RectWithCentralPrestressedRebar(double sigSp = 500.0)
    {
        var concreteMaterial = TestMaterials.Concrete("B25");
        var rebarMaterial = TestMaterials.Rebar("A500");

        double h = 0.5, b = 0.3;
        double y0 = -h / 2.0, y1 = h / 2.0, x0 = -b / 2.0, x1 = b / 2.0;

        var concrete = new MaterialArea
        {
            Id = 1,
            Tag = "concrete",
            Category = AreaCategory.Region,
            Material = concreteMaterial,
            MaterialId = concreteMaterial.Id,
            DiagrammType = DiagrammType.L2,
            Hull = new Contour([x0, x1, x1, x0, x0], [y0, y0, y1, y1, y0], "outer"),
        };
        concrete.SetWKT();
        concrete.SliceXY(nx: 12, ny: 20);

        var strands = new MaterialArea
        {
            Id = 2,
            Tag = "strands",
            Category = AreaCategory.RebarGroup,
            Material = rebarMaterial,
            MaterialId = rebarMaterial.Id,
            DiagrammType = DiagrammType.L2,
            SigSp = sigSp,
            GammaSp = 1.0,
            Fibers =
            [
                Fiber.CreatePoint(0.02, -0.1, 0.0),
                Fiber.CreatePoint(0.02, 0.1, 0.0),
            ],
        };

        var section = new CrossSection { Id = 1, Tag = "central prestress", Areas = [concrete, strands] };
        section.ResolveAndBuildDiagramms(0.85, pool: null, rebarDifferentialDiagram: false);
        return section;
    }

    /// <summary>
    /// Прямоугольник 0.3×0.5 (B25, L3) с ненапрягаемой арматурой сверху и снизу и
    /// ЭКСЦЕНТРИЧНОЙ напрягаемой арматурой A1000 понизу (σ_sp = 900 МПа) — повторяет
    /// сечение из проекта пользователя, на котором решатель НДС расходился: начальное
    /// приближение промахивается на всю силу обжатия (≈500 кН и ≈110 кН·м).
    /// </summary>
    public static CrossSection RectWithEccentricPrestressedRebar(double sigSp = 900.0, double strandEpsSu = 0.015)
    {
        var concreteMaterial = TestMaterials.Concrete("B25");
        var rebarMaterial = TestMaterials.Rebar("A500");
        var strandMaterial = TestMaterials.PrestressedRebar("A1000", strandEpsSu);

        double h = 0.5, b = 0.3;
        double y0 = -h / 2.0, y1 = h / 2.0, x0 = -b / 2.0, x1 = b / 2.0;

        var concrete = new MaterialArea
        {
            Id = 1,
            Tag = "concrete",
            Category = AreaCategory.Region,
            Material = concreteMaterial,
            MaterialId = concreteMaterial.Id,
            DiagrammType = DiagrammType.L3,
            Hull = new Contour([x0, x1, x1, x0, x0], [y0, y0, y1, y1, y0], "outer"),
        };
        concrete.SetWKT();
        concrete.SliceXY(nx: 12, ny: 20);

        var rebar = new MaterialArea
        {
            Id = 2,
            Tag = "rebar",
            Category = AreaCategory.RebarGroup,
            Material = rebarMaterial,
            MaterialId = rebarMaterial.Id,
            DiagrammType = DiagrammType.L2,
            HostArea = concrete,
            HostAreaId = concrete.Id,
            Fibers =
            [
                Fiber.CreatePoint(0.016, -0.12, 0.22),
                Fiber.CreatePoint(0.016, 0.0, 0.22),
                Fiber.CreatePoint(0.016, 0.12, 0.22),
                Fiber.CreatePoint(0.02, -0.12, -0.22),
                Fiber.CreatePoint(0.02, 0.12, -0.22),
            ],
        };

        var strands = new MaterialArea
        {
            Id = 3,
            Tag = "strands",
            Category = AreaCategory.RebarGroup,
            Material = strandMaterial,
            MaterialId = strandMaterial.Id,
            DiagrammType = DiagrammType.L3,
            HostArea = concrete,
            HostAreaId = concrete.Id,
            SigSp = sigSp,
            GammaSp = 1.0,
            Fibers =
            [
                Fiber.CreatePoint(0.02, -0.04, -0.22),
                Fiber.CreatePoint(0.02, 0.04, -0.22),
            ],
        };

        var section = new CrossSection
        {
            Id = 2,
            Tag = "rect 300x500 ps",
            Areas = [concrete, rebar, strands],
        };
        section.ResolveAndBuildDiagramms(0.85, pool: null, rebarDifferentialDiagram: true);
        return section;
    }

    static MaterialChars Example47ConcreteChars(CalcType calc, bool longTerm) => longTerm
        ? new MaterialChars
        {
            Type = MatType.Concrete, TypeCalc = calc,
            Fc = -11_000, Ft = 1_100, E = 9_090_909.091,
            Ec0 = -0.0034, Ec1 = -0.00121, Ec2 = -0.0048, Ec1Red = -0.0028,
            Et1Red = 0.00022, Et0 = 0.00024, Et1 = 0.000121, Et2 = 0.00031
        }
        : new MaterialChars
        {
            Type = MatType.Concrete, TypeCalc = calc,
            Fc = -11_000, Ft = 1_100, E = 24_000_000,
            Ec0 = -0.002, Ec1 = -0.000275, Ec2 = -0.0035, Ec1Red = -0.0015,
            Et1Red = 0.00008, Et0 = 0.0001, Et1 = 0.0000275, Et2 = 0.00015
        };

    static MaterialChars Example47RebarChars(CalcType calc) => new()
    {
        Type = MatType.ReSteelF, TypeCalc = calc,
        Fc = -400_000, Ft = 400_000, Ry = 400_000, E = 200_000_000,
        Ec2 = -0.0035, Et2 = 0.025
    };
}
