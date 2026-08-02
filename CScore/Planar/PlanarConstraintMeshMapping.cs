using CScore.Fem;

namespace CScore.Planar;

/// <summary>Ребро производной сетки между двумя dense node indices.</summary>
public sealed record PlanarMeshEdge(int A, int B);

/// <summary>Производное отображение одного constraint-объекта на сущности snapshot.</summary>
public sealed class PlanarConstraintMeshMapping
{
    public string ConstraintObjectId { get; init; } = "";
    public IReadOnlyList<int> PointNodeIndices { get; init; } = [];
    public IReadOnlyList<PlanarMeshEdge> OrderedCurveEdges { get; init; } = [];
    public IReadOnlyList<int> CurveElementIndices { get; init; } = [];
    public IReadOnlyList<int> RegionNodeIndices { get; init; } = [];
    public IReadOnlyList<int> RegionElementIndices { get; init; } = [];
    public IReadOnlyList<PlanarMeshEntityProvenance> EntityProvenance { get; init; } = [];
    public IReadOnlyList<PlanarSourceReference> SourceReferences { get; init; } = [];
    public IReadOnlyList<PlanarStructuralRelation> StructuralRelations { get; init; } = [];
    public IReadOnlyList<FemValidationDiagnostic> Diagnostics { get; init; } = [];
}
