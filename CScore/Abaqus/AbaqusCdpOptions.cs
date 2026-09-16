using CScore;

namespace CScore.Abaqus;

/// <summary>Настройки построения и экспорта бетонного материала Abaqus CDP.</summary>
public sealed record AbaqusCdpOptions
{
    /// <summary>Вид расчёта, из которого берутся характеристики бетона.</summary>
    public CalcType CalcType { get; init; } = CalcType.C;

    /// <summary>Согласованная система единиц Abaqus.</summary>
    public AbaqusCdpUnitSystem UnitSystem { get; init; } =
        AbaqusCdpUnitSystem.ForProfile(AbaqusCdpUnitProfile.MpaMmN);

    /// <summary>Угол дилатации в градусах.</summary>
    public double DilationAngleDegrees { get; init; } = 35.0;

    /// <summary>Эксцентриситет поверхности текучести.</summary>
    public double Eccentricity { get; init; } = 0.1;

    /// <summary>Отношение начальной двухосной и одноосной прочности.</summary>
    public double Fb0Fc0 { get; init; } = 1.16;

    /// <summary>Отношение мер напряжений на растягивающем и сжимающем меридианах.</summary>
    public double Kc { get; init; } = 0.667;

    /// <summary>Параметр вязкостной регуляризации Abaqus.</summary>
    public double Viscosity { get; init; } = 0.0001;

    /// <summary>Коэффициент Пуассона.</summary>
    public double PoissonRatio { get; init; } = 0.2;

    /// <summary>Энергия разрушения в единицах выбранного профиля.</summary>
    public double FractureEnergy { get; init; } = 0.0726;

    /// <summary>Размер конечного элемента в единицах длины выбранного профиля.</summary>
    public double ElementLength { get; init; } = 10.0;

    /// <summary>Уровень напряжения первой точки compression относительно fc.</summary>
    public double InitialCompressionStressRatio { get; init; } = 0.4;

    /// <summary>Минимальный уровень нисходящей ветви DEKB.</summary>
    public double CompressionEtaMin { get; init; } = 0.05;

    /// <summary>Имя материала в Abaqus до очистки от небезопасных символов.</summary>
    public string MaterialName { get; init; } = "Concrete-CDP";

    /// <summary>Создаёт настройки по умолчанию в системе MPa-mm-N.</summary>
    public static AbaqusCdpOptions Default() => ForProfile(AbaqusCdpUnitProfile.MpaMmN);

    /// <summary>
    /// Создаёт настройки для профиля, переводя канонические Gf в N/mm и Le в mm
    /// в единицы выбранного профиля.
    /// </summary>
    /// <param name="profile">Предустановленный профиль единиц.</param>
    /// <param name="fractureEnergyNPerMm">Энергия разрушения в N/mm.</param>
    /// <param name="elementLengthMm">Размер элемента в mm.</param>
    public static AbaqusCdpOptions ForProfile(
        AbaqusCdpUnitProfile profile,
        double fractureEnergyNPerMm = 0.0726,
        double elementLengthMm = 10.0)
    {
        var units = AbaqusCdpUnitSystem.ForProfile(profile);
        bool lengthIsMillimeters = profile == AbaqusCdpUnitProfile.MpaMmN;
        double energy = profile == AbaqusCdpUnitProfile.PaMN
            ? fractureEnergyNPerMm * 1000.0
            : fractureEnergyNPerMm;
        double length = lengthIsMillimeters ? elementLengthMm : elementLengthMm / 1000.0;

        return new AbaqusCdpOptions
        {
            UnitSystem = units,
            FractureEnergy = energy,
            ElementLength = length
        };
    }
}
