using CScore.Fem;
using CScore.Planar;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore;

/// <summary>Результат применения boundary action mapping к OpenSees shell-модели.</summary>
public sealed class PlanarBoundaryOpenSeesResult
{
    public ShellOpenSeesModel? Model { get; init; }
    public IReadOnlyList<FemValidationDiagnostic> Diagnostics { get; init; } = [];
    public PlanarBoundaryActionMeshMappingResult? SourceMapping { get; init; }
    public IReadOnlyDictionary<int, int> SnapshotNodeToOpenSeesTag { get; init; } =
        new Dictionary<int, int>();
    public bool IsCalculable => Model is not null && !Diagnostics.Any(diagnostic => diagnostic.IsError);
}
