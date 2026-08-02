using CScore.Fem;

namespace CScore.Planar;

/// <summary>Строит и проверяет mapping одного connection между двумя mesh snapshots.</summary>
public static class PlanarConnectionMapper
{
    public static PlanarConnectionMappingResult Map(
        PlanarConnection connection,
        PlanarRegion regionA,
        PlanarMeshSnapshot sideA,
        PlanarRegion regionB,
        PlanarMeshSnapshot sideB)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(regionA);
        ArgumentNullException.ThrowIfNull(sideA);
        ArgumentNullException.ThrowIfNull(regionB);
        ArgumentNullException.ThrowIfNull(sideB);

        var diagnostics = new List<FemValidationDiagnostic>();
        diagnostics.AddRange(PlanarConnectionValidator.Validate(
            connection,
            new Dictionary<int, PlanarRegion>
            {
                [regionA.Id] = regionA,
                [regionB.Id] = regionB
            }));
        ValidateSnapshot(sideA, connection.SideA.RegionId, diagnostics, "A");
        ValidateSnapshot(sideB, connection.SideB.RegionId, diagnostics, "B");
        if (diagnostics.Any(diagnostic => diagnostic.IsError))
            return new() { Diagnostics = diagnostics };

        var mappingA = FindConstraintMapping(sideA, ConstraintId(connection.Id, regionA.Id), diagnostics, "A");
        var mappingB = FindConstraintMapping(sideB, ConstraintId(connection.Id, regionB.Id), diagnostics, "B");
        if (mappingA is null || mappingB is null)
            return new() { Diagnostics = diagnostics };

        if (!TryBuildSideMapping(
                connection.SideA,
                regionA,
                sideA,
                mappingA,
                connection.MatchingToleranceM,
                ToGlobal(regionA.Frame, connection.SideA.Points[0]),
                ToGlobal(regionA.Frame, connection.SideA.Points[^1]),
                out var sideMappingA,
                diagnostics,
                "A") ||
            !TryBuildSideMapping(
                connection.SideB,
                regionB,
                sideB,
                mappingB,
                connection.MatchingToleranceM,
                ToGlobal(regionA.Frame, connection.SideA.Points[0]),
                ToGlobal(regionA.Frame, connection.SideA.Points[^1]),
                out var sideMappingB,
                diagnostics,
                "B"))
            return new() { Diagnostics = diagnostics };

