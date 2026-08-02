using CScore;
using CScore.Fem;
using CScore.Planar;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;

namespace OpenCS.Gmsh.Tests;

public sealed class PlanarMeshPersistenceTests
{
    [Fact]
    public void SavePlanarMeshSnapshot_RoundTripsWithoutReplacingFemMesh()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opencs-gmsh-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "test" };
            db.SaveFemSchema(schema);
            var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 1, 1, 0], Y = [0, 0, 1, 1] });
            db.AddPlanarRegion(region, schema.Id);
            var settings = new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed);
            var provenance = new PlanarMeshProvenance("4.15.2", "geo-v1");
            var snapshot = new PlanarMeshSnapshot
            {
                RegionId = region.Id,
                InputFingerprint = PlanarMeshFingerprint.Compute(region, settings, provenance),
                IsCalculable = true,
                Settings = settings,
                Provenance = provenance,
                Nodes = [new(0, 0, 0, 0, 0, 0), new(1, 1, 0, 1, 0, 0), new(2, 0, 1, 0, 1, 0)],
                Elements = [new(0, PlanarMeshElementKind.Triangle3, [0, 1, 2])],
                BoundaryMappings = [new()
                {
                    Key = new(BoundaryLoop.Outer, 0, 0, 1),
                    NodeIndices = [0, 1]
                }]
            };

            db.SavePlanarMeshSnapshot(snapshot);
            var loaded = Assert.Single(db.GetPlanarMeshSnapshots(region.Id));

            Assert.True(loaded.IsCalculable);
            Assert.Single(loaded.Elements);
            Assert.Single(loaded.BoundaryMappings);
            Assert.Equal([0, 1], loaded.BoundaryMappings[0].NodeIndices);
            Assert.Equal(snapshot.InputFingerprint, loaded.InputFingerprint);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void AddPlanarRegion_RoundTripsMeshMaxElementSizeM()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opencs-gmsh-region-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "test" };
            db.SaveFemSchema(schema);
            var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 1, 1, 0], Y = [0, 0, 1, 1] });
            region.MeshMaxElementSizeM = 0.35;

            db.AddPlanarRegion(region, schema.Id);
            var loaded = Assert.Single(db.GetPlanarRegions(schema.Id));

            Assert.Equal(0.35, loaded.MeshMaxElementSizeM);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void AddPlanarRegion_RoundTripsConstraintObjectsAndFingerprint()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opencs-gmsh-constraint-region-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "test" };
            db.SaveFemSchema(schema);
            var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] });
            region.ConstraintObjects =
            [
                PlanarConstraintObject.Point("point-1", new(1, 1),
                    new PlanarStructuralFacet(PlanarStructuralKind.PointMpc, new PlanarMasterReference("test", "master")),
                    new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint))
            ];
            db.AddPlanarRegion(region, schema.Id);

            var loaded = Assert.Single(db.GetPlanarRegions(schema.Id));

            var constraint = Assert.Single(loaded.ConstraintObjects);
            Assert.Equal("point-1", constraint.Id);
            Assert.Equal(new PlanarPoint2D(1, 1), constraint.Geometry.Points[0]);
            Assert.Equal(PlanarMeshKind.EmbeddedPoint, constraint.MeshFacet.Kind);
            Assert.Equal("master", constraint.StructuralFacet.MasterReference?.Key);
            Assert.Equal(region.GeometryFingerprint, loaded.GeometryFingerprint);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SavePlanarMeshSnapshot_RoundTripsMsh41ProvenanceAndConstraintMappings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opencs-gmsh-constraint-snapshot-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "test" };
            db.SaveFemSchema(schema);
            var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 1, 1, 0], Y = [0, 0, 1, 1] });
            db.AddPlanarRegion(region, schema.Id);
            var settings = new PlanarMeshSettings(0.5, 6, PlanarMeshElementMode.Mixed);
            var provenance = new PlanarMeshProvenance("4.15.2", "gmsh-planar-v3-msh41");
            var snapshot = new PlanarMeshSnapshot
            {
                RegionId = region.Id,
                InputFingerprint = PlanarMeshFingerprint.Compute(region, settings, provenance),
                IsCalculable = true,
                Settings = settings,
                Provenance = provenance,
                MeshFormatVersion = "msh41",
                EntityProvenance = [new("point-1", 0, 9, 3001, "constraint:point-1:point")],
                Nodes = [new(0, 0, 0, 0, 0, 0), new(1, 1, 0, 1, 0, 0), new(2, 0, 1, 0, 1, 0)],
                Elements = [new(0, PlanarMeshElementKind.Triangle3, [0, 1, 2])],
                ConstraintMappings =
                [
                    new()
                     {
                         ConstraintObjectId = "point-1",
                         PointNodeIndices = [0],
                         EntityProvenance = [new("point-1", 0, 9, 3001, "constraint:point-1:point")],
                         SourceReferences = [new(10, "bar", [21], ["e21"], [1, 2], ["1", "2"])],
                         StructuralRelations = [new(10, "bar", [21], ["e21"],
                             new PlanarMasterReference("fem-member", "10"), PlanarStructuralKind.EmbeddedMember,
                             PlanarDofMask.UX | PlanarDofMask.UY | PlanarDofMask.UZ)]
                     }
                ]
            };

            db.SavePlanarMeshSnapshot(snapshot);
            var loaded = Assert.Single(db.GetPlanarMeshSnapshots(region.Id));

            Assert.Equal("msh41", loaded.MeshFormatVersion);
            Assert.Equal("point-1", Assert.Single(loaded.EntityProvenance).LogicalConstraintId);
            var mapping = Assert.Single(loaded.ConstraintMappings);
             Assert.Equal("point-1", mapping.ConstraintObjectId);
             Assert.Equal([0], mapping.PointNodeIndices);
             Assert.Equal([21], mapping.SourceReferences.Single().ElementIds);
             Assert.Equal(PlanarStructuralKind.EmbeddedMember, mapping.StructuralRelations.Single().Kind);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void DatabaseService_MigratesSchema47To49Idempotently()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opencs-gmsh-migration-{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new DatabaseService(path)) { }
            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                 command.CommandText = "UPDATE settings SET value_json='47' WHERE key='schema_version'";
                command.ExecuteNonQuery();
            }

            using (var migrated = new DatabaseService(path))
            {
                using var command = new SqliteConnection($"Data Source={path}");
                command.Open();
                using var schema = command.CreateCommand();
                schema.CommandText = "SELECT value_json FROM settings WHERE key='schema_version'";
                 Assert.Equal("49", schema.ExecuteScalar()?.ToString());
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
