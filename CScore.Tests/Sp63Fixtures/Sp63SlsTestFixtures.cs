using CScore;
using CScore.Sp63.CrackWidth;
using CScore.Sp63.Deflection;
using CScore.Sp63.Normal;

namespace CScore.Tests.Sp63Fixtures;

/// <summary>
/// Общие тестовые сечения для SLS-задач СП 63 (ширина трещин, кривизна, прогиб), используемые
/// тестами папок Sp63CrackWidth и Sp63Deflection. Все builders создают реальные
/// <see cref="CrossSection"/>: одна бетонная область с замкнутым осевым контуром (координаты
/// профиля от <c>y = 0</c> до <c>y = H</c>) и два point-fiber уровня ненапрягаемой арматуры
/// на <c>y = 0.04</c> и <c>y = H − 0.04</c>. Верхний слой несёт As = 20.36e-4 м² (растянут
/// при положительном моменте), нижний — A's = 1e-8 м².
/// </summary>
internal static class Sp63SlsTestSections
{
    /// <summary>Площадь верхнего слоя (As, растянут при Mx &gt; 0), м².</summary>
    public const double TopLayerArea = 20.36e-4;

    /// <summary>Площадь нижнего слоя (A's), м².</summary>
    public const double BottomLayerArea = 1e-8;

    /// <summary>Диаметр стержней обоих слоёв, м.</summary>
    public const double BarDiameter = 0.036;

    /// <summary>Привязка слоёв арматуры к ближайшей грани, м.</summary>
    public const double Cover = 0.04;

    /// <summary>Тяжёлый бетон B25 для SLS: E = 30 000 000, Fc = −18 500, Ft = 1 550, Class = 25, кПа.</summary>
    public static Material B25() => ConcreteMaterial(25);

    /// <summary>Бетон без класса (Class = 0) — для тестов кривизны по таблицам 6.10/6.12.</summary>
    public static Material B25WithoutClass() => ConcreteMaterial(0);

    /// <summary>Арматура A400 для SLS: E = 200 000 000, Ft = 500 000, Fc = −500 000, кПа.</summary>
    public static Material A400() => RebarMaterial(withChars: true);

    static Material ConcreteMaterial(double concreteClass)
    {
        var material = new Material
        {
            Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000.0
        };
        material.N = new MaterialChars(CalcType.N)
        {
            Type = MatType.Concrete,
            Class = concreteClass,
            Fc = -18_500.0,
            Ft = 1_550.0,
            E = 30_000_000.0
        };
        return material;
    }

    static Material RebarMaterial(bool withChars)
    {
        var material = new Material
        {
            Id = 2, Tag = "A400", Type = MatType.ReSteelU, E = 200_000_000.0
        };
        if (withChars)
        {
            material.N = new MaterialChars(CalcType.N)
            {
                Type = MatType.ReSteelU,
                Ft = 500_000.0,
                Fc = -500_000.0,
                E = 200_000_000.0
            };
        }
        return material;
    }

    /// <summary>Прямоугольник: одна полоса width × height, слои y = 0.04 и y = height − 0.04.</summary>
    public static CrossSection Rectangle(double width = 0.5, double height = 0.3,
        double topLayerArea = TopLayerArea, double bottomLayerArea = BottomLayerArea) =>
        RectangleLike(width, height, topLayerArea, bottomLayerArea,
            RectContour(width, height));

    /// <summary>
    /// Тавр с полкой ВЕРХУ: web x = [−webWidth/2; webWidth/2], y = [0; height − flangeHeight],
    /// верхняя полка x = [−width/2; width/2], y = [height − flangeHeight; height].
    /// Полка находится в сжатой зоне при Mx &lt; 0.
    /// </summary>
    public static CrossSection TopTee(double width = 0.6, double height = 0.6,
        double webWidth = 0.2, double flangeHeight = 0.15,
        double topLayerArea = TopLayerArea, double bottomLayerArea = BottomLayerArea)
    {
        double bw2 = webWidth / 2.0, w2 = width / 2.0, yf = height - flangeHeight;
        return RectangleLike(width, height, topLayerArea, bottomLayerArea,
            [[-bw2, 0.0], [bw2, 0.0], [bw2, yf], [w2, yf], [w2, height],
             [-w2, height], [-w2, yf], [-bw2, yf], [-bw2, 0.0]]);
    }

    /// <summary>
    /// Двутавр: нижняя полка y = [0; bottomFlangeHeight], web y = [bottomFlangeHeight;
    /// height − topFlangeHeight], верхняя полка y = [height − topFlangeHeight; height].
    /// </summary>
    public static CrossSection ISection(double width = 0.6, double height = 0.6,
        double webWidth = 0.2, double topFlangeHeight = 0.1, double bottomFlangeHeight = 0.1,
        double topLayerArea = TopLayerArea, double bottomLayerArea = BottomLayerArea)
    {
        double bw2 = webWidth / 2.0, w2 = width / 2.0;
        double yb = bottomFlangeHeight, yt = height - topFlangeHeight;
        return RectangleLike(width, height, topLayerArea, bottomLayerArea,
            [[-w2, 0.0], [w2, 0.0], [w2, yb], [bw2, yb], [bw2, yt], [w2, yt], [w2, height],
             [-w2, height], [-w2, yt], [-bw2, yt], [-bw2, yb], [-w2, yb], [-w2, 0.0]]);
    }

