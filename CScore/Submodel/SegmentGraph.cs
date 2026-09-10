using CScore.Fem;

namespace CScore.Submodel;

/// <param name="EndClusters">Кластеры степени 1. Для простой цепочки их ровно два.</param>
public sealed record SegmentComponent(
    IReadOnlyList<int> SegmentIndices,
    IReadOnlyList<int> ClusterIndices,
    IReadOnlyList<int> EndClusters,
    bool IsSimpleChain,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>Разбиение отрезков на компоненты связности и проверка простой двухконцевой цепочки.</summary>
public static class SegmentGraph
{
    public static IReadOnlyList<SegmentComponent> Build(
        IReadOnlyList<BeamSegmentInput> segments,
        NodeClusterSet clusters)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(clusters);

        var parent = Enumerable.Range(0, clusters.Clusters.Count).ToArray();
        for (var i = 0; i < segments.Count; i++)
            Union(parent, clusters.StartCluster[i], clusters.EndCluster[i]);

        var segmentsByRoot = new Dictionary<int, List<int>>();
        for (var i = 0; i < segments.Count; i++)
        {
            var root = Find(parent, clusters.StartCluster[i]);
            if (!segmentsByRoot.TryGetValue(root, out var list)) segmentsByRoot[root] = list = [];
            list.Add(i);
        }

        var result = new List<SegmentComponent>();
        foreach (var (root, segmentIndices) in segmentsByRoot.OrderBy(p => p.Value[0]))
        {
            var clusterIndices = Enumerable.Range(0, clusters.Clusters.Count)
                .Where(c => Find(parent, c) == root).ToList();
            var degree = clusterIndices.ToDictionary(c => c, _ => 0);
            var pairs = new HashSet<(int A, int B)>();
            var diagnostics = new List<FemValidationDiagnostic>();
            var keys = segmentIndices.Select(s => segments[s].SourceKey).ToList();

            foreach (var segmentIndex in segmentIndices)
            {
                var a = clusters.StartCluster[segmentIndex];
                var b = clusters.EndCluster[segmentIndex];
                degree[a]++;
                degree[b]++;

                var pair = a <= b ? (a, b) : (b, a);
                if (!pairs.Add(pair))
                    diagnostics.Add(new("chain_duplicate_segment",
                        $"Элемент {segments[segmentIndex].SourceKey} дублирует уже имеющийся отрезок между теми же узлами.",
                        true, [segments[segmentIndex].SourceKey]));
            }

            var ends = degree.Where(p => p.Value == 1).Select(p => p.Key).OrderBy(c => c).ToList();
            if (degree.Any(p => p.Value > 2))
                diagnostics.Add(new("chain_branching",
                    $"В узле сходится больше двух элементов — ветвление не извлекается как цепочка (элементы: {string.Join(", ", keys)}).",
                    true, keys));

            if (segmentIndices.Count >= clusterIndices.Count)
                diagnostics.Add(new("chain_cycle",
                    $"Выбранные элементы образуют замкнутый контур (элементы: {string.Join(", ", keys)}).",
                    true, keys));

            var isSimpleChain = diagnostics.Count == 0 && ends.Count == 2;
            if (!isSimpleChain && diagnostics.Count == 0)
                diagnostics.Add(new("chain_branching",
                    $"Компонента не является простой цепочкой: концов степени 1 найдено {ends.Count}.", true, keys));

            result.Add(new SegmentComponent(segmentIndices, clusterIndices, ends, isSimpleChain, diagnostics));
        }
        return result;
    }

    static int Find(int[] parent, int value)
    {
        while (parent[value] != value)
        {
            parent[value] = parent[parent[value]];
            value = parent[value];
        }
        return value;
    }

    static void Union(int[] parent, int a, int b)
    {
        var rootA = Find(parent, a);
        var rootB = Find(parent, b);
        if (rootA != rootB) parent[Math.Max(rootA, rootB)] = Math.Min(rootA, rootB);
    }
}
