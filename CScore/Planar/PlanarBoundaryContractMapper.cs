using CScore.Fem;

namespace CScore.Planar;

/// <summary>Преобразует разметку BoundarySegment в явные узлы и рёбра snapshot.</summary>
public static class PlanarBoundaryContractMapper
{
    public static PlanarBoundaryContractResult Map(
        PlanarRegion region,
        PlanarMeshSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(snapshot);

        var diagnostics = new List<FemValidationDiagnostic>();
        var nodes = snapshot.Nodes.Select(node => node.Index).ToHashSet();
        var segmentKeys = new List<(PlanarBoundaryKey Key, BoundaryRole Role)>();
        var seenSegments = new HashSet<PlanarBoundaryKey>();
        foreach (BoundarySegment segment in region.BoundarySegments)
        {
            var key = new PlanarBoundaryKey(segment.Loop, segment.HoleIndex, segment.StartVertex, segment.EndVertex);
            if (!seenSegments.Add(key))
            {
                diagnostics.Add(new("planar_boundary_segment_duplicate", $"Граница {Format(key)} повторяется в PlanarRegion."));
                continue;
            }
            segmentKeys.Add((key, segment.Role));
        }

        var mappingByKey = new Dictionary<PlanarBoundaryKey, PlanarMeshBoundaryMapping>();
        foreach (PlanarMeshBoundaryMapping mapping in snapshot.BoundaryMappings)
        {
            if (!mappingByKey.TryAdd(mapping.Key, mapping))
            {
                diagnostics.Add(new("planar_boundary_mapping_duplicate", $"Mapping границы {Format(mapping.Key)} повторяется в snapshot."));
                continue;
            }

            if (mapping.NodeIndices.Count < 2)
                diagnostics.Add(new("planar_boundary_mapping_node_count", $"Mapping границы {Format(mapping.Key)} должен содержать минимум два узла."));
            for (int i = 0; i < mapping.NodeIndices.Count; i++)
            {
                if (!nodes.Contains(mapping.NodeIndices[i]))
                    diagnostics.Add(new("planar_boundary_mapping_unknown_node", $"Mapping границы {Format(mapping.Key)} ссылается на неизвестный узел {mapping.NodeIndices[i]}."));
                if (i > 0 && mapping.NodeIndices[i] == mapping.NodeIndices[i - 1])
                    diagnostics.Add(new("planar_boundary_mapping_repeated_node", $"Mapping границы {Format(mapping.Key)} содержит соседние одинаковые узлы."));
            }
        }

        foreach (var (key, _) in segmentKeys)
            if (!mappingByKey.ContainsKey(key))
                diagnostics.Add(new("planar_boundary_mapping_missing", $"Для размеченной границы {Format(key)} отсутствует mapping snapshot."));

        foreach (PlanarBoundaryKey key in mappingByKey.Keys)
            if (!seenSegments.Contains(key))
                diagnostics.Add(new("planar_boundary_segment_unexpected", $"Snapshot содержит mapping границы {Format(key)}, которой нет в PlanarRegion."));

        var sets = new List<PlanarBoundarySet>();
        if (diagnostics.Count == 0)
        {
            foreach (var group in segmentKeys.GroupBy(item => item.Role))
            {
                var keys = group.Select(item => item.Key).ToArray();
                var nodesInSet = new List<int>();
                var seenNodes = new HashSet<int>();
                var edges = new List<(int A, int B)>();
                var seenEdges = new HashSet<(int A, int B)>();
                foreach (PlanarBoundaryKey key in keys)
                {
                    IReadOnlyList<int> chain = mappingByKey[key].NodeIndices;
                    foreach (int node in chain)
                        if (seenNodes.Add(node)) nodesInSet.Add(node);
                    for (int i = 1; i < chain.Count; i++)
                    {
                        var edge = (A: chain[i - 1], B: chain[i]);
                        var canonical = edge.A < edge.B ? edge : (edge.B, edge.A);
                        if (seenEdges.Add(canonical)) edges.Add(edge);
                    }
                }
                sets.Add(new(group.Key, keys, nodesInSet, edges));
            }
        }

        return new(diagnostics.Count == 0, diagnostics, sets);
    }

    static string Format(PlanarBoundaryKey key) =>
        $"{key.Loop}:{key.HoleIndex}:{key.StartVertex}-{key.EndVertex}";
}
