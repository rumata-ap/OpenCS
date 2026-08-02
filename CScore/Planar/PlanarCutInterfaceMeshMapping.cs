using CScore.Fem;

namespace CScore.Planar;

/// <summary>Узел ordered chain cut interface с нормализованной координатой длины.</summary>
public sealed record PlanarCutInterfaceMeshNode(int NodeIndex, PlanarVector3 Position, double S);

/// <summary>Отображение cut interface на конкретный PlanarMeshSnapshot.</summary>
public sealed class PlanarCutInterfaceMeshMapping
{
    public string InterfaceId { get; init; } = "";
    public int SnapshotId { get; init; }
    public string SnapshotFingerprint { get; init; } = "";
    public IReadOnlyList<PlanarCutInterfaceMeshNode> OrderedNodes { get; init; } = [];
    public IReadOnlyList<PlanarMeshEdge> OrderedEdges { get; init; } = [];
    public PlanarConnectionOrientation Orientation { get; init; } = PlanarConnectionOrientation.Forward;
    public PlanarVector3 Normal { get; init; }
    public IReadOnlyList<FemValidationDiagnostic> Diagnostics { get; init; } = [];
    public bool IsCalculable => !Diagnostics.Any(diagnostic => diagnostic.IsError);
}

/// <summary>Результат построения mapping cut interface.</summary>
public sealed class PlanarCutInterfaceMeshMappingResult
{
    public PlanarCutInterfaceMeshMapping? Mapping { get; init; }
    public IReadOnlyList<FemValidationDiagnostic> Diagnostics { get; init; } = [];
    public bool IsCalculable => Mapping?.IsCalculable == true && !Diagnostics.Any(diagnostic => diagnostic.IsError);
}

/// <summary>Строит ordered cut chain из существующих boundary/constraint mappings.</summary>
public static class PlanarCutInterfaceMeshMapper
{
    /// <summary>Отображает cut interface без nearest-node эвристики.</summary>
    public static PlanarCutInterfaceMeshMappingResult Map(
        PlanarCutInterface cut,
        PlanarMeshSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(cut);
        ArgumentNullException.ThrowIfNull(snapshot);

        var diagnostics = cut.Validate().ToList();
        diagnostics.AddRange(PlanarMeshSnapshotValidator.Validate(snapshot));
        if (diagnostics.Any(diagnostic => diagnostic.IsError))
            return new() { Diagnostics = diagnostics };

        IReadOnlyList<int>? nodeIndices = null;
        IReadOnlyList<PlanarMeshEdge>? explicitEdges = null;
        string? meshConstraintId = cut.MeshConstraintId;
        if (string.IsNullOrWhiteSpace(meshConstraintId) && cut.BoundaryKey is null)
            meshConstraintId = $"cut:{cut.Id}";
        if (!string.IsNullOrWhiteSpace(meshConstraintId))
        {
            var constraint = snapshot.ConstraintMappings.FirstOrDefault(item =>
                string.Equals(item.ConstraintObjectId, meshConstraintId, StringComparison.Ordinal));
            if (constraint is null)
                diagnostics.Add(new("planar_boundary_mapping_missing", $"Не найден constraint mapping '{meshConstraintId}'."));
            else
                explicitEdges = constraint.OrderedCurveEdges;
        }

        if (cut.BoundaryKey is { } boundaryKey)
        {
            var boundary = snapshot.BoundaryMappings.FirstOrDefault(item => item.Key == boundaryKey);
            if (boundary is null)
                diagnostics.Add(new("planar_boundary_mapping_missing", $"Не найден boundary mapping '{boundaryKey}'."));
            else
                nodeIndices = boundary.NodeIndices;
        }

        if (nodeIndices is null && explicitEdges is { Count: > 0 })
            nodeIndices = OrderedNodesFromEdges(explicitEdges, diagnostics);
        if (nodeIndices is null)
            diagnostics.Add(new("planar_boundary_mapping_missing", $"Для interface '{cut.Id}' отсутствует ordered mesh mapping."));

        if (diagnostics.Any(diagnostic => diagnostic.IsError) || nodeIndices is null)
            return new() { Diagnostics = diagnostics };

        var nodesByIndex = snapshot.Nodes.ToDictionary(node => node.Index);
        if (nodeIndices.Count < 2 || nodeIndices.Any(index => !nodesByIndex.ContainsKey(index)))
        {
            diagnostics.Add(new("planar_boundary_mapping_node_invalid", $"Mapping interface '{cut.Id}' содержит неизвестные или недостаточные узлы."));
            return new() { Diagnostics = diagnostics };
        }

        bool reverse = ShouldReverse(nodeIndices, nodesByIndex, cut.Geometry.Points[0], cut.Geometry.Points[^1], cut.ToleranceM);
        if (!EndpointMatches(nodeIndices, nodesByIndex, cut.Geometry.Points[reverse ? ^1 : 0], cut.ToleranceM) ||
            !EndpointMatches(nodeIndices, nodesByIndex, cut.Geometry.Points[reverse ? 0 : ^1], cut.ToleranceM))
            diagnostics.Add(new("planar_boundary_mapping_endpoint_mismatch", $"Mapping interface '{cut.Id}' не покрывает endpoints исходной curve."));

        int[] orderedIndices = reverse ? nodeIndices.Reverse().ToArray() : nodeIndices.ToArray();
        var orderedNodes = new List<PlanarCutInterfaceMeshNode>(orderedIndices.Length);
        var orderedEdges = new List<PlanarMeshEdge>(Math.Max(0, orderedIndices.Length - 1));
        double total = 0;
        for (int index = 1; index < orderedIndices.Length; index++)
        {
            if (!nodesByIndex.TryGetValue(orderedIndices[index - 1], out var a) ||
                !nodesByIndex.TryGetValue(orderedIndices[index], out var b))
                continue;
            double length = Position(a).DistanceTo(Position(b));
            if (length <= 1e-12)
                diagnostics.Add(new("planar_boundary_mapping_degenerate_edge", $"Mapping interface '{cut.Id}' содержит нулевое ребро."));
            total += length;
        }

        if (total <= 1e-12)
            diagnostics.Add(new("planar_boundary_mapping_degenerate", $"Interface '{cut.Id}' имеет нулевую длину."));
        else
        {
            double distance = 0;
            orderedNodes.Add(new(orderedIndices[0], Position(nodesByIndex[orderedIndices[0]]), 0));
            for (int index = 1; index < orderedIndices.Length; index++)
            {
                var previous = Position(nodesByIndex[orderedIndices[index - 1]]);
                var current = Position(nodesByIndex[orderedIndices[index]]);
                distance += previous.DistanceTo(current);
                orderedNodes.Add(new(orderedIndices[index], current, distance / total));
                orderedEdges.Add(new(orderedIndices[index - 1], orderedIndices[index]));
            }
        }

        var mapping = new PlanarCutInterfaceMeshMapping
        {
            InterfaceId = cut.Id,
            SnapshotId = snapshot.Id,
            SnapshotFingerprint = snapshot.InputFingerprint,
            OrderedNodes = orderedNodes,
            OrderedEdges = orderedEdges,
            Orientation = reverse ? PlanarConnectionOrientation.Reverse : PlanarConnectionOrientation.Forward,
            Normal = cut.NormalFromFragmentToOmittedSide,
            Diagnostics = diagnostics
        };
        return new() { Mapping = mapping, Diagnostics = diagnostics };
    }

