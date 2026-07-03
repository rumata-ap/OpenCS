namespace CSfea.Thermal.Materials;

/// <summary>Теплофизические свойства материала как функции температуры.</summary>
public interface IHeatMaterial
{
    /// <summary>Теплопроводность λ, Вт/(м·°C).</summary>
    double Conductivity(double T_celsius);

    /// <summary>Объёмная теплоёмкость ρc, Дж/(м³·°C).</summary>
    double VolumetricHeatCapacity(double T_celsius);
}
