namespace CSfea.Thermal.Bc;

/// <summary>Тип граничного условия на ребре внешнего контура.</summary>
public enum HeatBoundaryBcType
{
    /// <summary>Температура окружающей среды по кривой пожара (fire_curve).</summary>
    Fire,

    /// <summary>Постоянная температура окружающей среды <see cref="HeatBoundaryEdge.TAmbientCelsius"/>.</summary>
    Ambient,

    /// <summary>Адиабатическое ребро — нулевой поток, вклад в K и F не добавляется.</summary>
    Adiabatic,
}
