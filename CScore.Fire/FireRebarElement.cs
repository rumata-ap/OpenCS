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

    /// <summary>
    /// Температурный коэффициент условий работы арматуры γ_st по таблице 5.6.
    /// Единый для растяжения и сжатия: формулы (5.5) и (5.6) СП 468 применяют
    /// один и тот же коэффициент к R_s и к R_sc.
    /// </summary>
    public double GammaSt = 1.0;

    /// <summary>Разрешённая группа класса арматуры по таблице 5.6.</summary>
    public FireRebarClass ClassGroup = FireRebarClass.A240A500;

    /// <summary>Источник группы класса: explicit, tag, class или fallback.</summary>
    public string ClassSource = "fallback";
}
