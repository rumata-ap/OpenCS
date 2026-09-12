using CScore;

namespace CScore.Tests.Sp63Normal;

/// <summary>Тестовые материалы и сечения для упрощённых проверок СП 63.</summary>
internal static class Sp63NormalFixtures
{
    /// <summary>Создаёт бетонный материал с характеристиками для расчёта C.</summary>
    public static Material Concrete(int id = 1, double rb = 20_000.0)
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
            Ft = 1_000.0,
            E = 30_000_000.0,
            Ec1Red = -0.0015,
            Ec2 = -0.0035,
            Et1Red = 0.00008,
            Et2 = 0.00015
        };
        return material;
    }

    /// <summary>Создаёт арматурный материал с заданными сопротивлениями.</summary>
    public static Material Rebar(int id, double rs = 435_000.0,
        double rsc = 435_000.0)
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
            Ft = rs,
            Fc = -rsc,
            E = 200_000_000.0,
            Et2 = 0.025,
            Ec2 = -0.0035
        };
        return material;
    }

    /// <summary>Создаёт осевой прямоугольник без арматуры.</summary>
    public static CrossSection Rectangle(double width, double height)
    {
        var section = new CrossSection { Tag = "test" };
        section.Areas.Add(ConcreteRegion(Concrete(), [
            (-width / 2, -height / 2),
            ( width / 2, -height / 2),
            ( width / 2,  height / 2),
            (-width / 2,  height / 2)
        ]));
        return section;
    }

    /// <summary>Добавляет бетонную область с заданным замкнутым контуром.</summary>
    public static MaterialArea ConcreteRegion(Material material,
        (double X, double Y)[] vertices)
    {
        var xs = vertices.Select(vertex => vertex.X).ToList();
        var ys = vertices.Select(vertex => vertex.Y).ToList();
        xs.Add(vertices[0].X);
        ys.Add(vertices[0].Y);
        if (xs.Count < 5)
        {
            xs.Add(vertices[0].X);
            ys.Add(vertices[0].Y);
        }

        var area = new MaterialArea
        {
            Category = AreaCategory.Region,
            Material = material,
            MaterialId = material.Id
        };
        area.Contours.Add(new Contour(xs, ys, "hull")
        {
            Type = ContourType.Hull
        });
        area.SetWKT();
        return area;
    }

    /// <summary>Добавляет точечный стержень продольной арматуры.</summary>
    public static void AddBar(CrossSection section, double x, double y,
        double area, Material rebar)
    {
        var group = new MaterialArea
        {
            Category = AreaCategory.RebarGroup,
            Material = rebar,
            MaterialId = rebar.Id
        };
        group.Fibers.Add(new Fiber
        {
            TypeFiber = FiberType.point,
            X = x,
            Y = y,
            Area = area
        });
        section.Areas.Add(group);
    }
}
