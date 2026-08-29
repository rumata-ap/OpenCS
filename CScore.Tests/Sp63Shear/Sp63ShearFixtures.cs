using CScore;

namespace CScore.Tests.Sp63Shear;

/// <summary>
/// Общие тестовые сечения и материалы для расчёта наклонных сечений.
/// Характеристики задаются через свойства <see cref="Material.C"/> и т.п.: только они
/// наполняют словарь, из которого читает <see cref="Material.GetChars"/>.
/// </summary>
internal static class Sp63ShearFixtures
{
    /// <summary>Прочность арматуры A500 на растяжение, кПа.</summary>
    public const double RsA500 = 435_000.0;

    /// <summary>Создаёт бетонный материал; Fc задаётся отрицательным (сжатие — «минус»).</summary>
    public static Material Concrete(int id, double rb, double rbt)
    {
        var material = new Material
        {
            Id = id,
            Tag = $"B-{id}",
            Type = MatType.Concrete,
            E = 30_000_000.0
        };
        material.C = new MaterialChars(CalcType.C)
        {
            Type = MatType.Concrete,
            Fc = -rb,
            Ft = rbt,
            E = 30_000_000.0,
            Ec1Red = -0.0015,
            Ec2 = -0.0035,
            Et1Red = 0.00008,
            Et2 = 0.00015
        };
        return material;
    }

    /// <summary>Создаёт арматурный материал с заданным Rs (Ft), кПа.</summary>
    public static Material Rebar(int id, double rs = RsA500, double materialClass = 500.0)
    {
        var material = new Material
        {
            Id = id,
            Tag = $"A-{id}",
            Type = MatType.ReSteelU,
            E = 200_000_000.0
        };
        material.C = new MaterialChars(CalcType.C)
        {
            Type = MatType.ReSteelU,
            Class = materialClass,
            Fc = -rs,
            Ft = rs,
            E = 200_000_000.0,
            Ec2 = -0.0035,
            Et2 = 0.025
        };
        return material;
    }

    /// <summary>Создаёт бетонную область по вершинам контура.</summary>
    public static MaterialArea ConcreteRegion(Material material, (double X, double Y)[] vertices)
    {
        var xs = new List<double>();
        var ys = new List<double>();
        foreach (var (x, y) in vertices) { xs.Add(x); ys.Add(y); }
        xs.Add(vertices[0].X);
        ys.Add(vertices[0].Y);

        var area = new MaterialArea
        {
            Category = AreaCategory.Region,
            Material = material,
            MaterialId = material.Id
        };
        area.Contours.Add(new Contour(xs, ys, "hull") { Type = ContourType.Hull });
        area.SetWKT();
        return area;
    }

    /// <summary>Добавляет в сечение точечный стержень продольной арматуры.</summary>
    public static void AddRebar(
        CrossSection section, double x, double y, double area, double rs = RsA500)
    {
        var material = Rebar((int)(rs / 1000.0), rs);
        var rebarArea = new MaterialArea
        {
            Category = AreaCategory.RebarGroup,
            Material = material,
            MaterialId = material.Id
        };
        rebarArea.Fibers.Add(new Fiber { TypeFiber = FiberType.point, X = x, Y = y, Area = area });
        section.Areas.Add(rebarArea);
    }

    /// <summary>Балка 0,30 × 0,60 с арматурой снизу и сверху.</summary>
    public static CrossSection Beam(double bottomRebarY, double topRebarY)
    {
        var section = new CrossSection { Tag = "Б-1" };
        section.Areas.Add(ConcreteRegion(
            Concrete(11_500, 11_500.0, 900.0),
            [(-0.15, -0.30), (0.15, -0.30), (0.15, 0.30), (-0.15, 0.30)]));
        AddRebar(section, x: -0.05, y: bottomRebarY, area: 0.001);
        AddRebar(section, x: -0.05, y: topRebarY, area: 0.0005);
        return section;
    }
}
