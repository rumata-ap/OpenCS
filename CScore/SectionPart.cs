namespace CScore;

/// <summary>
/// Часть стадийного сечения: набор областей, включаемых в работу одновременно.
/// Либо ссылается на обычное сечение (<see cref="SourceSectionId"/>, области берутся из него при
/// загрузке), либо держит собственные области пула.
/// </summary>
public sealed class SectionPart
{
    /// <summary>Порядковый номер части (0 — включена с начала истории).</summary>
    public int Index { get; set; }

    /// <summary>Наименование части.</summary>
    public string Tag { get; set; } = "";

    /// <summary>Id обычного сечения-источника; null — собственные области.</summary>
    public int? SourceSectionId { get; set; }

    /// <summary>Области части.</summary>
    public List<MaterialArea> Areas { get; set; } = [];

    /// <summary>Копия для параллельного расчёта (области клонируются).</summary>
    public SectionPart CloneForCalc() => new()
    {
        Index = Index,
        Tag = Tag,
        SourceSectionId = SourceSectionId,
        Areas = Areas.Select(a => a.CloneForCalc()).ToList(),
    };
}
