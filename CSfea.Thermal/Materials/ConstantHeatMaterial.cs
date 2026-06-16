namespace CSfea.Thermal.Materials;

/// <summary>Материал с постоянными λ и ρc, не зависящими от температуры.</summary>
public sealed class ConstantHeatMaterial(double lambda, double rhocp) : IHeatMaterial
{
    /// <inheritdoc/>
    public double Conductivity(double T_celsius) => lambda;

    /// <inheritdoc/>
    public double VolumetricHeatCapacity(double T_celsius) => rhocp;
}
