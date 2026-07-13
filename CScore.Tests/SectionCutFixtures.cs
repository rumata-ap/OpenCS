using System.Collections.Generic;
using System.Linq;

namespace CScore.Tests;

/// <summary>Готовые CrossSection-фикстуры для тестов SectionCutBuilder.</summary>
internal static class SectionCutFixtures
{
    public static Material BuildConcreteMaterial(double e = 32_500_000, double fc = -17_000)
    {
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            E = e,
            Fc = fc,
            Ft = 1_200,
            Ec2 = -0.0035,
            Ec1Red = fc / e,
            Et1Red = 1_200.0 / e,
            Et2 = 0.00015,
            Type = MatType.Concrete
        };

        var m = new Material { Id = 10_001, Tag = "test-concrete", Type = MatType.Concrete, E = e };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        return m;
    }

    public static Material BuildSteelMaterial()
    {
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            E = 200_000_000,
            Fc = -435_000,
            Ft = 435_000,
            Ec2 = -0.025,
            Et2 = 0.025,
            Type = MatType.ReSteelF
        };

        var m = new Material { Id = 10_002, Tag = "test-steel-A500", Type = MatType.ReSteelF, E = 200_000_000 };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        return m;
    }

    static MaterialArea BuildRectArea(int id, string tag, double x0, double x1, double y0, double y1,
        Material material, int nx = 11, int ny = 11)
    {
        var hull = new Contour(
            new[] { x0, x1, x1, x0, x0 },
            new[] { y0, y0, y1, y1, y0 },
            tag)
        { Type = ContourType.Hull };

        var area = new MaterialArea
        {
            Id = id, Tag = tag, Category = AreaCategory.Region,
            Material = material, MaterialId = material.Id,
            DiagrammType = DiagrammType.L2, Contours = [hull], NX = nx, NY = ny
        };
        area.Hull = hull;
        area.ResolveAndBuildDiagramms();
        area.SliceXY(nx, ny);
        return area;
    }

    /// <summary>Прямоугольник width×height с центром в (0,0) и 4 угловыми стержнями Ø25.</summary>
    public static CrossSection BuildReinforcedRectangle(double width, double height, int nx = 11, int ny = 11)
    {
        var concrete = BuildConcreteMaterial();
        double x0 = -width / 2, x1 = width / 2, y0 = -height / 2, y1 = height / 2;
        var area = BuildRectArea(1, "rect", x0, x1, y0, y1, concrete, nx, ny);

        var steel = BuildSteelMaterial();
        double cover = 0.05, dia = 0.025;
        double rx = width / 2 - cover, ry = height / 2 - cover;
        var bars = new[]
        {
            Fiber.CreatePoint(dia, -rx, -ry), Fiber.CreatePoint(dia,  rx, -ry),
            Fiber.CreatePoint(dia,  rx,  ry), Fiber.CreatePoint(dia, -rx,  ry),
        };
        var rebar = MaterialArea.CreateRebarArea(bars, steel, DiagrammType.L2, area);

        return new CrossSection { Tag = "reinforced-rect", Areas = [area, rebar] };
    }

    /// <summary>Прямоугольное кольцо (outer hull + прямоугольная дыра в центре).</summary>
    public static CrossSection BuildHollowRectangle(double outerSize, double innerSize)
    {
        var concrete = BuildConcreteMaterial();
        double o = outerSize / 2, i = innerSize / 2;
        var hull = new Contour(new[] { -o, o, o, -o, -o }, new[] { -o, -o, o, o, -o }, "outer")
        { Type = ContourType.Hull };
        var hole = new Contour(new[] { -i, i, i, -i, -i }, new[] { -i, -i, i, i, -i }, "inner")
        { Type = ContourType.Hole };

        var area = new MaterialArea
        {
            Id = 1, Tag = "hollow", Category = AreaCategory.Region,
            Material = concrete, MaterialId = concrete.Id,
            DiagrammType = DiagrammType.L2, Contours = [hull, hole], NX = 21, NY = 21
        };
        area.Hull = hull;
        area.ResolveAndBuildDiagramms();
        area.SliceXY(21, 21);

        return new CrossSection { Tag = "hollow-rect", Areas = [area] };
    }

    /// <summary>Два прямоугольника width×heightEach друг над другом с разным E (для проверки скачка на границе).</summary>
    public static CrossSection BuildTwoMaterialStack(double width, double heightEach)
    {
        double x0 = -width / 2, x1 = width / 2;
        var lower = BuildConcreteMaterial(e: 32_500_000);
        var upper = BuildConcreteMaterial(e: 20_000_000);

        var areaLower = BuildRectArea(1, "lower", x0, x1, 0, heightEach, lower);
        var areaUpper = BuildRectArea(2, "upper", x0, x1, heightEach, 2 * heightEach, upper);

        return new CrossSection { Tag = "two-material-stack", Areas = [areaLower, areaUpper] };
    }

    /// <summary>TwoStageSection: Stage1 (0..h) + базовая область Stage2 (h..2h), разные Kurvature.</summary>
    public static TwoStageSection BuildTwoStageSection(double width, double heightEach)
    {
        double x0 = -width / 2, x1 = width / 2;
        var concrete = BuildConcreteMaterial();

        var stage1Area = BuildRectArea(1, "stage1", x0, x1, 0, heightEach, concrete);
        var stage1Section = new CrossSection { Tag = "stage1", Areas = [stage1Area] };

        var stage2Area = BuildRectArea(2, "stage2", x0, x1, heightEach, 2 * heightEach, concrete);

        return new TwoStageSection
        {
            Tag = "two-stage",
            Stage1 = stage1Section,
            Stage1Kurvature = new Kurvature { e0 = -0.0001, ky = 0, kz = 0 },
            Areas = [stage2Area]
        };
    }
}
