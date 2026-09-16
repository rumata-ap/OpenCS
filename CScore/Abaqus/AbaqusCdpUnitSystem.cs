namespace CScore.Abaqus;

/// <summary>Профиль согласованных единиц для Abaqus CDP.</summary>
public enum AbaqusCdpUnitProfile
{
    /// <summary>Мегапаскали, миллиметры и ньютоны.</summary>
    MpaMmN,

    /// <summary>Килопаскали, метры и килоньютоны.</summary>
    KpaMKN,

    /// <summary>Паскали, метры и ньютоны.</summary>
    PaMN,

    /// <summary>Пользовательская согласованная система.</summary>
    Custom
}

/// <summary>Описывает масштаб напряжений и подписи единиц, передаваемые в Abaqus.</summary>
public sealed record AbaqusCdpUnitSystem(
    AbaqusCdpUnitProfile Profile,
    string StressUnit,
    string LengthUnit,
    string ForceUnit,
    double StressScaleFromOpenCsKpa)
{
    /// <summary>Производная единица энергии разрушения.</summary>
    public string EnergyUnit => $"{ForceUnit}/{LengthUnit}";

    /// <summary>Возвращает одну из предустановленных систем единиц.</summary>
    public static AbaqusCdpUnitSystem ForProfile(AbaqusCdpUnitProfile profile) => profile switch
    {
        AbaqusCdpUnitProfile.MpaMmN => new(profile, "MPa", "mm", "N", 0.001),
        AbaqusCdpUnitProfile.KpaMKN => new(profile, "kPa", "m", "kN", 1.0),
        AbaqusCdpUnitProfile.PaMN   => new(profile, "Pa", "m", "N", 1000.0),
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile,
            "Custom unit system requires explicit unit labels and stress scale.")
    };

    /// <summary>Создаёт пользовательский профиль с явными единицами и масштабом.</summary>
    public static AbaqusCdpUnitSystem Custom(
        string stressUnit,
        string lengthUnit,
        string forceUnit,
        double stressScaleFromOpenCsKpa) => new(
        AbaqusCdpUnitProfile.Custom,
        stressUnit,
        lengthUnit,
        forceUnit,
        stressScaleFromOpenCsKpa);
}
