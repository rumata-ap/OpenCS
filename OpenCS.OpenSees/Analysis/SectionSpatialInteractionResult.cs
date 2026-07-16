namespace OpenCS.OpenSees.Analysis;

/// <summary>Итог полной пространственной диаграммы N-Mx-My.</summary>
public sealed class SectionSpatialInteractionResult
{
    /// <summary>Агрегированный статус результата.</summary>
    public string Status { get; init; } = "error";

    /// <summary>Итог проверки исходных точек: ok, not_ok, indeterminate или not_available.</summary>
    public string VerificationStatus { get; init; } = "not_available";

    /// <summary>Тип построенной фигуры: surface или plane.</summary>
    public string GeometryKind { get; init; } = "surface";

    /// <summary>Минимальная рабочая продольная сила после поиска сходимости границы.</summary>
    public double? EffectiveMinimumAxialForceN { get; init; }

    /// <summary>Максимальная рабочая продольная сила после поиска сходимости границы.</summary>
    public double? EffectiveMaximumAxialForceN { get; init; }

    /// <summary>Построенные полярные срезы.</summary>
    public IReadOnlyList<SectionSpatialInteractionSlice> Slices { get; init; } = [];

    /// <summary>Проверки исходных строк ForceSet.</summary>
    public IReadOnlyList<SectionSpatialInteractionDemandCheck> DemandChecks { get; init; } = [];

    /// <summary>Точки в порядке сил и углов запроса.</summary>
    public IReadOnlyList<SectionSpatialInteractionPoint> Points { get; init; } = [];

    /// <summary>Агрегированная диагностика оркестрации.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}