        var exactPairs = BuildExactPairs(
            connection,
            sideMappingA,
            sideMappingB,
            diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.IsError))
            return new() { Diagnostics = diagnostics };

        var mapping = new PlanarConnectionMeshMapping
        {
            ConnectionId = connection.Id,
            ConnectionFingerprint = PlanarConnectionFingerprint.Compute(connection),
            MeshMode = connection.MeshMode,
            SideASnapshotId = sideA.Id,
            SideAFingerprint = sideA.InputFingerprint,
            SideBSnapshotId = sideB.Id,
            SideBFingerprint = sideB.InputFingerprint,
            SideA = sideMappingA,
            SideB = sideMappingB,
            ExactNodePairs = exactPairs,
            Diagnostics = diagnostics
        };
        return new() { Mapping = mapping, Diagnostics = diagnostics };
    }

    public static IReadOnlyList<FemValidationDiagnostic> ValidateCurrent(
        PlanarConnection connection,
        PlanarRegion regionA,
        PlanarConnectionMeshMapping mapping,
        PlanarMeshSnapshot sideA,
        PlanarRegion regionB,
        PlanarMeshSnapshot sideB)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(regionA);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(sideA);
        ArgumentNullException.ThrowIfNull(regionB);
        ArgumentNullException.ThrowIfNull(sideB);

        var diagnostics = new List<FemValidationDiagnostic>();
        if (mapping.ConnectionId != connection.Id ||
            !string.Equals(mapping.ConnectionFingerprint, PlanarConnectionFingerprint.Compute(connection), StringComparison.Ordinal))
            diagnostics.Add(new("planar_connection_fingerprint_stale", "Connection mapping построен для другого source contract."));
        if (mapping.MeshMode != connection.MeshMode)
            diagnostics.Add(new("planar_connection_mode_mismatch", "Connection mapping построен для другого mesh mode."));
        if (mapping.SideA.RegionId != regionA.Id || mapping.SideB.RegionId != regionB.Id)
            diagnostics.Add(new("planar_connection_snapshot_region_mismatch", "Connection mapping ссылается на другие регионы."));
        if (mapping.SideASnapshotId != sideA.Id || !string.Equals(mapping.SideAFingerprint, sideA.InputFingerprint, StringComparison.Ordinal) ||
            mapping.SideBSnapshotId != sideB.Id || !string.Equals(mapping.SideBFingerprint, sideB.InputFingerprint, StringComparison.Ordinal))
            diagnostics.Add(new("planar_connection_fingerprint_stale", "Connection mapping построен для других mesh snapshots."));
        return diagnostics;
    }

    static string ConstraintId(int connectionId, int regionId) => $"connection:{connectionId}:region:{regionId}";

    static void ValidateSnapshot(
        PlanarMeshSnapshot snapshot,
        int expectedRegionId,
        ICollection<FemValidationDiagnostic> diagnostics,
        string side)
    {
        if (snapshot.RegionId != expectedRegionId)
            diagnostics.Add(new("planar_connection_snapshot_region_mismatch", $"Snapshot стороны {side} принадлежит региону {snapshot.RegionId}, ожидался {expectedRegionId}."));
        if (!snapshot.IsCalculable)
            diagnostics.Add(new("planar_connection_snapshot_not_calculable", $"Snapshot стороны {side} нерасчётен."));
        foreach (var diagnostic in PlanarMeshSnapshotValidator.Validate(snapshot).Where(diagnostic => diagnostic.IsError))
            diagnostics.Add(diagnostic);
    }

    static PlanarConstraintMeshMapping? FindConstraintMapping(
        PlanarMeshSnapshot snapshot,
        string id,
        ICollection<FemValidationDiagnostic> diagnostics,
        string side)
    {
        var matches = snapshot.ConstraintMappings.Where(mapping =>
            string.Equals(mapping.ConstraintObjectId, id, StringComparison.Ordinal)).ToArray();
        if (matches.Length == 0)
        {
            diagnostics.Add(new("planar_connection_mapping_missing", $"Для стороны {side} отсутствует constraint mapping '{id}'."));
            return null;
        }
        if (matches.Length > 1)
        {
            diagnostics.Add(new("planar_connection_mapping_ambiguous", $"Для стороны {side} найдено несколько mappings '{id}'."));
            return null;
        }
        if (matches[0].Diagnostics.Any(diagnostic => diagnostic.IsError))
        {
            foreach (var diagnostic in matches[0].Diagnostics.Where(diagnostic => diagnostic.IsError))
                diagnostics.Add(diagnostic);
            return null;
        }
        return matches[0];
    }

    static bool TryBuildSideMapping(
        ConnectionLocus locus,
        PlanarRegion region,
        PlanarMeshSnapshot snapshot,
        PlanarConstraintMeshMapping mapping,
        double tolerance,
        PlanarVector3 canonicalStart,
        PlanarVector3 canonicalEnd,
        out PlanarConnectionSideMapping result,
        ICollection<FemValidationDiagnostic> diagnostics,
        string side)
    {
        result = new();
        if (!TryWalkChain(mapping.OrderedCurveEdges, out var chain))
        {
            diagnostics.Add(new("planar_connection_chain_invalid", $"Цепочка стороны {side} содержит разрыв, ветвление или цикл."));
            return false;
        }

        var nodes = snapshot.Nodes.ToDictionary(node => node.Index);
        if (chain.Any(index => !nodes.ContainsKey(index)))
        {
            diagnostics.Add(new("planar_connection_chain_invalid", $"Цепочка стороны {side} ссылается на неизвестный mesh node."));
            return false;
        }

        var source = locus.Points.Select(point => ToGlobal(region.Frame, point)).ToArray();
        var positions = chain.Select(index =>
            new PlanarVector3(nodes[index].X, nodes[index].Y, nodes[index].Z)).ToArray();
        bool direct = Close(positions[0], canonicalStart, tolerance) && Close(positions[^1], canonicalEnd, tolerance);
        bool reverse = Close(positions[0], canonicalEnd, tolerance) && Close(positions[^1], canonicalStart, tolerance);
        if (direct == reverse)
        {
            diagnostics.Add(new("planar_connection_orientation_ambiguous", $"Ориентация mesh chain стороны {side} не определяется однозначно."));
            return false;
        }
        if (positions.Any(position => PointToPolylineDistance(position, source) > tolerance))
        {
            diagnostics.Add(new("planar_connection_chain_invalid", $"Mesh chain стороны {side} выходит за пределы исходного spatial locus."));
            return false;
        }

        if (reverse)
        {
            chain = chain.Reverse().ToArray();
            positions = positions.Reverse().ToArray();
        }

        var meshNodes = new List<PlanarConnectionMeshNode>(positions.Length);
        var total = Length(positions);
        if (total <= 1e-12)
        {
            diagnostics.Add(new("planar_connection_chain_invalid", $"Mesh chain стороны {side} имеет нулевую длину."));
            return false;
        }
        double cumulative = 0;
        for (var index = 0; index < positions.Length; index++)
        {
            if (index > 0) cumulative += (positions[index] - positions[index - 1]).Length;
            meshNodes.Add(new(chain[index], positions[index], cumulative / total));
        }

        result = new()
        {
            RegionId = snapshot.RegionId,
            ConstraintObjectId = mapping.ConstraintObjectId,
            Orientation = reverse ? PlanarConnectionOrientation.Reverse : PlanarConnectionOrientation.Forward,
            OrderedNodeIndices = chain,
            OrderedEdges = BuildEdges(chain),
            Nodes = meshNodes
        };
        return true;
    }

    static IReadOnlyList<PlanarConnectionNodePair> BuildExactPairs(
        PlanarConnection connection,
        PlanarConnectionSideMapping sideA,
        PlanarConnectionSideMapping sideB,
        ICollection<FemValidationDiagnostic> diagnostics)
    {
        if (connection.MeshMode != PlanarConnectionMeshMode.ConformingPartition)
            return [];
        if (sideA.Nodes.Count != sideB.Nodes.Count)
        {
            diagnostics.Add(new("planar_connection_conforming_partition_mismatch", "Conforming chains имеют разное число узлов."));
            return [];
        }

        var pairs = new List<PlanarConnectionNodePair>(sideA.Nodes.Count);
        for (var index = 0; index < sideA.Nodes.Count; index++)
        {
            var distance = (sideA.Nodes[index].Position - sideB.Nodes[index].Position).Length;
            if (distance > connection.MatchingToleranceM)
            {
                diagnostics.Add(new("planar_connection_conforming_partition_mismatch", $"Узлы conforming chains с индексом {index} не совпадают."));
                return [];
            }
            pairs.Add(new(sideA.Nodes[index].NodeIndex, sideB.Nodes[index].NodeIndex, distance));
        }
        return pairs;
    }

    static bool TryWalkChain(IReadOnlyList<PlanarMeshEdge> edges, out int[] chain)
    {
        chain = [];
        if (edges.Count == 0) return false;
        foreach (var first in new[] { (edges[0].A, edges[0].B), (edges[0].B, edges[0].A) })
        {
            var result = new List<int> { first.Item1, first.Item2 };
            var used = new HashSet<int> { 0 };
            for (var index = 1; index < edges.Count; index++)
            {
                var candidates = new List<(int Index, int Next)>();
                for (var candidateIndex = 1; candidateIndex < edges.Count; candidateIndex++)
                {
                    if (used.Contains(candidateIndex)) continue;
                    var edge = edges[candidateIndex];
                    if (edge.A == result[^1]) candidates.Add((candidateIndex, edge.B));
                    if (edge.B == result[^1]) candidates.Add((candidateIndex, edge.A));
                }
                if (candidates.Count != 1) break;
                used.Add(candidates[0].Index);
                result.Add(candidates[0].Next);
            }
            if (used.Count == edges.Count && result.Distinct().Count() == result.Count)
            {
                chain = result.ToArray();
                return true;
            }
        }
        return false;
    }

    static IReadOnlyList<PlanarMeshEdge> BuildEdges(IReadOnlyList<int> chain) =>
        Enumerable.Range(0, chain.Count - 1)
            .Select(index => new PlanarMeshEdge(chain[index], chain[index + 1]))
            .ToArray();

    static PlanarVector3 ToGlobal(Frame3D frame, PlanarPoint2D point) =>
        frame.Origin + frame.LocalX * point.U + frame.LocalY * point.V;

    static double Length(IReadOnlyList<PlanarVector3> points) =>
        Enumerable.Range(0, points.Count - 1)
            .Sum(index => (points[index + 1] - points[index]).Length);

    static double PointToPolylineDistance(PlanarVector3 point, IReadOnlyList<PlanarVector3> polyline)
    {
        double best = double.PositiveInfinity;
        for (var index = 0; index < polyline.Count - 1; index++)
        {
            var start = polyline[index];
            var end = polyline[index + 1];
            var direction = end - start;
            var lengthSquared = direction.LengthSquared;
            var parameter = lengthSquared <= 1e-24
                ? 0
                : Math.Clamp((point - start).Dot(direction) / lengthSquared, 0, 1);
            best = Math.Min(best, (point - (start + direction * parameter)).Length);
        }
        return best;
    }

    static bool Close(PlanarVector3 first, PlanarVector3 second, double tolerance) =>
        (first - second).Length <= tolerance;
}
