namespace OpenCS.OpenSees.Analysis;

/// <summary>Один полярный срез поверхности несущей способности при фиксированном N.</summary>
public sealed class SectionSpatialInteractionSlice
{
    /// <summary>Продольная сила среза в Н.</summary>
    public double AxialForceN { get; init; }

    /// <summary>Роль среза: boundary или support.</summary>
    public string Role { get; init; } = "support";

    /// <summary>Признак полной сходимости всех направлений среза.</summary>
    public bool IsComplete { get; init; }

    /// <summary>Статус среза.</summary>
    public string Status { get; init; } = "error";

    /// <summary>Точки полярного среза.</summary>
    public IReadOnlyList<SectionSpatialInteractionPoint> Points { get; init; } = [];

    /// <summary>Диагностика среза.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}
