using CScore.Fem;

namespace CScore.Planar;

/// <summary>Проверяет source contract связи до запуска двух независимых meshing operations.</summary>
public static class PlanarConnectionValidator
{
    const double GeometryTolerance = 1e-12;

    public static IReadOnlyList<FemValidationDiagnostic> Validate(
        PlanarConnection connection,
        IReadOnlyDictionary<int, PlanarRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(regions);

        var diagnostics = new List<FemValidationDiagnostic>();
        if (connection.Id <= 0)
            diagnostics.Add(new("planar_connection_id_invalid", "Connection должен иметь положительный ID."));
        if (!double.IsFinite(connection.MatchingToleranceM) || connection.MatchingToleranceM <= 0)
            diagnostics.Add(new("planar_connection_tolerance_invalid", "Допуск connection должен быть положительным конечным числом."));

        if (connection.SideA is null || connection.SideB is null)
        {
            diagnostics.Add(new("planar_connection_locus_invalid", "Обе стороны connection должны быть заданы."));
            return diagnostics;
        }

        if (connection.SideA.RegionId == connection.SideB.RegionId)
            diagnostics.Add(new("planar_connection_same_region", "Обе стороны connection не могут ссылаться на один регион."));

        if (!regions.TryGetValue(connection.SideA.RegionId, out var regionA))
            diagnostics.Add(new("planar_connection_region_unknown", $"Регион {connection.SideA.RegionId} для стороны A не найден."));
        if (!regions.TryGetValue(connection.SideB.RegionId, out var regionB))
            diagnostics.Add(new("planar_connection_region_unknown", $"Регион {connection.SideB.RegionId} для стороны B не найден."));

        bool validA = ValidateLocus(connection.SideA, regionA, connection.MeshMode, connection.MatchingToleranceM, diagnostics, "A");
        bool validB = ValidateLocus(connection.SideB, regionB, connection.MeshMode, connection.MatchingToleranceM, diagnostics, "B");
        if (validA && validB && regionA is not null && regionB is not null)
            ValidateSpatialLocus(connection, regionA, regionB, diagnostics);

        return diagnostics;
    }

    static bool ValidateLocus(
        ConnectionLocus locus,
        PlanarRegion? region,
        PlanarConnectionMeshMode mode,
        double tolerance,
        ICollection<FemValidationDiagnostic> diagnostics,
        string side)
    {
        var points = locus.Points;
        bool valid = true;
        if (points is null || points.Count < 2)
        {
            diagnostics.Add(new("planar_connection_locus_invalid", $"Locus стороны {side} должен содержать минимум две точки."));
            return false;
        }

        if (points.Any(point => !point.IsFinite))
        {
            diagnostics.Add(new("planar_connection_locus_invalid", $"Locus стороны {side} содержит нечисловую координату."));
            valid = false;
        }

        if (HasZeroSegment(points))
        {
            diagnostics.Add(new("planar_connection_locus_invalid", $"Locus стороны {side} содержит сегмент нулевой длины."));
            valid = false;
        }

        if (HasSelfIntersection(points))
        {
            diagnostics.Add(new("planar_connection_locus_invalid", $"Locus стороны {side} имеет самопересечение."));
            valid = false;
        }

        if (region is not null && points.All(point => point.IsFinite))
        {
            var facet = mode == PlanarConnectionMeshMode.ConformingPartition
                ? PlanarMeshKind.ConformingPartition
                : PlanarMeshKind.EmbeddedCurve;
            var constraint = PlanarConstraintObject.Curve(
                $"connection-validation-{side}",
                points,
                new PlanarStructuralFacet(PlanarStructuralKind.None),
                new PlanarMeshFacet(facet),
                $"connection-validation-{side}");
            foreach (var diagnostic in PlanarConstraintValidator.Validate(region, [constraint]))
                diagnostics.Add(diagnostic);
        }

        return valid && !diagnostics.Any(diagnostic =>
            diagnostic.Code is "planar_connection_locus_invalid" &&
            diagnostic.Message.Contains($"стороны {side}", StringComparison.Ordinal));
    }

