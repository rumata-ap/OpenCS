using CSfea.Thermal.Materials;

namespace CScore.Fire;

/// <summary>
/// Теплофизические свойства бетона по СП 468.1325800 (приложение А).
/// </summary>
/// <remarks>
/// Реализует λ(T), c(T), ρ(T) и ρ(T)·c(T) с линейной интерполяцией по табличным точкам.
/// </remarks>
public sealed class Sp468ConcreteHeatMaterial : IHeatMaterial
{
    private const double LVapor = 2_257_000.0;
    private readonly string _aggregateType;
    private readonly double _moistureFraction;

    private static readonly double[] LambdaSilicateT = [20.0, 100.0, 200.0, 400.0, 600.0, 800.0, 1000.0, 1200.0];
    private static readonly double[] LambdaSilicateV = [1.60, 1.50, 1.35, 1.10, 0.90, 0.75, 0.62, 0.55];

    private static readonly double[] LambdaCarbonateT = [20.0, 100.0, 200.0, 400.0, 600.0, 800.0, 1000.0, 1200.0];
    private static readonly double[] LambdaCarbonateV = [1.80, 1.65, 1.50, 1.20, 0.95, 0.80, 0.65, 0.55];

    private static readonly double[] LambdaLightweightT = [20.0, 200.0, 400.0, 600.0, 800.0, 1000.0, 1200.0];
    private static readonly double[] LambdaLightweightV = [0.80, 0.75, 0.65, 0.55, 0.48, 0.42, 0.38];

    private static readonly double[] CpBaseT = [20.0, 100.0, 200.0, 400.0, 600.0, 800.0, 1000.0, 1200.0];
    private static readonly double[] CpBaseV = [900.0, 900.0, 1000.0, 1100.0, 1100.0, 1100.0, 1100.0, 1100.0];

    /// <summary>
    /// Создаёт модель бетона СП 468 с заданным типом заполнителя и влажностью.
    /// </summary>
    /// <param name="aggregateType">Тип заполнителя: silicate, carbonate, lightweight.</param>
    /// <param name="moistureFraction">Массовая доля влаги бетона (например, 0.025).</param>
    public Sp468ConcreteHeatMaterial(string aggregateType = "silicate", double moistureFraction = 0.025)
    {
        _aggregateType = aggregateType;
        _moistureFraction = moistureFraction;
    }

    /// <summary>Теплопроводность λ(T), Вт/(м·°C).</summary>
    public double Conductivity(double T_celsius)
    {
        if (_aggregateType == "carbonate")
            return Interp(T_celsius, LambdaCarbonateT, LambdaCarbonateV);

        if (_aggregateType == "lightweight")
            return Interp(T_celsius, LambdaLightweightT, LambdaLightweightV);

        return Interp(T_celsius, LambdaSilicateT, LambdaSilicateV);
    }

    /// <summary>
    /// Удельная теплоёмкость c(T), Дж/(кг·°C), с пиком в области 100°C из-за влаги.
    /// </summary>
    public double SpecificHeat(double T_celsius)
    {
        double cp = Interp(T_celsius, CpBaseT, CpBaseV);
        if (80.0 <= T_celsius && T_celsius <= 120.0)
        {
            double peak = LVapor * _moistureFraction / 20.0;
            cp += peak * (1.0 - Math.Abs(T_celsius - 100.0) / 20.0);
        }

        return cp;
    }

    /// <summary>Плотность ρ(T), кг/м³.</summary>
    public double Density(double T_celsius)
    {
        double rho20 = _aggregateType == "lightweight" ? 1800.0 : 2400.0;
        if (T_celsius <= 20.0)
            return rho20;
        if (T_celsius >= 1200.0)
            return rho20 * 0.97;
        return rho20 * (1.0 - 0.03 * (T_celsius - 20.0) / (1200.0 - 20.0));
    }

    /// <summary>Объёмная теплоёмкость ρ(T)·c(T), Дж/(м³·°C).</summary>
    public double VolumetricHeatCapacity(double T_celsius)
        => Density(T_celsius) * SpecificHeat(T_celsius);

    /// <summary>
    /// Линейная интерполяция по возрастающим узлам с зажимом за пределами диапазона.
    /// </summary>
    internal static double Interp(double T, double[] xs, double[] ys)
    {
        if (T <= xs[0])
            return ys[0];
        if (T >= xs[^1])
            return ys[^1];

        for (int i = 1; i < xs.Length; i++)
        {
            if (T <= xs[i])
            {
                double x0 = xs[i - 1];
                double x1 = xs[i];
                double y0 = ys[i - 1];
                double y1 = ys[i];
                return y0 + (y1 - y0) * (T - x0) / (x1 - x0);
            }
        }

        return ys[^1];
    }
}
