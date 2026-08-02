using CScore.Fem;
using CScore.Planar;

namespace CSfea.CScoreBridge;

/// <summary>Результат переноса Planar boundary actions в входные данные CSfea.</summary>
public sealed class PlanarBoundaryShellMeshResult
{
    public bool IsCalculable => !Diagnostics.Any(diagnostic => diagnostic.IsError);

    public double[] NodalForceVector { get; init; } = [];

    public int[] FixedDofs { get; init; } = [];

    public double[] UFixed { get; init; } = [];

    public IReadOnlyList<FemValidationDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>Исходный mapping сохраняется для трассировки provenance до boundary contract.</summary>
    public PlanarBoundaryActionMeshMappingResult SourceMapping { get; init; } = null!;
}