    /// <summary>Прямоугольник с одним уровнем арматуры (y = 0.04).</summary>
    public static CrossSection RectangleOneLayer(double width = 0.5, double height = 0.3)
    {
        var section = Build(B25(), RectContour(width, height));
        AddBar(section, Cover, TopLayerArea, A400());
        return section;
    }

    /// <summary>Прямоугольник с тремя уровнями арматуры: 0.04, height/2 и height − 0.04.</summary>
    public static CrossSection RectangleThreeLayers(double width = 0.5, double height = 0.3)
    {
        var section = Rectangle(width, height);
        AddBar(section, height / 2.0, TopLayerArea, A400());
        return section;
    }

    /// <summary>Прямоугольник с отверстием в бетонной области.</summary>
    public static CrossSection RectangleWithHole(double width = 0.5, double height = 0.3)
    {
        var section = Rectangle(width, height);
        var area = section.Areas.First(a => a.Category == AreaCategory.Region);
        area.Contours.Add(new Contour(
            [-0.05, 0.05, 0.05, -0.05, -0.05],
            [0.10, 0.10, 0.20, 0.20, 0.10], "hole")
        { Type = ContourType.Hole });
        area.SetWKT();
        return section;
    }

    /// <summary>Прямоугольник с напрягаемой арматурой (SigSp ≠ 0).</summary>
    public static CrossSection RectanglePrestressed(double width = 0.5, double height = 0.3)
    {
        var section = Rectangle(width, height);
        var group = section.Areas.First(a => a.Category == AreaCategory.RebarGroup);
        group.SigSp = 100_000.0;
        return section;
    }

    /// <summary>Прямоугольник, у бетона которого нет характеристик для CalcType.N.</summary>
    public static CrossSection RectangleMissingConcreteChars(double width = 0.5, double height = 0.3)
    {
        var concrete = new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000.0 };
        return RectangleLike(width, height, TopLayerArea, BottomLayerArea,
            RectContour(width, height), concrete);
    }

    /// <summary>Прямоугольник, у арматуры которого нет характеристик для CalcType.N.</summary>
    public static CrossSection RectangleMissingRebarChars(double width = 0.5, double height = 0.3)
    {
        var rebar = RebarMaterial(withChars: false);
        return RectangleLike(width, height, TopLayerArea, BottomLayerArea,
            RectContour(width, height), rebar: rebar);
    }

    /// <summary>Прямоугольник без класса бетона (Class = 0) — для длительной кривизны.</summary>
    public static CrossSection RectangleNoConcreteClass(double width = 0.5, double height = 0.3)
    {
        var concrete = B25WithoutClass();
        return RectangleLike(width, height, TopLayerArea, BottomLayerArea,
            RectContour(width, height), concrete);
    }

    /// <summary>
    /// Тонкий helper над <see cref="Sp63SlsSectionGeometryFactory.TryCreate"/> для Rectangle;
    /// при ошибке выбрасывает <see cref="InvalidOperationException"/> с кодами сообщений.
    /// </summary>
    public static Sp63SlsSectionGeometry GeometryRectangle(double width = 0.5, double height = 0.3,
        int tensionDirection = 1) =>
        CreateOrFail(Rectangle(width, height), Sp63NormalShapeKind.Rectangular,
            Sp63NormalAxis.Mx, tensionDirection);

    /// <summary>
    /// Тонкий helper над <see cref="Sp63SlsSectionGeometryFactory.TryCreate"/> для TopTee;
    /// при ошибке выбрасывает <see cref="InvalidOperationException"/> с кодами сообщений.
    /// По умолчанию сечение ориентировано полкой в сжатой зоне (tensionDirection = −1) —
    /// основной сценарий этой фикстуры.
    /// </summary>
    public static Sp63SlsSectionGeometry GeometryTopTee(double width = 0.6, double height = 0.6,
        double webWidth = 0.2, double flangeHeight = 0.15, int tensionDirection = -1) =>
        CreateOrFail(TopTee(width, height, webWidth, flangeHeight), Sp63NormalShapeKind.Tee,
            Sp63NormalAxis.Mx, tensionDirection);

    /// <summary>Helper над фабрикой профиля для произвольного сечения и направления.</summary>
    public static Sp63SlsSectionGeometry GeometryOrFail(CrossSection section,
        Sp63NormalShapeKind shapeKind, Sp63NormalAxis axis, int tensionDirection) =>
        CreateOrFail(section, shapeKind, axis, tensionDirection);

    static Sp63SlsSectionGeometry CreateOrFail(CrossSection section,
        Sp63NormalShapeKind shapeKind, Sp63NormalAxis axis, int tensionDirection)
    {
        if (!Sp63SlsSectionGeometryFactory.TryCreate(section, shapeKind, axis, CalcType.N,
                tensionDirection, out var geometry, out var messages))
            throw new InvalidOperationException("Sp63SlsSectionGeometryFactory: " +
                string.Join("; ", messages.Select(message => $"{message.Code}/{message.Text}")));
        return geometry!;
    }

    /// <summary>Строит осевой контур с двумя слоями арматуры; профиль от y = 0 до y = height.</summary>
    static CrossSection RectangleLike(double width, double height,
        double topLayerArea, double bottomLayerArea, double[][] contour,
        Material? concrete = null, Material? rebar = null)
    {
        var section = Build(concrete ?? B25(), contour);
        AddBar(section, Cover, bottomLayerArea, rebar ?? A400());
        AddBar(section, height - Cover, topLayerArea, rebar ?? A400());
        return section;
    }

    /// <summary>
    /// Прямоугольник для оси My: уровни арматуры лежат вдоль X (координата высоты для My),
    /// крайний по положительной оси уровень несёт As = 20.36e-4 м².
    /// </summary>
    public static CrossSection RectangleMy(double width = 0.3, double height = 0.5)
    {
        var section = Build(B25(),
            [[0.0, -height / 2], [width, -height / 2], [width, height / 2],
             [0.0, height / 2], [0.0, -height / 2]]);
        AddBarAt(section, Cover, 0.0, BottomLayerArea, A400());
        AddBarAt(section, width - Cover, 0.0, TopLayerArea, A400());
        return section;
    }

    /// <summary>Замкнутый осевой контур прямоугольника: x = [−width/2; width/2], y = [0; height].</summary>
    static double[][] RectContour(double width, double height) =>
        [[-width / 2, 0.0], [width / 2, 0.0], [width / 2, height],
         [-width / 2, height], [-width / 2, 0.0]];

    static CrossSection Build(Material concrete, double[][] contour)
    {
        var section = new CrossSection { Tag = "sls-test" };
        var area = new MaterialArea
        {
            Category = AreaCategory.Region,
            Material = concrete,
            MaterialId = concrete.Id
        };
        area.Contours.Add(new Contour(
            contour.Select(point => point[0]).ToList(),
            contour.Select(point => point[1]).ToList(), "hull")
        { Type = ContourType.Hull });
        area.SetWKT();
        section.Areas.Add(area);
        return section;
    }

    static void AddBar(CrossSection section, double y, double area, Material rebar) =>
        AddBarAt(section, 0.0, y, area, rebar);

    static void AddBarAt(CrossSection section, double x, double y, double area, Material rebar)
    {
        var group = new MaterialArea
        {
            Category = AreaCategory.RebarGroup,
            Material = rebar,
            MaterialId = rebar.Id
        };
        group.Fibers.Add(new Fiber
        {
            TypeFiber = FiberType.point, X = x, Y = y, Area = area, Diameter = BarDiameter
        });
        section.Areas.Add(group);
    }
}

