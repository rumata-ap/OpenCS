namespace OpenCS.OpenSees.Analysis;

/// <summary>Итог полной пространственной диаграммы N-Mx-My.</summary>
public sealed class SectionSpatialInteractionResult
{
    /// <summary>Агрегированный статус результата.</summary>
    public string Status { get; init; } = "error";

    /// <summary>Точки в порядке сил и углов запроса.</summary>
    public IReadOnlyList<SectionSpatialInteractionPoint> Points { get; init; } = [];

    /// <summary>Агрегированная диагностика оркестрации.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}
