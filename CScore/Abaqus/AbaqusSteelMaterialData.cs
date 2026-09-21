namespace CScore.Abaqus;

/// <summary>Точка таблицы *Plastic: истинное напряжение и истинная пластическая деформация.</summary>
/// <param name="Stress">Истинное напряжение σ(1+ε) в единицах Abaqus.</param>
/// <param name="PlasticStrain">Истинная пластическая деформация ln(1+ε) − σ_true/E.</param>
/// <param name="NominalStrain">Исходная (инженерная) деформация диаграммы OpenCS.</param>
/// <param name="NominalStress">Исходное (инженерное) напряжение в единицах Abaqus.</param>
public sealed record AbaqusSteelPlasticPoint(
    double Stress,
    double PlasticStrain,
    double NominalStrain,
    double NominalStress);

/// <summary>Набор данных стали для Abaqus: упругость, таблица *Plastic и предупреждения.</summary>
public sealed record AbaqusSteelMaterialData
{
    /// <summary>Очищенное имя материала Abaqus.</summary>
    public string MaterialName { get; init; } = "Steel-Plastic";

    /// <summary>Согласованная система единиц экспортированных величин.</summary>
    public AbaqusCdpUnitSystem UnitSystem { get; init; } =
        AbaqusCdpUnitSystem.ForProfile(AbaqusCdpUnitProfile.MpaMmN);

    /// <summary>Модуль упругости E в единицах напряжения Abaqus.</summary>
    public double ElasticModulus { get; init; }

    /// <summary>Коэффициент Пуассона.</summary>
    public double PoissonRatio { get; init; }

    /// <summary>Точки *Plastic; первая — начало пластичности с нулевой пластической деформацией.</summary>
    public IReadOnlyList<AbaqusSteelPlasticPoint> Plastic { get; init; } = [];

    /// <summary>Предупреждения об упрощениях при переносе диаграммы в Abaqus.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