/// <summary>
/// Тестовые опции формульных SLS-задач СП 63. Сигнатуры default-вызовов зафиксированы планом:
/// <c>CrackWidth(shapeKind = Rectangular, axis = Mx, mode = SingleTerm, phi1 = 1.0, phi2 = 0.5,
/// limit = 0.4 мм)</c>, <c>Deflection(shapeKind = Rectangular, scheme = SimplySupportedUniform,
/// axis = Mx, span = 6 м, limit = 20 мм, forcesMode = Manual, longTermShare = 1.0)</c>.
/// </summary>
internal static class Sp63SlsTestOptions
{
    /// <summary>Опции упрощённой проверки ширины раскрытия трещин.</summary>
    public static Sp63CrackWidthOptions CrackWidth(
        Sp63NormalShapeKind shapeKind = Sp63NormalShapeKind.Rectangular,
        Sp63NormalAxis axis = Sp63NormalAxis.Mx,
        Sp63CrackWidthMode mode = Sp63CrackWidthMode.SingleTerm,
        double phi1 = 1.0, double phi2 = 0.5, double limit = 0.4) =>
        new(shapeKind, axis, phi1, phi2, limit,
            Mode: mode, LongTermShare: 1.0, AcrcLimShortMm: 0.4);

    /// <summary>Опции формульного расчёта прогиба.</summary>
    public static Sp63DeflectionOptions Deflection(
        Sp63NormalShapeKind shapeKind = Sp63NormalShapeKind.Rectangular,
        Sp63DeflectionStaticScheme scheme = Sp63DeflectionStaticScheme.SimplySupportedUniform,
        Sp63NormalAxis axis = Sp63NormalAxis.Mx,
        double span = 6.0, double limit = 20.0,
        Sp63DeflectionForcesMode forcesMode = Sp63DeflectionForcesMode.Manual,
        double longTermShare = 1.0) =>
        new(shapeKind, axis, scheme, span, limit, Sp63Humidity.From40To75,
            forcesMode, longTermShare);
}
