using CScore;
using CScore.Fem;
using CScore.Planar;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Gmsh.Tests;

public sealed class PlanarConnectionPersistenceTests
{
    [Fact]
    public void SavePlanarConnection_RoundTripsSourceContract()
    {
        var path = TempPath("connection");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "test" };
            db.SaveFemSchema(schema);
            var regionA = RegionA(10);
            var regionB = RegionB(20);
            db.AddPlanarRegion(regionA, schema.Id);
            db.AddPlanarRegion(regionB, schema.Id);
            var connection = Connection(0, regionA.Id, regionB.Id);

            db.AddPlanarConnection(connection);

            var loaded = Assert.Single(db.GetPlanarConnections(schema.Id));
            Assert.True(loaded.Id > 0);
            Assert.Equal(connection.Tag, loaded.Tag);
            Assert.Equal(connection.SideA.RegionId, loaded.SideA.RegionId);
            Assert.Equal(connection.SideA.Points.ToArray(), loaded.SideA.Points.ToArray());
            Assert.Equal(connection.SideA.Tag, loaded.SideA.Tag);
            Assert.Equal(connection.SideB.RegionId, loaded.SideB.RegionId);
            Assert.Equal(connection.SideB.Points.ToArray(), loaded.SideB.Points.ToArray());
            Assert.Equal(connection.SideB.Tag, loaded.SideB.Tag);
            Assert.Equal(connection.MeshMode, loaded.MeshMode);
            Assert.Equal(connection.MatchingToleranceM, loaded.MatchingToleranceM);
            Assert.Equal(PlanarConnectionFingerprint.Compute(loaded), loaded.Fingerprint);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public void SavePlanarConnectionMeshMapping_RoundTripsChainsAndFingerprints()
    {
        var path = TempPath("mapping");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "test" };
            db.SaveFemSchema(schema);
            var regionA = RegionA(10);
            var regionB = RegionB(20);
            db.AddPlanarRegion(regionA, schema.Id);
            db.AddPlanarRegion(regionB, schema.Id);
            var connection = Connection(0, regionA.Id, regionB.Id);
            db.AddPlanarConnection(connection);

            var snapshotA = Snapshot(regionA.Id, "snapshot-a");
            var snapshotB = Snapshot(regionB.Id, "snapshot-b");
            db.SavePlanarMeshSnapshot(snapshotA);
            db.SavePlanarMeshSnapshot(snapshotB);
            var mapping = new PlanarConnectionMeshMapping
            {
                ConnectionId = connection.Id,
                ConnectionFingerprint = PlanarConnectionFingerprint.Compute(connection),
                MeshMode = connection.MeshMode,
                SideASnapshotId = snapshotA.Id,
                SideAFingerprint = snapshotA.InputFingerprint,
                SideBSnapshotId = snapshotB.Id,
                SideBFingerprint = snapshotB.InputFingerprint,
                SideA = SideMapping(regionA.Id, $"connection:{connection.Id}:region:{regionA.Id}", PlanarConnectionOrientation.Forward, 0, 1),
                SideB = SideMapping(regionB.Id, $"connection:{connection.Id}:region:{regionB.Id}", PlanarConnectionOrientation.Reverse, 1, 0),
                ExactNodePairs = [new(0, 1, 0)]
            };

            db.SavePlanarConnectionMeshMapping(mapping);

