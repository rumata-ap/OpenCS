using CScore.Planar;

namespace CScore.Submodel;

/// <summary>Обход цепочки от конца к концу и сортировка вдоль оси.</summary>
public static class ChainOrdering
{
    public static StraightBeamChain OrderByTraversal(
        IReadOnlyList<BeamSegmentInput> segments, NodeClusterSet clusters, SegmentComponent component,
        StraightnessResult straightness, PlanarVector3? preferredDirection, ResolvedTolerances tolerances)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var startCluster = ChooseStart(clusters, component, straightness, preferredDirection, tolerances);
        var direction = component.EndClusters[0] == startCluster ? straightness.AxisDirection : straightness.AxisDirection * -1.0;
        var origin = clusters.Clusters[startCluster].Center;
        var incident = new Dictionary<int, List<int>>();
        foreach (var index in component.SegmentIndices)
        {
            Add(incident, clusters.StartCluster[index], index);
            Add(incident, clusters.EndCluster[index], index);
        }

        var orderedSegments = new List<OrderedBeamSegment>();
        var orderedClusters = new List<int> { startCluster };
        var visited = new HashSet<int>();
        var current = startCluster;
        while (true)
        {
            var next = incident[current].FirstOrDefault(index => !visited.Contains(index), -1);
            if (next < 0) break;
            visited.Add(next);
            var reversed = clusters.StartCluster[next] != current;
            var other = reversed ? clusters.StartCluster[next] : clusters.EndCluster[next];
            orderedSegments.Add(BuildOrdered(segments[next], reversed, origin, direction));
            orderedClusters.Add(other);
            current = other;
        }

        var nodes = BuildNodes(clusters, orderedClusters, origin, direction);
        return new StraightBeamChain(orderedSegments, nodes, origin, direction,
            nodes[^1].Station - nodes[0].Station, [], []);
    }

    public static StraightBeamChain OrderByStation(
        IReadOnlyList<BeamSegmentInput> segments, NodeClusterSet clusters, IReadOnlyList<int> segmentIndices,
        PlanarVector3 axisOrigin, PlanarVector3 axisDirection, ResolvedTolerances tolerances)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var direction = axisDirection.Normalize();
        var sequence = segmentIndices.OrderBy(index => Math.Min(
            (segments[index].Start - axisOrigin).Dot(direction), (segments[index].End - axisOrigin).Dot(direction))).ToList();
        var ordered = sequence.Select(index => BuildOrdered(segments[index], IsReversedAlong(segments[index], direction), axisOrigin, direction)).ToList();
        var clusterOrder = new List<int>();
        foreach (var index in sequence)
        {
            var reversed = IsReversedAlong(segments[index], direction);
            var first = reversed ? clusters.EndCluster[index] : clusters.StartCluster[index];
            var second = reversed ? clusters.StartCluster[index] : clusters.EndCluster[index];
            if (clusterOrder.Count == 0 || clusterOrder[^1] != first) clusterOrder.Add(first);
            clusterOrder.Add(second);
        }
        var nodes = BuildNodes(clusters, clusterOrder, axisOrigin, direction);
        return new StraightBeamChain(ordered, nodes, axisOrigin, direction,
            nodes[^1].Station - nodes[0].Station, [], []);
    }

    static OrderedBeamSegment BuildOrdered(BeamSegmentInput segment, bool reversed, PlanarVector3 origin, PlanarVector3 direction)
    {
        var start = reversed ? segment.End : segment.Start;
        var end = reversed ? segment.Start : segment.End;
        return new OrderedBeamSegment(segment, reversed, (start - origin).Dot(direction), (end - origin).Dot(direction),
            segment.LengthM, ChainAngleMath.AngleBetweenLinesDeg(segment.Delta, direction));
    }

    static IReadOnlyList<ChainNode> BuildNodes(NodeClusterSet clusters, IReadOnlyList<int> clusterOrder,
        PlanarVector3 origin, PlanarVector3 direction) =>
        clusterOrder.Select((clusterIndex, i) =>
        {
            var cluster = clusters.Clusters[clusterIndex];
            return new ChainNode(cluster.Center, (cluster.Center - origin).Dot(direction),
                i == 0 || i == clusterOrder.Count - 1, cluster.SourceKeys, cluster.DiameterM);
        }).ToList();

    static int ChooseStart(NodeClusterSet clusters, SegmentComponent component, StraightnessResult straightness,
        PlanarVector3? preferredDirection, ResolvedTolerances tolerances)
    {
        var first = component.EndClusters[0];
        var second = component.EndClusters[1];
        if (preferredDirection is { } preferred && preferred.Length > 0.0 &&
            ChainAngleMath.AngleBetweenLinesDeg(preferred, straightness.AxisDirection) <= tolerances.AngularDeg)
            return preferred.Dot(clusters.Clusters[second].Center - clusters.Clusters[first].Center) >= 0.0 ? first : second;
        return CompareLexicographic(clusters.Clusters[first].Center, clusters.Clusters[second].Center,
            tolerances.NodeCoincidenceM) <= 0 ? first : second;
    }

    static bool IsReversedAlong(BeamSegmentInput segment, PlanarVector3 direction) => segment.Delta.Dot(direction) < 0.0;

    static int CompareLexicographic(PlanarVector3 a, PlanarVector3 b, double tolerance)
    {
        var x = Compare(a.X, b.X, tolerance);
        if (x != 0) return x;
        var y = Compare(a.Y, b.Y, tolerance);
        return y != 0 ? y : Compare(a.Z, b.Z, tolerance);
    }

    static int Compare(double a, double b, double tolerance) => Math.Abs(a - b) <= tolerance ? 0 : a.CompareTo(b);

    static void Add(Dictionary<int, List<int>> map, int key, int value)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        list.Add(value);
    }
}