    static void ValidateSpatialLocus(
        PlanarConnection connection,
        PlanarRegion regionA,
        PlanarRegion regionB,
        ICollection<FemValidationDiagnostic> diagnostics)
    {
        var globalA = connection.SideA.Points.Select(point => ToGlobal(regionA.Frame, point)).ToArray();
        var globalB = connection.SideB.Points.Select(point => ToGlobal(regionB.Frame, point)).ToArray();
        var tolerance = connection.MatchingToleranceM;
        bool direct = Close(globalA[0], globalB[0], tolerance) && Close(globalA[^1], globalB[^1], tolerance);
        bool reverse = Close(globalA[0], globalB[^1], tolerance) && Close(globalA[^1], globalB[0], tolerance);
        if (!direct && !reverse)
        {
            diagnostics.Add(new("planar_connection_locus_space_mismatch", "Начала и концы двух connection locus-ов не совпадают в глобальном пространстве."));
            return;
        }
        if (direct && reverse)
        {
            diagnostics.Add(new("planar_connection_orientation_ambiguous", "Ориентацию сторон connection нельзя определить однозначно."));
            return;
        }

        if (Math.Abs(Length(globalA) - Length(globalB)) > tolerance ||
            globalA.Any(point => PointToPolylineDistance(point, globalB) > tolerance) ||
            globalB.Any(point => PointToPolylineDistance(point, globalA) > tolerance))
            diagnostics.Add(new("planar_connection_locus_space_mismatch", "Две connection polyline не совпадают в пределах заданного допуска."));
    }

    static PlanarVector3 ToGlobal(Frame3D frame, PlanarPoint2D point) =>
        frame.Origin + frame.LocalX * point.U + frame.LocalY * point.V;

    static bool HasZeroSegment(IReadOnlyList<PlanarPoint2D> points) =>
        Enumerable.Range(0, points.Count - 1).Any(index => DistanceSquared(points[index], points[index + 1]) <= GeometryTolerance * GeometryTolerance);

    static bool HasSelfIntersection(IReadOnlyList<PlanarPoint2D> points)
    {
        for (var first = 0; first < points.Count - 1; first++)
        for (var second = first + 1; second < points.Count - 1; second++)
        {
            if (second - first <= 1) continue;
            if (SegmentsIntersect(points[first], points[first + 1], points[second], points[second + 1])) return true;
        }
        return false;
    }

    static bool SegmentsIntersect(PlanarPoint2D a, PlanarPoint2D b, PlanarPoint2D c, PlanarPoint2D d)
    {
        double abC = Orientation(a, b, c);
        double abD = Orientation(a, b, d);
        double cdA = Orientation(c, d, a);
        double cdB = Orientation(c, d, b);
        return abC * abD < -GeometryTolerance && cdA * cdB < -GeometryTolerance;
    }

    static double Orientation(PlanarPoint2D a, PlanarPoint2D b, PlanarPoint2D c) =>
        (b.U - a.U) * (c.V - a.V) - (b.V - a.V) * (c.U - a.U);

    static double DistanceSquared(PlanarPoint2D a, PlanarPoint2D b) =>
        Math.Pow(a.U - b.U, 2) + Math.Pow(a.V - b.V, 2);

    static double Length(IReadOnlyList<PlanarVector3> points) =>
        Enumerable.Range(0, points.Count - 1)
            .Sum(index => (points[index + 1] - points[index]).Length);

    static double PointToPolylineDistance(PlanarVector3 point, IReadOnlyList<PlanarVector3> polyline)
    {
        double best = double.PositiveInfinity;
        for (var index = 0; index < polyline.Count - 1; index++)
            best = Math.Min(best, PointToSegmentDistance(point, polyline[index], polyline[index + 1]));
        return best;
    }

    static double PointToSegmentDistance(PlanarVector3 point, PlanarVector3 start, PlanarVector3 end)
    {
        var direction = end - start;
        var lengthSquared = direction.LengthSquared;
        if (lengthSquared <= GeometryTolerance * GeometryTolerance) return (point - start).Length;
        var parameter = Math.Clamp((point - start).Dot(direction) / lengthSquared, 0, 1);
        return (point - (start + direction * parameter)).Length;
    }

    static bool Close(PlanarVector3 first, PlanarVector3 second, double tolerance) =>
        (first - second).Length <= tolerance;
}
