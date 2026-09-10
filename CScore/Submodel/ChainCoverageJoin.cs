using CScore.Fem;
using CScore.Planar;

namespace CScore.Submodel;

/// <summary>Кандидат на извлечение: одна компонента либо фрагменты с продольным разрывом.</summary>
public sealed record ChainCandidate(
    IReadOnlyList<int> SegmentIndices, StraightBeamChain Chain, bool IsStraight, bool HasGap,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics)
{
    public bool IsExtractable => IsStraight && !HasGap && !Diagnostics.Any(d => d.IsError);
    public double TotalLengthM => Chain.Segments.Sum(s => s.LengthM);
    public double SpanM => Chain.LengthM;
    public IReadOnlyList<string> SegmentKeys => Chain.Segments.Select(s => s.Source.SourceKey).ToList();
    public ChainCandidate WithDiagnostics(IReadOnlyList<FemValidationDiagnostic> extra) =>
        extra.Count == 0 ? this : this with { Diagnostics = [.. Diagnostics, .. extra] };
}

/// <summary>Сборка кандидатов со сшивкой коллинеарных компонент, разделённых небольшим разрывом.</summary>
public static class ChainCoverageJoin
{
    public static IReadOnlyList<ChainCandidate> BuildCandidates(
        IReadOnlyList<BeamSegmentInput> segments, NodeClusterSet clusters,
        IReadOnlyList<SegmentComponent> components, PlanarVector3? preferredDirection,
        ResolvedTolerances tolerances)
    {
        var simple = new List<(SegmentComponent Component, StraightnessResult Straightness)>();
        var rejected = new List<ChainCandidate>();
        foreach (var component in components)
        {
            if (!component.IsSimpleChain || component.EndClusters.Count != 2)
                rejected.Add(BuildRejected(component));
            else
                simple.Add((component, ChainStraightnessCheck.Check(segments, clusters, component, tolerances)));
        }

        var candidates = new List<ChainCandidate>();
        foreach (var group in GroupCollinear(clusters, simple, tolerances))
        {
            if (group.Count == 1)
            {
                var (component, straightness) = group[0];
                var chain = ChainOrdering.OrderByTraversal(segments, clusters, component, straightness, preferredDirection, tolerances);
                candidates.Add(new ChainCandidate(component.SegmentIndices, chain, straightness.IsStraight, false, straightness.Diagnostics));
            }
            else
                candidates.Add(BuildJoined(segments, clusters, group, tolerances));
        }
        candidates.AddRange(rejected);
        return candidates;
    }

    static List<List<(SegmentComponent Component, StraightnessResult Straightness)>> GroupCollinear(
        NodeClusterSet clusters, List<(SegmentComponent Component, StraightnessResult Straightness)> simple,
        ResolvedTolerances tolerances)
    {
        var groups = new List<List<(SegmentComponent, StraightnessResult)>>();
        var used = new bool[simple.Count];
        for (var i = 0; i < simple.Count; i++)
        {
            if (used[i]) continue;
            used[i] = true;
            var reference = simple[i].Straightness;
            var extent = Extent(clusters, simple[i].Component, reference.AxisOrigin, reference.AxisDirection);
            var collinear = new List<(int Index, double Min, double Max)> { (i, extent.Min, extent.Max) };
            for (var j = 0; j < simple.Count; j++)
            {
                if (used[j] || j == i || !IsCollinearContinuation(reference, simple[j].Straightness, tolerances)) continue;
                var other = Extent(clusters, simple[j].Component, reference.AxisOrigin, reference.AxisDirection);
                collinear.Add((j, other.Min, other.Max));
            }
            collinear.Sort((a, b) => a.Min.CompareTo(b.Min));
            var anchor = collinear.FindIndex(item => item.Index == i);
            var first = anchor;
            while (first > 0 && IsJoinableGap(collinear[first].Min - collinear[first - 1].Max, tolerances.GapM)) first--;
            var last = anchor;
            while (last + 1 < collinear.Count && IsJoinableGap(collinear[last + 1].Min - collinear[last].Max, tolerances.GapM)) last++;
            var group = new List<(SegmentComponent, StraightnessResult)>();
            for (var k = first; k <= last; k++)
            {
                group.Add(simple[collinear[k].Index]);
                used[collinear[k].Index] = true;
            }
            groups.Add(group);
        }
        return groups;
    }

    static bool IsJoinableGap(double gap, double maximum) => gap > 0.0 && gap <= maximum + 1e-12;

    static bool IsCollinearContinuation(StraightnessResult a, StraightnessResult b, ResolvedTolerances tolerances) =>
        ChainAngleMath.AngleBetweenLinesDeg(a.AxisDirection, b.AxisDirection) <= tolerances.AngularDeg + 1e-12 &&
        (b.AxisOrigin - a.AxisOrigin).Cross(a.AxisDirection).Length <= tolerances.LineDistanceM + 1e-12;

    static (double Min, double Max) Extent(NodeClusterSet clusters, SegmentComponent component,
        PlanarVector3 origin, PlanarVector3 direction)
    {
        var stations = component.ClusterIndices.Select(index => (clusters.Clusters[index].Center - origin).Dot(direction)).ToList();
        return (stations.Min(), stations.Max());
    }

    static ChainCandidate BuildJoined(IReadOnlyList<BeamSegmentInput> segments, NodeClusterSet clusters,
        List<(SegmentComponent Component, StraightnessResult Straightness)> group, ResolvedTolerances tolerances)
    {
        var indices = group.SelectMany(item => item.Component.SegmentIndices).ToList();
        var reference = group[0].Straightness;
        var ordered = group.Select(item => (item.Component,
                Extent: Extent(clusters, item.Component, reference.AxisOrigin, reference.AxisDirection)))
            .OrderBy(item => item.Extent.Min).ToList();
        var diagnostics = new List<FemValidationDiagnostic>();
        for (var i = 1; i < ordered.Count; i++)
        {
            var gap = ordered[i].Extent.Min - ordered[i - 1].Extent.Max;
            var keys = ordered[i - 1].Component.SegmentIndices.Concat(ordered[i].Component.SegmentIndices)
                .Select(index => segments[index].SourceKey).ToList();
            diagnostics.Add(new("chain_gap", $"Между элементами на одной прямой продольный разрыв {gap * 1000:F1} мм — цепочка не является непрерывной.", true, keys));
        }
        diagnostics.AddRange(group.SelectMany(item => item.Straightness.Diagnostics));
        var chain = ChainOrdering.OrderByStation(segments, clusters, indices, reference.AxisOrigin, reference.AxisDirection, tolerances);
        return new ChainCandidate(indices, chain, group.All(item => item.Straightness.IsStraight), true, diagnostics);
    }

    static ChainCandidate BuildRejected(SegmentComponent component) =>
        new(component.SegmentIndices,
            new StraightBeamChain([], [], PlanarVector3.Zero, new PlanarVector3(1, 0, 0), 0.0, [], []),
            false, false, component.Diagnostics);
}
