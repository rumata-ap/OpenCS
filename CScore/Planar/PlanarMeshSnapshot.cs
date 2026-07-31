using CScore.Fem;

namespace CScore.Planar;

/// <summary>Тип линейного оболочечного элемента снимка сетки.</summary>
public enum PlanarMeshElementKind
{
    Triangle3,
    Quadrangle4
}

/// <summary>Узел снимка сетки в локальных и глобальных координатах.</summary>
public sealed record PlanarMeshNode(int Index, double U, double V, double X, double Y, double Z);

/// <summary>Оболочечный элемент снимка сетки по плотным индексам узлов.</summary>
public sealed record PlanarMeshElement(int Index, PlanarMeshElementKind Kind, IReadOnlyList<int> NodeIndices);

/// <summary>Ключ исходного участка границы региона.</summary>
public sealed record PlanarBoundaryKey(BoundaryLoop Loop, int HoleIndex, int StartVertex, int EndVertex);

/// <summary>Отображение участка границы на цепочку рёбер сетки.</summary>
public sealed class PlanarMeshBoundaryMapping
{
    public PlanarBoundaryKey Key { get; init; } = new(BoundaryLoop.Outer, 0, 0, 1);
    public IReadOnlyList<int> NodeIndices { get; init; } = [];
}

/// <summary>Производная сетка одного PlanarRegion вместе с результатом геометрической проверки.</summary>
public sealed class PlanarMeshSnapshot
{
    public int Id { get; set; }
    public int RegionId { get; init; }
    public string InputFingerprint { get; init; } = "";
    public bool IsCalculable { get; init; }
    public PlanarMeshSettings? Settings { get; init; }
    public PlanarMeshProvenance? Provenance { get; init; }
    public IReadOnlyList<FemValidationDiagnostic> Diagnostics { get; init; } = [];
    public IReadOnlyList<PlanarMeshNode> Nodes { get; init; } = [];
    public IReadOnlyList<PlanarMeshElement> Elements { get; init; } = [];
    public IReadOnlyList<PlanarMeshBoundaryMapping> BoundaryMappings { get; init; } = [];
}
