using CScore.Fem;

namespace CScore.Planar;

/// <summary>Идентичность одного состояния parent FEM-модели.</summary>
public sealed record PlanarBoundarySourceScenario(
    string SourceModelId,
    string? ResultId,
    int? StageIndex,
    int? StepIndex,
    bool ResultAvailable,
    bool Converged);

/// <summary>Запрос provider-а на получение действия одного cut interface.</summary>
public sealed record PlanarBoundaryActionRequest
{
    public PlanarCutInterface Interface { get; init; } = new();
    public PlanarBoundaryActionSourceMode SourceMode { get; init; }
    public PlanarBoundaryActionKind? RequestedKind { get; init; }
    public PlanarDofMask RequiredDofs { get; init; }
    public Frame3D TargetFrame { get; init; } = Frame3D.Identity;
    public PlanarBoundarySourceScenario? Scenario { get; init; }
}

/// <summary>Результат одного provider-а до объединения source modes.</summary>
public sealed class PlanarBoundaryActionProviderResult
{
    public PlanarBoundaryActionSourceMode SourceMode { get; init; }
    public IReadOnlyList<PlanarBoundaryForceAction> ForceActions { get; init; } = [];
    public IReadOnlyList<PlanarBoundaryKinematicAction> KinematicActions { get; init; } = [];
    public IReadOnlyList<PlanarBoundarySourceReference> SourceReferences { get; init; } = [];
    public IReadOnlyList<FemValidationDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>Маска DOF, покрытая actions provider-а.</summary>
    public PlanarDofMask CoveredDofs => ForceActions
        .Select(action => action.DofMask)
        .Concat(KinematicActions.Select(action => action.DofMask))
        .Aggregate(PlanarDofMask.None, (mask, item) => mask | item);

    /// <summary>Признак пригодности результата для дальнейшего mapping.</summary>
    public bool IsCalculable => !Diagnostics.Any(diagnostic => diagnostic.IsError);
}
