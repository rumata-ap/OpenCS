using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public sealed class PlanarConnectionMapperTests
{
    [Fact]
    public void Map_EmbeddedLocusAllowsDifferentPartitions()
    {
        var connection = Connection(PlanarConnectionMeshMode.EmbeddedLocus);
        var regionA = RegionA(10);
        var regionB = RegionB(20);
        var sideA = SnapshotA(connection, [1, 2, 3], "snapshot-a");
        var sideB = SnapshotB(connection, [3, 2.5, 2, 1.5, 1], "snapshot-b");

        var result = PlanarConnectionMapper.Map(connection, regionA, sideA, regionB, sideB);

        Assert.True(result.IsCalculable, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotNull(result.Mapping);
        Assert.Empty(result.Mapping!.ExactNodePairs);
        Assert.Equal(5, result.Mapping.SideB.Nodes.Count);
    }

    [Fact]
    public void Map_ConformingPartitionCreatesPairsAndNormalizesReverse()
    {
        var connection = Connection(PlanarConnectionMeshMode.ConformingPartition);
        var regionA = RegionA(10);
        var regionB = RegionB(20);
        var sideA = SnapshotA(connection, [1, 2, 3], "snapshot-a");
        var sideB = SnapshotB(connection, [3, 2, 1], "snapshot-b");

        var result = PlanarConnectionMapper.Map(connection, regionA, sideA, regionB, sideB);

        Assert.True(result.IsCalculable, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(PlanarConnectionOrientation.Reverse, result.Mapping!.SideB.Orientation);
        Assert.Equal(
            [new PlanarConnectionNodePair(0, 2, 0), new PlanarConnectionNodePair(1, 1, 0), new PlanarConnectionNodePair(2, 0, 0)],
            result.Mapping.ExactNodePairs);
    }

    [Fact]
    public void Map_ConformingPartitionBlocksCoordinateMismatch()
    {
        var connection = Connection(PlanarConnectionMeshMode.ConformingPartition);
        var regionA = RegionA(10);
        var regionB = RegionB(20);
        var sideA = SnapshotA(connection, [1, 2, 3], "snapshot-a");
        var sideB = SnapshotB(connection, [3, 2.1, 1], "snapshot-b");

        var result = PlanarConnectionMapper.Map(connection, regionA, sideA, regionB, sideB);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, item => item.Code == "planar_connection_conforming_partition_mismatch");
    }

    [Fact]
    public void ValidateCurrent_RejectsChangedSnapshotFingerprint()
    {
        var connection = Connection(PlanarConnectionMeshMode.EmbeddedLocus);
        var regionA = RegionA(10);
        var regionB = RegionB(20);
        var sideA = SnapshotA(connection, [1, 2, 3], "snapshot-a");
        var sideB = SnapshotB(connection, [3, 2, 1], "snapshot-b");
        var mapped = PlanarConnectionMapper.Map(connection, regionA, sideA, regionB, sideB);
        var changedSideA = Copy(sideA, "changed");

        var diagnostics = PlanarConnectionMapper.ValidateCurrent(
            connection, regionA, mapped.Mapping!, changedSideA, regionB, sideB);

        Assert.Contains(diagnostics, item => item.Code == "planar_connection_fingerprint_stale");
    }

    static PlanarConnection Connection(PlanarConnectionMeshMode mode) => new()
    {
        Id = 7,
        MeshMode = mode,
        SideA = new ConnectionLocus(10, [new(2, 1), new(2, 3)]),
        SideB = new ConnectionLocus(20, [new(3, 0), new(1, 0)])
    };

    static PlanarRegion RegionA(int id)
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] });
        region.Id = id;
        return region;
    }

    static PlanarRegion RegionB(int id)
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 4, 4, 0], Y = [-1, -1, 1, 1] },
            frame: new Frame3D(
                new(2, 0, 0),
                new(0, 1, 0),
                new(0, 0, 1),
                new(1, 0, 0)));
        region.Id = id;
        return region;
    }

    static PlanarMeshSnapshot SnapshotA(PlanarConnection connection, double[] y, string fingerprint) =>
        Snapshot(
            connection.SideA.RegionId,
            fingerprint,
            [
                new(0, 2, y[0], 2, y[0], 0),
                new(1, 2, y[1], 2, y[1], 0),
                new(2, 2, y[2], 2, y[2], 0)
            ],
            [new(0, 1), new(1, 2)],
            $"connection:{connection.Id}:region:{connection.SideA.RegionId}");

    static PlanarMeshSnapshot SnapshotB(PlanarConnection connection, double[] y, string fingerprint)
    {
        var nodes = y.Select((value, index) =>
            new PlanarMeshNode(index, value, 0, 2, value, 0)).ToArray();
        var edges = Enumerable.Range(0, nodes.Length - 1)
            .Select(index => new PlanarMeshEdge(index, index + 1))
            .ToArray();
        return Snapshot(
            connection.SideB.RegionId,
            fingerprint,
            nodes,
            edges,
            $"connection:{connection.Id}:region:{connection.SideB.RegionId}");
    }

    static PlanarMeshSnapshot Snapshot(
        int regionId,
        string fingerprint,
        IReadOnlyList<PlanarMeshNode> nodes,
        IReadOnlyList<PlanarMeshEdge> edges,
        string constraintId) => new()
    {
        Id = regionId + 100,
        RegionId = regionId,
        InputFingerprint = fingerprint,
        IsCalculable = true,
        Nodes = nodes,
        ConstraintMappings = [new() { ConstraintObjectId = constraintId, OrderedCurveEdges = edges }]
    };

    static PlanarMeshSnapshot Copy(PlanarMeshSnapshot snapshot, string fingerprint) => new()
    {
        Id = snapshot.Id,
        RegionId = snapshot.RegionId,
        InputFingerprint = fingerprint,
        IsCalculable = snapshot.IsCalculable,
        Nodes = snapshot.Nodes,
        Elements = snapshot.Elements,
        ConstraintMappings = snapshot.ConstraintMappings
    };
}
