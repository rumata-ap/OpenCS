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
}
