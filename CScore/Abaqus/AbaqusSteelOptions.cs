using CScore;

namespace CScore.Abaqus;

/// <summary>Настройки экспорта стали и арматуры в Abaqus (*Elastic + *Plastic).</summary>
public sealed record AbaqusSteelOptions
{
    /// <summary>Вид расчёта, из которого берутся характеристики стали.</summary>
    public CalcType CalcType { get; init; } = CalcType.C;

    /// <summary>Согласованная система единиц Abaqus.</summary>
    public AbaqusCdpUnitSystem UnitSystem { get; init; } =
        AbaqusCdpUnitSystem.ForProfile(AbaqusCdpUnitProfile.MpaMmN);

    /// <summary>Коэффициент Пуассона.</summary>
    public double PoissonRatio { get; init; } = 0.3;

    /// <summary>Имя материала в Abaqus до очистки от небезопасных символов.</summary>
    public string MaterialName { get; init; } = "Steel-Plastic";

    /// <summary>Площадка текучести в диаграмме СП 16 (только для конструкционной стали).</summary>
    public bool HasYieldPlateau { get; init; } = true;

    /// <summary>Создаёт настройки по умолчанию в системе MPa-mm-N.</summary>
    public static AbaqusSteelOptions Default() => ForProfile(AbaqusCdpUnitProfile.MpaMmN);

    /// <summary>Создаёт настройки для предустановленного профиля единиц.</summary>
    public static AbaqusSteelOptions ForProfile(AbaqusCdpUnitProfile profile) =>
        new() { UnitSystem = AbaqusCdpUnitSystem.ForProfile(profile) };
}