            var loaded = Assert.Single(db.GetPlanarConnectionMeshMappings(connection.Id));
            Assert.Equal(mapping.ConnectionFingerprint, loaded.ConnectionFingerprint);
            Assert.Equal(mapping.SideASnapshotId, loaded.SideASnapshotId);
            Assert.Equal(PlanarConnectionOrientation.Reverse, loaded.SideB.Orientation);
            Assert.Equal([new PlanarConnectionNodePair(0, 1, 0)], loaded.ExactNodePairs);
            Assert.Equal(1, loaded.SideB.Nodes[0].S);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public void DatabaseService_MigratesSchema48To49AndCreatesConnectionTables()
    {
        var path = TempPath("migration");
        try
        {
            using (var db = new DatabaseService(path)) { }
            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE settings SET value_json='48' WHERE key='schema_version'";
                command.ExecuteNonQuery();
            }

            using (var migrated = new DatabaseService(path))
            {
            }
            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var version = connection.CreateCommand();
                version.CommandText = "SELECT value_json FROM settings WHERE key='schema_version'";
                Assert.Equal("49", version.ExecuteScalar()?.ToString());
                using var tables = connection.CreateCommand();
                tables.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('planar_connections','planar_connection_mappings')";
                Assert.Equal(2L, (long)tables.ExecuteScalar()!);
            }
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public void DeletePlanarRegion_RemovesConnectionsAndMappingsWithForeignKeysDisabled()
    {
        var path = TempPath("delete");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "test" };
            db.SaveFemSchema(schema);
            var regionA = RegionA(10);
            var regionB = RegionB(20);
            db.AddPlanarRegion(regionA, schema.Id);
            db.AddPlanarRegion(regionB, schema.Id);
            var connection = Connection(0, regionA.Id, regionB.Id);
            db.AddPlanarConnection(connection);
            var snapshotA = Snapshot(regionA.Id, "snapshot-a");
            var snapshotB = Snapshot(regionB.Id, "snapshot-b");
            db.SavePlanarMeshSnapshot(snapshotA);
            db.SavePlanarMeshSnapshot(snapshotB);
            db.SavePlanarConnectionMeshMapping(new PlanarConnectionMeshMapping
            {
                ConnectionId = connection.Id,
                ConnectionFingerprint = PlanarConnectionFingerprint.Compute(connection),
                MeshMode = connection.MeshMode,
                SideASnapshotId = snapshotA.Id,
                SideAFingerprint = snapshotA.InputFingerprint,
                SideBSnapshotId = snapshotB.Id,
                SideBFingerprint = snapshotB.InputFingerprint
            });

            db.DeletePlanarRegion(regionA.Id);

            Assert.Empty(db.GetPlanarConnections(schema.Id));
            using var connectionCheck = new SqliteConnection($"Data Source={path}");
            connectionCheck.Open();
            using var command = connectionCheck.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM planar_connection_mappings";
            Assert.Equal(0L, (long)command.ExecuteScalar()!);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    static string TempPath(string suffix) => Path.Combine(Path.GetTempPath(), $"opencs-gmsh-connection-{suffix}-{Guid.NewGuid():N}.db");

    static void DeleteDatabase(string path)
    {
        // SQLite may release a pooled handle just after Dispose; wait for the actual delete condition.
        SqliteConnection.ClearAllPools();
        if (!SpinWait.SpinUntil(() =>
            {
                if (!File.Exists(path)) return true;
                try
                {
                    File.Delete(path);
                    return !File.Exists(path);
                }
                catch (IOException)
                {
                    return false;
                }
            }, TimeSpan.FromSeconds(5)) && File.Exists(path))
            throw new IOException($"Не удалось удалить временную SQLite БД '{path}'.");
    }

    static PlanarConnection Connection(int id, int regionAId, int regionBId) => new()
    {
        Id = id,
        Tag = "plate-wall",
        MeshMode = PlanarConnectionMeshMode.EmbeddedLocus,
        SideA = new ConnectionLocus(regionAId, [new(2, 1), new(2, 3)], "plate-locus"),
        SideB = new ConnectionLocus(regionBId, [new(3, 0), new(1, 0)], "wall-locus"),
        MatchingToleranceM = 1e-7
    };

    static PlanarRegion RegionA(int id)
    {
        var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] });
        region.Id = id;
        return region;
    }

    static PlanarRegion RegionB(int id)
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 4, 4, 0], Y = [-1, -1, 1, 1] },
            frame: new Frame3D(new(2, 0, 0), new(0, 1, 0), new(0, 0, 1), new(1, 0, 0)));
        region.Id = id;
        return region;
    }

    static PlanarMeshSnapshot Snapshot(int regionId, string fingerprint) => new()
    {
        RegionId = regionId,
        InputFingerprint = fingerprint,
        IsCalculable = true,
        Nodes = [new(0, 0, 0, 2, 1, 0), new(1, 0, 0, 2, 3, 0)],
        ConstraintMappings = [new() { ConstraintObjectId = $"connection:7:region:{regionId}", OrderedCurveEdges = [new(0, 1)] }]
    };

    static PlanarConnectionSideMapping SideMapping(
        int regionId,
        string constraintId,
        PlanarConnectionOrientation orientation,
        int firstNode,
        int secondNode) => new()
    {
        RegionId = regionId,
        ConstraintObjectId = constraintId,
        Orientation = orientation,
        OrderedNodeIndices = [firstNode, secondNode],
        OrderedEdges = [new(firstNode, secondNode)],
        Nodes = [new(firstNode, new(2, 1, 0), firstNode), new(secondNode, new(2, 3, 0), secondNode)]
    };
}
