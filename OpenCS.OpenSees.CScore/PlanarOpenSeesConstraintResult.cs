using CScore.Fem;
using CScore.Planar;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore;

/// <summary>Одна emitted OpenSees relation с provenance до исходной topology.</summary>
public sealed record PlanarOpenSeesConstraintEmission(
    string ConstraintObjectId,
    PlanarStructuralKind StructuralKind,
    PlanarOpenSeesConstraintPolicy Policy,
    int SourceMemberId,
    string SourceMemberTag,
    IReadOnlyList<int> SourceElementIds,
    IReadOnlyList<string> SourceElementTags,
    int MasterNodeTag,
    int SlaveNodeTag,
    IReadOnlyList<int> Dofs,
    IReadOnlyList<int> HostSnapshotNodeIndices,
    IReadOnlyList<int> SourceNodeIds);

/// <summary>Результат атомарного применения planar constraints к OpenSees-модели.</summary>
public sealed record PlanarOpenSeesConstraintResult(
    ShellOpenSeesModel? Model,
    IReadOnlyList<PlanarOpenSeesConstraintEmission> Emissions,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics)
{
    /// <summary>Признак, что модель можно передать следующему этапу расчёта.</summary>
    public bool IsCalculable => Model is not null &&
        !Diagnostics.Any(diagnostic => diagnostic.IsError);
}
