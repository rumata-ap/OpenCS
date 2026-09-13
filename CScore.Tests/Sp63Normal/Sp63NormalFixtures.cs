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
        double area, Material rebar, double sigSp = 0.0, double diameter = 0.016)
    {
        var group = new MaterialArea
        {
            Category = AreaCategory.RebarGroup,
            Material = rebar,
            MaterialId = rebar.Id,
            SigSp = sigSp
        };
        group.Fibers.Add(new Fiber
        {
            TypeFiber = FiberType.point,
            X = x,
            Y = y,
            Area = area,
            Diameter = diameter
        });
        section.Areas.Add(group);
    }

    /// <summary>Создаёт прямоугольник с двумя симметричными слоями арматуры.</summary>
    public static CrossSection TwoLayerRectangle(double width, double height,
        double tensionArea, double compressionArea, double sigSp = 0.0,
        bool useDifferentRebar = false, double diameter = 0.016)
    {
        var section = Rectangle(width, height);
        AddBar(section, -width / 4, -height / 2 + 0.05, tensionArea,
            Rebar(2), sigSp, diameter);
        AddBar(section, width / 4, -height / 2 + 0.05, tensionArea,
            useDifferentRebar ? Rebar(3, rs: 400_000.0) : Rebar(2), sigSp, diameter);
        AddBar(section, -width / 4, height / 2 - 0.05, compressionArea,
            Rebar(2), sigSp, diameter);
        AddBar(section, width / 4, height / 2 - 0.05, compressionArea,
            Rebar(2), sigSp, diameter);
        return section;
    }

    /// <summary>Создаёт тавровое сечение с двумя слоями арматуры (по два стержня в слое).</summary>
    public static CrossSection Tee(double webWidth, double height, double flangeWidth,
        double flangeThickness, bool flangeOnTop, double tensionY, double compressionY,
        double tensionArea, double compressionArea, bool rotateForMy = false,
        double sigSp = 0.0, double rb = 14_500.0)
    {
        double halfWeb = webWidth / 2;
        var section = new CrossSection { Tag = "tee" };
        section.Areas.Add(ConcreteRegion(Concrete(rb: rb), TeeVertices(webWidth, height,
            flangeWidth, flangeThickness, flangeOnTop, rotateForMy)));
        var steel = Rebar(2);
        AddLayer(section, halfWeb / 2, tensionY, tensionArea / 2.0, steel, sigSp,
            rotateForMy);
        AddLayer(section, halfWeb / 2, compressionY, compressionArea / 2.0, steel,
            sigSp, rotateForMy);
        return section;
    }

    static (double X, double Y)[] TeeVertices(double webWidth, double height,
        double flangeWidth, double flangeThickness, bool flangeOnTop, bool rotateForMy)
    {
        double xf = flangeWidth / 2, xw = webWidth / 2;
        double y0 = -height / 2, y1 = height / 2;
        double yf = flangeOnTop ? y1 - flangeThickness : y0 + flangeThickness;
        (double X, double Y)[] vertices = flangeOnTop
            ? [(-xf, y1), (xf, y1), (xf, yf), (xw, yf),
               (xw, y0), (-xw, y0), (-xw, yf), (-xf, yf)]
            : [(-xf, y0), (xf, y0), (xf, yf), (xw, yf),
               (xw, y1), (-xw, y1), (-xw, yf), (-xf, yf)];
        return rotateForMy
            ? vertices.Select(vertex => (vertex.Y, vertex.X)).ToArray()
            : vertices;
    }

    static void AddLayer(CrossSection section, double halfWidth, double heightCoordinate,
        double area, Material steel, double sigSp, bool rotateForMy)
    {
        if (rotateForMy)
        {
            AddBar(section, heightCoordinate, -halfWidth, area, steel, sigSp);
            AddBar(section, heightCoordinate, halfWidth, area, steel, sigSp);
        }
        else
        {
            AddBar(section, -halfWidth, heightCoordinate, area, steel, sigSp);
            AddBar(section, halfWidth, heightCoordinate, area, steel, sigSp);
        }
    }

    /// <summary>Создаёт контекст элемента для тестов нормального расчёта.</summary>
    public static CScore.Sp63.Normal.Sp63NormalOptions MemberOptions() => new(
        CScore.Sp63.Normal.Sp63NormalShapeKind.Rectangular,
        CScore.Sp63.Normal.Sp63NormalAxis.Mx,
        new CScore.Sp63.Normal.Sp63MemberContext(
            6.0,
            CScore.Sp63.Normal.Sp63StructuralScheme.StaticallyIndeterminate,
            4.2,
            CScore.Sp63.Normal.Sp63NormalStabilityMode.Member,
            1.0));
}
