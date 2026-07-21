namespace OpenCS.OpenSees.Structural;

/// <summary>Результат одного шага нагрузки нелинейного расчёта. Для несошедшегося шага
/// (Converged=false) списки перемещений/реакций/усилий пусты — состояние не было закоммичено.</summary>
public sealed record FemNonlinearStepResult(
    int StepIndex,
    double LoadFactor,
    bool Converged,
    IReadOnlyList<FemNodeDisplacement> Displacements,
    IReadOnlyList<FemNodeReaction> Reactions,
    IReadOnlyList<FemElementEndForces> ElementForces);

/// <summary>Типизированный результат нелинейного расчёта FEM-схемы — полная история шагов.</summary>
public sealed class FemNonlinearResult
{
    public string Status { get; init; } = "created";
    public IReadOnlyList<FemNonlinearStepResult> Steps { get; init; } = [];
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
    public string? ArtifactDirectory { get; init; }
}
