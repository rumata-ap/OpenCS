namespace CScore.Fem;

/// <summary>Загружение расчётной схемы (любого источника; у импорта — <see cref="Origin"/> и <see cref="SourceLoadNum"/>).</summary>
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

    /// <summary>Коэффициент к собственному весу всей схемы (по плотности материалов); null — без собственного веса.</summary>
    public double? SelfWeightFactor { get; set; }

    /// <summary>Происхождение: <see cref="FemLoadOrigin.Manual"/> или «import:&lt;источник&gt;».</summary>
    public string Origin { get; set; } = FemLoadOrigin.Manual;

    /// <summary>Номер загружения в программе-источнике — ключ повторного переноса нагрузок; null — задано вручную.</summary>
    public int? SourceLoadNum { get; set; }
}
