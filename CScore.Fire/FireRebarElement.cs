using CScore;

namespace CScore.Fire;

/// <summary>
/// Арматурное волокно для огневой проверки несущей способности.
/// </summary>
public sealed class FireRebarElement
{
    /// <summary>Координата X точки арматуры, м.</summary>
    public double X;

    /// <summary>Координата Y точки арматуры, м.</summary>
    public double Y;

    /// <summary>Диаметр стержня, м.</summary>
    public double Diameter;

    /// <summary>Площадь стержня, м².</summary>
    public double Area;

    /// <summary>Материал арматуры.</summary>
    public Material Material = null!;

    /// <summary>Идентификатор стержня в тепловой модели.</summary>
    public int RebarId;

    /// <summary>Температура стержня, °C.</summary>
    public double Temperature;

    /// <summary>Температурный коэффициент арматуры при сжатии γ_st,c.</summary>
    public double GammaStC = 1.0;

    /// <summary>Температурный коэффициент арматуры при растяжении γ_st,t.</summary>
    public double GammaStT = 1.0;
}
