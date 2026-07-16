namespace CScore.Fem;

/// <summary>Загружение расчётной схемы. Используется только для internal/opensees источников.</summary>
public class FemLoadCase
{
    public int     Id       { get; set; }
    public int     SchemaId { get; set; }
    public string  Tag      { get; set; } = "";
    /// <summary>Тип: "permanent" | "live" | "wind" | "snow" | "seismic"</summary>
    public string? LoadType { get; set; }

    /// <summary>Нормативный тип по СП 20.13330.</summary>
    public string Sp20Type { get; set; } = "short_term";
    public string? Sp20Group { get; set; }
    public double? GammaFUnfav { get; set; }
    public double? GammaFFav { get; set; }
    public double? Psi1 { get; set; }
    public double? Psi2 { get; set; }
}
