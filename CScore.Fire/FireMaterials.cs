namespace CScore.Fire;

/// <summary>Коэффициенты температурной редукции γ для R-проверок по СП 468.</summary>
public static class FireMaterials
{
    private static readonly double[] GammaBtT = [20.0, 100.0, 200.0, 300.0, 400.0, 500.0, 600.0, 700.0, 800.0];
    private static readonly double[] GammaBtSilicate = [1.0, 1.0, 0.95, 0.85, 0.75, 0.60, 0.45, 0.30, 0.15];
    private static readonly double[] GammaBtCarbonate = [1.0, 1.0, 0.97, 0.90, 0.80, 0.65, 0.50, 0.35, 0.20];

    private static readonly double[] GammaStT = [20.0, 100.0, 200.0, 300.0, 400.0, 500.0, 600.0, 700.0, 800.0];
    private static readonly double[] GammaStComp = [1.0, 1.0, 0.95, 0.90, 0.85, 0.70, 0.50, 0.30, 0.15];
    private static readonly double[] GammaStTens = [1.0, 1.0, 1.0, 0.95, 0.85, 0.65, 0.45, 0.25, 0.10];

    /// <summary>
    /// γ_bt(T): коэффициент условий работы бетона по температуре.
    /// </summary>
    /// <param name="concreteId">Идентификатор/класс бетона (зарезервирован на будущее).</param>
    /// <param name="aggregateType">Тип заполнителя: silicate или carbonate.</param>
    /// <param name="T">Температура, °C.</param>
    public static double GammaBt(string concreteId, string aggregateType, double T)
    {
        _ = concreteId;
        double[] table = aggregateType == "carbonate" ? GammaBtCarbonate : GammaBtSilicate;
        return Sp468ConcreteHeatMaterial.Interp(T, GammaBtT, table);
    }

    /// <summary>
    /// γ_st(T): коэффициент условий работы арматуры по температуре.
    /// </summary>
    /// <param name="rebarClass">Класс арматуры (зарезервирован на будущее).</param>
    /// <param name="T">Температура, °C.</param>
    /// <param name="stressState">Состояние: compression или tension.</param>
    public static double GammaSt(string rebarClass, double T, string stressState = "compression")
    {
        _ = rebarClass;
        double[] table = stressState == "compression" ? GammaStComp : GammaStTens;
        return Sp468ConcreteHeatMaterial.Interp(T, GammaStT, table);
    }
}