    static IReadOnlyList<int>? OrderedNodesFromEdges(
        IReadOnlyList<PlanarMeshEdge> edges,
        ICollection<FemValidationDiagnostic> diagnostics)
    {
        if (edges.Count == 0) return null;
        var result = new List<int> { edges[0].A, edges[0].B };
        for (int index = 1; index < edges.Count; index++)
        {
            var edge = edges[index];
            if (result[^1] == edge.A) result.Add(edge.B);
            else if (result[^1] == edge.B) result.Add(edge.A);
            else
            {
                diagnostics.Add(new("planar_boundary_mapping_chain_invalid", "Ordered curve edges не образуют непрерывную цепочку."));
                return null;
            }
        }
        return result;
    }

    static bool ShouldReverse(
        IReadOnlyList<int> indices,
        IReadOnlyDictionary<int, PlanarMeshNode> nodes,
        PlanarPoint2D start,
        PlanarPoint2D end,
        double tolerance)
    {
        double forward = DistanceSquared(nodes[indices[0]], start) + DistanceSquared(nodes[indices[^1]], end);
        double reverse = DistanceSquared(nodes[indices[^1]], start) + DistanceSquared(nodes[indices[0]], end);
        return reverse + tolerance * tolerance < forward;
    }

    static bool EndpointMatches(
        IReadOnlyList<int> indices,
        IReadOnlyDictionary<int, PlanarMeshNode> nodes,
        PlanarPoint2D expected,
        double tolerance)
    {
        return DistanceSquared(nodes[indices[0]], expected) <= tolerance * tolerance ||
               DistanceSquared(nodes[indices[^1]], expected) <= tolerance * tolerance;
    }

    static double DistanceSquared(PlanarMeshNode node, PlanarPoint2D point) =>
        (node.U - point.U) * (node.U - point.U) + (node.V - point.V) * (node.V - point.V);

    static PlanarVector3 Position(PlanarMeshNode node) => new(node.X, node.Y, node.Z);
}

static class PlanarBoundaryVectorExtensions
{
    public static double DistanceTo(this PlanarVector3 first, PlanarVector3 second) =>
        (first - second).Length;
}
