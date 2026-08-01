namespace CScore.Planar;

/// <summary>Извлекает уникальные рёбра периметра T3/Q4-элементов снимка сетки для отрисовки.
/// Только периметр каждого элемента (без диагоналей — рёбра, а не триангуляция заливки), общие
/// рёбра соседних элементов не дублируются.</summary>
public static class PlanarMeshEdgeExtractor
{
    public static IReadOnlyList<(int A, int B)> ExtractEdges(PlanarMeshSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var seen = new HashSet<(int, int)>();
        var result = new List<(int A, int B)>();
        foreach (var element in snapshot.Elements)
        {
            var nodes = element.NodeIndices;
            for (int i = 0; i < nodes.Count; i++)
            {
                int a = nodes[i];
                int b = nodes[(i + 1) % nodes.Count];
                var key = a < b ? (a, b) : (b, a);
                if (seen.Add(key))
                    result.Add(key);
            }
        }
        return result;
    }
}
