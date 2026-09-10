using CScore.Fem;
using CScore.Planar;

namespace CScore.Submodel;

/// <param name="DiameterM">Наибольшее расстояние между точками кластера.</param>
public sealed record NodeCluster(
    int Index,
    PlanarVector3 Center,
    double DiameterM,
    IReadOnlyList<string> SourceKeys);

public sealed record NodeClusterSet(
    IReadOnlyList<NodeCluster> Clusters,
    IReadOnlyList<int> StartCluster,
    IReadOnlyList<int> EndCluster,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics)
{
    public bool HasAmbiguous => Diagnostics.Any(d => d.Code == "chain_node_cluster_ambiguous");
}

/// <summary>Кластеризация концов отрезков по координатам.</summary>
public static class NodeClusterBuilder
{
    public static NodeClusterSet Build(IReadOnlyList<BeamSegmentInput> segments, double toleranceM)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (!double.IsFinite(toleranceM) || toleranceM <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(toleranceM));

        var points = new PlanarVector3[segments.Count * 2];
        var owners = new string[points.Length];
        for (var i = 0; i < segments.Count; i++)
        {
            points[2 * i] = segments[i].Start;
            points[2 * i + 1] = segments[i].End;
            owners[2 * i] = segments[i].SourceKey;
            owners[2 * i + 1] = segments[i].SourceKey;
        }

        var spatialIndex = new SpatialPointIndex(toleranceM);
        for (var i = 0; i < points.Length; i++) spatialIndex.Add(i, points[i]);

        var parent = Enumerable.Range(0, points.Length).ToArray();
        var toleranceSq = toleranceM * toleranceM;
        for (var i = 0; i < points.Length; i++)
        foreach (var j in spatialIndex.Neighbors(points[i]))
        {
            if (j <= i) continue;
            if ((points[j] - points[i]).LengthSquared <= toleranceSq) Union(parent, i, j);
        }

        var groups = new Dictionary<int, List<int>>();
        for (var i = 0; i < points.Length; i++)
        {
            var root = Find(parent, i);
            if (!groups.TryGetValue(root, out var members)) groups[root] = members = [];
            members.Add(i);
        }

        var clusters = new List<NodeCluster>();
        var clusterOfPoint = new int[points.Length];
        var diagnostics = new List<FemValidationDiagnostic>();
        foreach (var members in groups.Values.OrderBy(m => m[0]))
        {
            var clusterIndex = clusters.Count;
            var center = PlanarVector3.Zero;
            foreach (var pointId in members)
            {
                center += points[pointId];
                clusterOfPoint[pointId] = clusterIndex;
            }
            center *= 1.0 / members.Count;

            var diameter = 0.0;
            for (var a = 0; a < members.Count; a++)
            for (var b = a + 1; b < members.Count; b++)
                diameter = Math.Max(diameter, (points[members[b]] - points[members[a]]).Length);

            var keys = members.Select(m => owners[m]).Distinct().ToList();
            clusters.Add(new NodeCluster(clusterIndex, center, diameter, keys));
            if (diameter > 2.0 * toleranceM)
                diagnostics.Add(new("chain_node_cluster_ambiguous",
                    $"Узловой кластер имеет размер {diameter * 1000:F1} мм при допуске {toleranceM * 1000:F1} мм — совпадение концов неоднозначно.",
                    true, keys));
        }

        var startCluster = new int[segments.Count];
        var endCluster = new int[segments.Count];
        for (var i = 0; i < segments.Count; i++)
        {
            startCluster[i] = clusterOfPoint[2 * i];
            endCluster[i] = clusterOfPoint[2 * i + 1];
        }
        return new NodeClusterSet(clusters, startCluster, endCluster, diagnostics);
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
