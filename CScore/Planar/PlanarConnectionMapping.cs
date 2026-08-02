using CScore.Fem;

namespace CScore.Planar;

/// <summary>Узел интерфейсной chain с каноническим параметром длины.</summary>
public sealed record PlanarConnectionMeshNode(int NodeIndex, PlanarVector3 Position, double S);

/// <summary>Однозначная пара узлов двух сторон conforming interface.</summary>
public sealed record PlanarConnectionNodePair(int SideANodeIndex, int SideBNodeIndex, double DistanceM);

/// <summary>Производное отображение одной стороны connection на mesh snapshot.</summary>
public sealed class PlanarConnectionSideMapping
{
    public int RegionId { get; init; }
    public string ConstraintObjectId { get; init; } = "";
    public PlanarConnectionOrientation Orientation { get; init; }
    public IReadOnlyList<int> OrderedNodeIndices { get; init; } = [];
    public IReadOnlyList<PlanarMeshEdge> OrderedEdges { get; init; } = [];
    public IReadOnlyList<PlanarConnectionMeshNode> Nodes { get; init; } = [];
}

/// <summary>Сопоставление одного пространственного интерфейса с двумя независимыми meshes.</summary>
public sealed class PlanarConnectionMeshMapping
{
    public int ConnectionId { get; init; }
    public string ConnectionFingerprint { get; init; } = "";
    public PlanarConnectionMeshMode MeshMode { get; init; }
    public int SideASnapshotId { get; init; }
    public string SideAFingerprint { get; init; } = "";
    public int SideBSnapshotId { get; init; }
    public string SideBFingerprint { get; init; } = "";
    public PlanarConnectionSideMapping SideA { get; init; } = new();
    public PlanarConnectionSideMapping SideB { get; init; } = new();
    public IReadOnlyList<PlanarConnectionNodePair> ExactNodePairs { get; init; } = [];
    public IReadOnlyList<FemValidationDiagnostic> Diagnostics { get; init; } = [];
    public bool IsCalculable => !Diagnostics.Any(diagnostic => diagnostic.IsError);
}

/// <summary>Результат построения или проверки connection mesh mapping.</summary>
public sealed class PlanarConnectionMappingResult
{
    public PlanarConnectionMeshMapping? Mapping { get; init; }
    public IReadOnlyList<FemValidationDiagnostic> Diagnostics { get; init; } = [];
    public bool IsCalculable => Mapping?.IsCalculable == true && !Diagnostics.Any(diagnostic => diagnostic.IsError);
}
