namespace CScore.Fire.Entities;

/// <summary>
/// Граничное условие на одном ребре контура (внешнего или отверстия).
/// </summary>
/// <remarks>
/// <see cref="EdgeIndex"/> — индекс ребра полигона (CCW для outer, CW для hole).
/// <see cref="ContourType"/> — <c>outer</c> | <c>hole</c>.
/// <see cref="HoleIndex"/> — <c>null</c> для outer, 0, 1, 2… для отверстий.
/// <see cref="BcType"/> ∈ { <c>fire</c>, <c>ambient</c>, <c>adiabatic</c> }.
/// </remarks>
public class FireBoundaryEdgeDef
{
    /// <summary>Индекс ребра полигона.</summary>
    public int EdgeIndex { get; set; }

    /// <summary>Тип граничного условия: <c>fire</c>, <c>ambient</c> или <c>adiabatic</c>.</summary>
    public string BcType { get; set; } = "adiabatic";

    /// <summary>Коэффициент конвективного теплообмена, Вт/(м²·K).</summary>
    public double AlphaConv { get; set; }

    /// <summary>Коэффициент излучения поверхности (0…1).</summary>
    public double Emissivity { get; set; }

    /// <summary>Температура окружающей среды, °C.</summary>
    public double TAmbientCelsius { get; set; } = 20.0;

    /// <summary>Тип контура: <c>outer</c> (внешний) или <c>hole</c> (отверстие).</summary>
    public string ContourType { get; set; } = "outer";

    /// <summary>Индекс отверстия; <c>null</c> для внешнего контура.</summary>
    public int? HoleIndex { get; set; }
}
