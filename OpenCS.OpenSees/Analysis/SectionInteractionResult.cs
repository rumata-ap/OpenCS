namespace OpenCS.OpenSees.Analysis;

/// <summary>Итог последовательного расчёта одноосной диаграммы N-M.</summary>
public sealed class SectionInteractionResult
{
    /// <summary>Агрегированный статус: ok, not_converged или error.</summary>
    public string Status { get; init; } = "error";

    /// <summary>Точки в том же порядке, что и силы в запросе.</summary>
    public IReadOnlyList<SectionInteractionPoint> Points { get; init; } = [];

    /// <summary>Агрегированная диагностика оркестрации.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}
