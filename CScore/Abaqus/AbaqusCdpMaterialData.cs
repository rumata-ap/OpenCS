namespace CScore.Abaqus;

/// <summary>Полный набор параметров и кривых материала для Abaqus CDP.</summary>
public sealed record AbaqusCdpMaterialData
{
    /// <summary>Очищенное имя материала Abaqus.</summary>
    public string MaterialName { get; init; } = "Concrete-CDP";

    /// <summary>Согласованная система единиц экспортированных величин.</summary>
    public AbaqusCdpUnitSystem UnitSystem { get; init; } =
        AbaqusCdpUnitSystem.ForProfile(AbaqusCdpUnitProfile.MpaMmN);

    /// <summary>Начальный модуль E0 в единицах напряжения Abaqus.</summary>
    public double ElasticModulus { get; init; }

    /// <summary>Коэффициент Пуассона.</summary>
    public double PoissonRatio { get; init; }

    /// <summary>Угол дилатации в градусах.</summary>
    public double DilationAngleDegrees { get; init; }

    /// <summary>Эксцентриситет.</summary>
    public double Eccentricity { get; init; }

    /// <summary>Отношение fb0/fc0.</summary>
    public double Fb0Fc0 { get; init; }

    /// <summary>Параметр Kc.</summary>
    public double Kc { get; init; }

    /// <summary>Вязкостная регуляризация.</summary>
    public double Viscosity { get; init; }

    /// <summary>
    /// Точки compression. Поле AbaqusStrain сериализуется как inelastic strain.
    /// </summary>
    public IReadOnlyList<AbaqusCdpCurvePoint> Compression { get; init; } = [];

    /// <summary>
    /// Точки tension. Поле AbaqusStrain сериализуется как cracking strain.
    /// </summary>
    public IReadOnlyList<AbaqusCdpCurvePoint> Tension { get; init; } = [];
}
