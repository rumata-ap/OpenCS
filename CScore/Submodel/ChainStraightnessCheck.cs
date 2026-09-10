using CScore.Fem;
using CScore.Planar;

namespace CScore.Submodel;

/// <summary>Результат проверки прямолинейности компоненты.</summary>
public sealed record StraightnessResult(
    PlanarVector3 AxisOrigin, PlanarVector3 AxisDirection, bool IsStraight,
    double MaxNodeOffsetM, string MaxNodeOffsetKey, double MaxAngleDeg, string MaxAngleKey,
    int LinearCriterionSegments, double MaxOverlapM, string? MaxOverlapKey,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>Каноническая ось компоненты и проверка прямолинейности.</summary>
public static class ChainStraightnessCheck
{
    public static StraightnessResult Check(
        IReadOnlyList<BeamSegmentInput> segments, NodeClusterSet clusters,
        SegmentComponent component, ResolvedTolerances tolerances)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(clusters);
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(tolerances);
        if (component.EndClusters.Count != 2)
            throw new ArgumentException("Ось строится только для компоненты с двумя концами.", nameof(component));

        var origin = clusters.Clusters[component.EndClusters[0]].Center;
        var far = clusters.Clusters[component.EndClusters[1]].Center;
        var axisVector = far - origin;
        if (axisVector.Length <= 0.0)
            throw new ArgumentException("Концы компоненты совпадают — ось не определена.", nameof(component));
        var direction = axisVector.Normalize();
        var diagnostics = new List<FemValidationDiagnostic>();

        var maxOffset = 0.0;
        var maxOffsetKey = clusters.Clusters[component.EndClusters[0]].SourceKeys.FirstOrDefault() ?? "";
        foreach (var clusterIndex in component.ClusterIndices)
        {
            var cluster = clusters.Clusters[clusterIndex];
            var offset = (cluster.Center - origin).Cross(direction).Length;
            if (offset <= maxOffset) continue;
            maxOffset = offset;
            maxOffsetKey = cluster.SourceKeys.FirstOrDefault() ?? "";
        }
        if (ExceedsTolerance(maxOffset, tolerances.LineDistanceM))
            diagnostics.Add(new("chain_not_straight",
                $"Узел отклоняется от оси цепочки на {maxOffset * 1000:F2} мм при допуске {tolerances.LineDistanceM * 1000:F2} мм.",
                true, [maxOffsetKey]));

        var maxAngle = 0.0;
        var maxAngleKey = "";
        var linearCriterionSegments = 0;
        foreach (var index in component.SegmentIndices)
        {
            var segment = segments[index];
            var angleTolerance = ChainAngleMath.AngleToleranceDeg(segment.LengthM, tolerances.LineDistanceM, tolerances.AngularDeg);
            if (angleTolerance is null)
            {
                linearCriterionSegments++;
                continue;
            }
            var angle = ChainAngleMath.AngleBetweenLinesDeg(segment.Delta, direction);
            if (angle > maxAngle)
            {
                maxAngle = angle;
                maxAngleKey = segment.SourceKey;
            }
            if (ExceedsTolerance(angle, angleTolerance.Value))
                diagnostics.Add(new("chain_not_straight",
                    $"Элемент {segment.SourceKey} отклоняется от оси цепочки на {angle:F3}° при допуске {angleTolerance.Value:F3}°.",
                    true, [segment.SourceKey]));
        }

        var (maxOverlap, overlapKey) = CheckOverlaps(segments, component, origin, direction, tolerances, diagnostics);
        return new StraightnessResult(origin, direction, diagnostics.Count == 0, maxOffset, maxOffsetKey,
            maxAngle, maxAngleKey, linearCriterionSegments, maxOverlap, overlapKey, diagnostics);
    }

    static (double MaxOverlapM, string? Key) CheckOverlaps(
        IReadOnlyList<BeamSegmentInput> segments, SegmentComponent component,
        PlanarVector3 origin, PlanarVector3 direction, ResolvedTolerances tolerances,
        List<FemValidationDiagnostic> diagnostics)
    {
        var intervals = component.SegmentIndices.Select(index =>
        {
            var a = (segments[index].Start - origin).Dot(direction);
            var b = (segments[index].End - origin).Dot(direction);
            return (Key: segments[index].SourceKey, Min: Math.Min(a, b), Max: Math.Max(a, b));
        }).OrderBy(interval => interval.Min).ToList();

        var maxOverlap = 0.0;
        string? key = null;
        for (var i = 1; i < intervals.Count; i++)
        {
            var overlap = intervals[i - 1].Max - intervals[i].Min;
            if (overlap <= maxOverlap) continue;
            maxOverlap = overlap;
            key = intervals[i].Key;
        }
        if (ExceedsTolerance(maxOverlap, tolerances.OverlapM) && key is not null)
            diagnostics.Add(new("chain_overlap",
                $"Элементы налагаются вдоль оси цепочки на {maxOverlap * 1000:F2} мм при допуске {tolerances.OverlapM * 1000:F2} мм.",
                true, [key]));
        return (maxOverlap, key);
    }

    /// <summary>Защищает включённую границу инженерного допуска от погрешности
    /// жёсткого поворота и скалярных произведений.</summary>
    static bool ExceedsTolerance(double value, double tolerance) =>
        value > tolerance + 1e-12 * Math.Max(1.0, Math.Abs(tolerance));
}
