namespace CScore.ParametricRc;

/// <summary>Поддерживаемая параметрическая форма железобетонного сечения.</summary>
public enum ParametricRcShape { Rectangle, Tee, IBeam, Circle, Annulus }

/// <summary>Способ материализации продольной арматуры параметрического источника.</summary>
public enum ParametricRebarMode { PhysicalBars, IdealizedLayer }

/// <summary>Зона поперечной арматуры типовой формы.</summary>
public enum ParametricStirrupZone { Body, Flange, TopFlange, BottomFlange, Web }

/// <summary>Ориентация открытого среза поперечной арматуры.</summary>
public enum ParametricStirrupDirection { Vertical, Horizontal }

/// <summary>Один набор открытых срезов с собственным материалом и шагом.</summary>
public sealed record ParametricStirrupCutSet(ParametricStirrupZone Zone,
    ParametricStirrupDirection Direction, int Count, double DiameterM,
    double SpacingM, double CoverM, int MaterialId)
{
    /// <summary>Шаг набора срезов; имя-псевдоним для API параметрического мастера.</summary>
    public double StepM => SpacingM;
}

/// <summary>Продольный слой параметрического сечения.</summary>
public sealed record ParametricLongitudinalLayer(
    bool Enabled, bool IsIdealized, int Count, double DiameterM, double CoordinateM,
    double AreaM2, IdealizedRebarAxis? Axis)
{
    /// <summary>Режим представления слоя в сформированной области.</summary>
    public ParametricRebarMode Mode => IsIdealized
        ? ParametricRebarMode.IdealizedLayer
        : ParametricRebarMode.PhysicalBars;

    /// <summary>Создаёт ряд физических стержней.</summary>
    public static ParametricLongitudinalLayer Physical(int count, double diameterM, double coordinateM) =>
        new(true, false, count, diameterM, coordinateM, 0.0, null);

    /// <summary>Создаёт расчётный слой с заданной площадью.</summary>
    public static ParametricLongitudinalLayer Idealized(double areaM2, double diameterM,
        double coordinateM, IdealizedRebarAxis axis) =>
        new(true, true, 1, diameterM, coordinateM, areaM2, axis);
}

/// <summary>Равномерная полярная раскладка физических стержней.</summary>
public sealed record ParametricPolarRebar(int Count, double DiameterM, double RadiusM);

/// <summary>
/// Отдельный продольный стержень, заданный координатами (армирование по данным схемы-источника: угловые и боковые
/// стержни подбора, второй диаметр и второй ряд заданного армирования).
/// </summary>
/// <param name="X">Абсцисса центра, м.</param>
/// <param name="Y">Ордината центра, м.</param>
/// <param name="AreaM2">Площадь, м² (у распределённой арматуры — доля площади грани).</param>
/// <param name="DiameterM">Диаметр (у распределённой арматуры — эквивалентный по площади), м.</param>
public sealed record ParametricRebarPoint(double X, double Y, double AreaM2, double DiameterM);

/// <summary>
/// Контурная раскладка продольной арматуры прямоугольного сечения: четыре угловых стержня и промежуточные
/// стержни на каждой из четырёх граней (равномерно между угловыми), как у стержневых сечений из импорта.
/// </summary>
/// <param name="CornerDiameterM">Диаметр угловых стержней, м.</param>
/// <param name="IntermediateDiameterM">Диаметр промежуточных стержней, м.</param>
/// <param name="CoverM">Расстояние от грани бетона до центра стержней, м (центр углового стержня — в (c, c)).</param>
/// <param name="CountAlongWidth">Число промежуточных стержней на каждой горизонтальной грани (верх и низ).</param>
/// <param name="CountAlongHeight">Число промежуточных стержней на каждой вертикальной грани (лево и право).</param>
public sealed record ParametricPerimeterRebar(
    double CornerDiameterM, double IntermediateDiameterM, double CoverM,
    int CountAlongWidth, int CountAlongHeight);

/// <summary>Параметрический источник типового железобетонного сечения в единицах СИ.</summary>
public sealed record ParametricRcSectionDefinition(
    ParametricRcShape Shape, double WidthM, double HeightM, double WebThicknessM,
    double FlangeThicknessM, double InnerDiameterM, string Tag,
    ParametricLongitudinalLayer? UpperRebar, ParametricLongitudinalLayer? LowerRebar,
    ParametricPolarRebar? PolarRebar)
{
    /// <summary>Версия JSON-контракта исходного описания.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Id материала бетона в проекте.</summary>
    public int ConcreteMaterialId { get; init; }
    /// <summary>Id материала продольной арматуры в проекте.</summary>
    public int LongitudinalMaterialId { get; init; }
    /// <summary>Необязательные наборы открытых срезов поперечной арматуры.</summary>
    public IReadOnlyList<ParametricStirrupCutSet> StirrupCuts { get; init; } = [];
    /// <summary>
    /// Дополнительные продольные стержни (материал — <see cref="LongitudinalMaterialId"/>), кроме верхнего и нижнего
    /// рядов; не для круга и кольца. Записи без поля читаются как пустой список.
    /// </summary>
    public IReadOnlyList<ParametricRebarPoint> ExtraBars { get; init; } = [];
    /// <summary>
    /// Контурная раскладка «углы + промежуточные стержни» (только прямоугольник; взаимоисключающе с нижним и
    /// верхним рядами). Записи без поля читаются как null.
    /// </summary>
    public ParametricPerimeterRebar? PerimeterRebar { get; init; }
    /// <summary>Создаёт прямоугольное сечение.</summary>
    public static ParametricRcSectionDefinition Rectangle(double widthM, double heightM) =>
        new(ParametricRcShape.Rectangle, widthM, heightM, 0, 0, 0, "Параметрическое сечение", null, null, null);

    /// <summary>Создаёт симметричный тавр.</summary>
    public static ParametricRcSectionDefinition Tee(double widthM, double heightM, double webThicknessM, double flangeThicknessM) =>
        new(ParametricRcShape.Tee, widthM, heightM, webThicknessM, flangeThicknessM, 0, "Параметрическое сечение", null, null, null);

    /// <summary>Создаёт симметричный двутавр.</summary>
    public static ParametricRcSectionDefinition IBeam(double widthM, double heightM, double webThicknessM, double flangeThicknessM) =>
        new(ParametricRcShape.IBeam, widthM, heightM, webThicknessM, flangeThicknessM, 0, "Параметрическое сечение", null, null, null);

    /// <summary>Создаёт круг.</summary>
    public static ParametricRcSectionDefinition Circle(double diameterM) =>
        new(ParametricRcShape.Circle, diameterM, diameterM, 0, 0, 0, "Параметрическое сечение", null, null, null);

    /// <summary>Создаёт кольцо.</summary>
    public static ParametricRcSectionDefinition Annulus(double outerDiameterM, double innerDiameterM) =>
        new(ParametricRcShape.Annulus, outerDiameterM, outerDiameterM, 0, 0, innerDiameterM, "Параметрическое сечение", null, null, null);
}
