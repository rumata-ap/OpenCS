using CScore.Fem;

namespace CScore.Planar;

/// <summary>Входные данные построения сетки одного плоского региона.</summary>
public sealed record PlanarMeshingRequest(
    PlanarRegion Region,
    PlanarMeshSettings Settings,
    IReadOnlyList<PlanarConstraintObject>? ConstraintObjects = null,
    string? ConstraintSourceFingerprint = null,
    IReadOnlyList<FemValidationDiagnostic>? PreflightDiagnostics = null)
{
    /// <summary>Явные request-local constraints или сохранённые ручные constraints региона.</summary>
    public IReadOnlyList<PlanarConstraintObject> EffectiveConstraintObjects => ConstraintObjects ?? Region.ConstraintObjects;

    /// <summary>Диагностика orchestration до запуска внешнего mesher-а.</summary>
    public IReadOnlyList<FemValidationDiagnostic> EffectivePreflightDiagnostics => PreflightDiagnostics ?? [];
}

/// <summary>Независимый от конкретного генератора контракт построения плоской сетки.</summary>
public interface IPlanarMesher
{
    Task<PlanarMeshSnapshot> BuildAsync(PlanarMeshingRequest request, CancellationToken cancellationToken = default);
}
