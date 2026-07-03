namespace CSfea.Thermal.Bc;

/// <summary>
/// Физика граничного потока Робина для теплового МКЭ.
/// Граничное условие на ребре внешнего контура:
/// −λ ∂T/∂n = q_conv + q_rad,
/// q_conv = α_conv · (T_s − T_∞),
/// q_rad = εσ · ((T_s + 273.15)⁴ − (T_∞ + 273.15)⁴).
/// Здесь q считается положительным наружу. В контексте FEM используется
/// противоположный знак (поток внутрь = нагрев), поэтому
/// <see cref="ComputeBoundaryFlux"/> возвращает
/// q_in = α_conv·(T_∞ − T_s) + εσ·((T_∞,K)⁴ − (T_s,K)⁴).
/// </summary>
public static class RobinHeatFlux
{
    /// <summary>Постоянная Стефана-Больцмана, Вт/(м²·К⁴).</summary>
    public const double SigmaStefanBoltzmann = 5.670374419e-8;

    /// <summary>
    /// Полный поток тепла через ребро (внутрь сечения), Вт/м².
    /// T_surface, T_ambient — в °C. Возвращает q_in (положительный при T_∞ &gt; T_s).
    /// </summary>
    public static double ComputeBoundaryFlux(
        double T_surface,
        double T_ambient,
        double alpha_conv,
        double emissivity)
    {
        double q_conv = alpha_conv * (T_ambient - T_surface);
        double Ts_K = T_surface + 273.15;
        double Tinf_K = T_ambient + 273.15;
        double q_rad = emissivity * SigmaStefanBoltzmann * (Tinf_K * Tinf_K * Tinf_K * Tinf_K - Ts_K * Ts_K * Ts_K * Ts_K);
        return q_conv + q_rad;
    }

    /// <summary>
    /// Линеаризованный коэффициент h_rad для Пикар-итерации, Вт/(м²·°C).
    /// Использует тождество T_∞,K⁴ − T_s,K⁴ = (T_∞,K − T_s,K)·(T_∞,K + T_s,K)·(T_∞,K² + T_s,K²),
    /// откуда q_rad = h_rad · (T_∞ − T_s), где
    /// h_rad = εσ · (T_∞,K + T_s,K)·(T_∞,K² + T_s,K²).
    /// </summary>
    public static double ComputeHRadiationLinearized(
        double T_surface,
        double T_ambient,
        double emissivity)
    {
        double Ts_K = T_surface + 273.15;
        double Tinf_K = T_ambient + 273.15;
        return emissivity * SigmaStefanBoltzmann * (Ts_K + Tinf_K) * (Ts_K * Ts_K + Tinf_K * Tinf_K);
    }

    /// <summary>
    /// Эффективный линеаризованный коэффициент теплообмена α_lin = α_conv + h_rad.
    /// Используется в сборке K_edge и F_edge для Робина.
    /// </summary>
    public static double ComputeAlphaLin(
        double T_surface,
        double T_ambient,
        double alpha_conv,
        double emissivity)
    {
        double h_rad = ComputeHRadiationLinearized(T_surface, T_ambient, emissivity);
        return alpha_conv + h_rad;
    }
}
