namespace CScore.ParametricRc;

/// <summary>Поддерживаемая параметрическая форма железобетонного сечения.</summary>
public enum ParametricRcShape { Rectangle, Tee, IBeam, Circle, Annulus }

/// <summary>Зона поперечной арматуры типовой формы.</summary>
public enum ParametricStirrupZone { Body, Flange, TopFlange, BottomFlange, Web }

/// <summary>Ориентация открытого среза поперечной арматуры.</summary>
public enum ParametricStirrupDirection { Vertical, Horizontal }

/// <summary>Один набор открытых срезов с собственным материалом и шагом.</summary>
public sealed record ParametricStirrupCutSet(ParametricStirrupZone Zone,
    ParametricStirrupDirection Direction, int Count, double DiameterM,
    double SpacingM, double CoverM, int MaterialId);

/// <summary>Продольный слой параметрического сечения.</summary>
public sealed record ParametricLongitudinalLayer(
    bool Enabled, bool IsIdealized, int Count, double DiameterM, double CoordinateM,
    double AreaM2, IdealizedRebarAxis? Axis)
{
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

/// <summary>Параметрический источник типового железобетонного сечения в единицах СИ.</summary>
public sealed record ParametricRcSectionDefinition(
    ParametricRcShape Shape, double WidthM, double HeightM, double WebThicknessM,
    double FlangeThicknessM, double InnerDiameterM, string Tag,
    ParametricLongitudinalLayer? UpperRebar, ParametricLongitudinalLayer? LowerRebar,
    ParametricPolarRebar? PolarRebar)
{
    /// <summary>Необязательные наборы открытых срезов поперечной арматуры.</summary>
    public IReadOnlyList<ParametricStirrupCutSet> StirrupCuts { get; init; } = [];
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
