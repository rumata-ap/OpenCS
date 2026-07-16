namespace CScore.Fem.Combinations;

/// <summary>Строитель глобального вектора узловых нагрузок FEM-схемы.</summary>
public static class FemLoadVectorBuilder
{
    /// <summary>Строит вектор в порядке Fx,Fy,Fz,Mx,My,Mz для каждого узла.</summary>
    public static double[] Build(
        IReadOnlyList<FemNode> nodes,
        IReadOnlyList<FemNodeLoad> loads,
        IReadOnlyList<int> orderedNodeIds,
        int loadCaseId)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(loads);
        ArgumentNullException.ThrowIfNull(orderedNodeIds);

        var nodeIds = new HashSet<int>();
        foreach (var node in nodes)
            if (!nodeIds.Add(node.Id))
                throw new ArgumentException($"Узел с ID {node.Id} задан более одного раза.", nameof(nodes));

        var indexes = new Dictionary<int, int>();
        for (int i = 0; i < orderedNodeIds.Count; i++)
        {
            int nodeId = orderedNodeIds[i];
            if (!nodeIds.Contains(nodeId))
                throw new ArgumentException($"Узел с ID {nodeId} отсутствует в схеме.", nameof(orderedNodeIds));
            if (!indexes.TryAdd(nodeId, i))
                throw new ArgumentException($"Узел с ID {nodeId} повторяется в порядке сборки.", nameof(orderedNodeIds));
        }

        var vector = new double[6 * orderedNodeIds.Count];
        foreach (var load in loads)
        {
            if (load.LoadCaseId != loadCaseId)
                throw new ArgumentException(
                    $"Нагрузка {load.Id} относится к загружению {load.LoadCaseId}, ожидалось {loadCaseId}.",
                    nameof(loads));
            if (!indexes.TryGetValue(load.NodeId, out int nodeIndex))
                throw new ArgumentException($"Нагрузка ссылается на неизвестный узел {load.NodeId}.", nameof(loads));

            int offset = nodeIndex * 6;
            vector[offset] += load.Fx;
            vector[offset + 1] += load.Fy;
            vector[offset + 2] += load.Fz;
            vector[offset + 3] += load.Mx;
            vector[offset + 4] += load.My;
            vector[offset + 5] += load.Mz;
        }

        return vector;
    }
}
