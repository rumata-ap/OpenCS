using CScore;

namespace CScore.Fire;

/// <summary>
/// Бетонное волокно для огневой проверки несущей способности.
/// </summary>
public sealed class FireConcreteElement
{
    /// <summary>Площадь волокна, м².</summary>
    public double Area;

    /// <summary>Координата центра тяжести по X, м.</summary>
    public double Cx;

    /// <summary>Координата центра тяжести по Y, м.</summary>
    public double Cy;

    /// <summary>Материал бетона.</summary>
    public Material Material = null!;

    /// <summary>Температура волокна, °C.</summary>
    public double Temperature;

    /// <summary>Температурный коэффициент бетона γ_bt.</summary>
    public double GammaBt = 1.0;
}
