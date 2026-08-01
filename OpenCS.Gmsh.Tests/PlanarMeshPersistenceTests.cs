using CScore;
using CScore.Fem;
using CScore.Planar;
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
}
