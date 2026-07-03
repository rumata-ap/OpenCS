namespace CScore.Fem;

/// <summary>Загружение расчётной схемы. Используется только для internal/opensees источников.</summary>
public class FemLoadCase
{
    public int     Id       { get; set; }
    public int     SchemaId { get; set; }
    public string  Tag      { get; set; } = "";
    /// <summary>Тип: "permanent" | "live" | "wind" | "snow" | "seismic"</summary>
    public string? LoadType { get; set; }
}
