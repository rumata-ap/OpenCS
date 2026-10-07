using CScore.Fem;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;

namespace OpenCS.OpenSees.Tests;

/// <summary>Хранение ГУ сеточного уровня (v78): кругооборот, замена своего происхождения, ГУ КЭ, копия, удаление, миграция.</summary>
public sealed class FemBoundaryPersistenceTests
{
    static readonly string Scad = FemLoadOrigin.Import("scad");

    static string TempPath() => Path.Combine(Path.GetTempPath(), $"opencs-boundary-{Guid.NewGuid():N}.db");

    static FemElement Element(string tag, string type, string nodes) =>
        new() { ElemTag = tag, ElemType = type, NodeIdsJson = nodes, Origin = FemMember.MeshSourceImported };

    static (DatabaseService Db, FemSchema Schema) Seed(string path)
    {
        var db = new DatabaseService(path);
        var schema = new FemSchema { Tag = "S", SourceType = "scad" };
        db.SaveFemSchema(schema);
        db.SaveFemMeshSnapshot(schema.Id,
            [new FemMeshNode { NodeTag = "1", Origin = FemMember.MeshSourceImported },
             new FemMeshNode { NodeTag = "2", X = 1, Origin = FemMember.MeshSourceImported },
             new FemMeshNode { NodeTag = "3", Y = 1, Origin = FemMember.MeshSourceImported }],
            [Element("10", "beam", "[1,2]"), Element("11", "shell", "[1,2,3]")]);
        return (db, schema);
    }

    static (List<FemMeshNodeSupport>, List<FemSpring>, List<FemRigidBody>) ScadBoundary(double kz)
    {
        var body = new FemRigidBody { MasterNodeTag = "1", Mask = FemBoundaryDofs.All, SourceElemTag = "500" };
        body.SetSlaveNodeTags(["2", "3"]);
        return ([new FemMeshNodeSupport { NodeTag = "1", Mask = 0b000011 }],
                [new FemSpring { NodeTag = "2", Kz = kz, SourceElemTag = "400" }],
                [body]);
    }

    [Fact]
    public void SaveFemBoundary_RoundTrips_AndReplacesOnlyOwnOrigin()
    {
        string path = TempPath();
        try
        {
            var (db, schema) = Seed(path);
            using (db)
            {
                db.SaveFemBoundary(schema.Id, FemLoadOrigin.Manual,
                    [new FemMeshNodeSupport { NodeTag = "3", Mask = FemBoundaryDofs.All }],
                    [new FemSpring { TargetKind = FemSpringTargetKinds.Node, NodeTag = "A", Kux = 7 }], []);
                var (supports, springs, bodies) = ScadBoundary(1e6);
                db.SaveFemBoundary(schema.Id, Scad, supports, springs, bodies,
                    new Dictionary<string, FemElementBoundaryProps> { ["10"] = new(0, 0b110000, null), ["11"] = new(null, null, 8.5e6) });

                // Повторное чтение источника: свои записи заменены, не размножены; ручные на месте.
                (supports, springs, bodies) = ScadBoundary(2e6);
                int updated = db.SaveFemBoundary(schema.Id, Scad, supports, springs, bodies,
                    new Dictionary<string, FemElementBoundaryProps> { ["10"] = new(0b010000, null, null), ["99"] = new(1, 1, null) });
                Assert.Equal(1, updated);
                Assert.True(supports[0].Id > 0);
                Assert.Equal(Scad, supports[0].Origin);

                var storedSupports = db.GetFemMeshNodeSupports(schema.Id);
                Assert.Equal([("3", FemBoundaryDofs.All, FemLoadOrigin.Manual), ("1", 0b000011, Scad)],
                    storedSupports.Select(s => (s.NodeTag, s.Mask, s.Origin)));
                var storedSprings = db.GetFemSprings(schema.Id);
                Assert.Equal(2, storedSprings.Count);
                Assert.Equal((FemSpringTargetKinds.Node, "A", 7.0), (storedSprings[0].TargetKind, storedSprings[0].NodeTag, storedSprings[0].Kux));
                Assert.Equal(("2", 2e6, "400", Scad), (storedSprings[1].NodeTag, storedSprings[1].Kz, storedSprings[1].SourceElemTag, storedSprings[1].Origin));
                var storedBody = Assert.Single(db.GetFemRigidBodies(schema.Id));
                Assert.Equal(("1", FemBoundaryDofs.All, "500"), (storedBody.MasterNodeTag, storedBody.Mask, storedBody.SourceElemTag));
                Assert.Equal(["2", "3"], storedBody.SlaveNodeTags);

                // ГУ КЭ: КЭ вне нового словаря потеряли свои (C1 у 11), 10 получил новые.
                var elements = db.GetFemMeshElements(schema.Id).ToDictionary(e => e.ElemTag);
                Assert.Equal((0b010000, (int?)null, (double?)null), (elements["10"].ReleaseI, elements["10"].ReleaseJ, elements["10"].FoundationC1));
                Assert.Null(elements["11"].FoundationC1);
            }
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void DuplicateAndDelete_CarryBoundary()
    {
        string path = TempPath();
        try
        {
            var (db, schema) = Seed(path);
            using (db)
            {
                var (supports, springs, bodies) = ScadBoundary(1e6);
                db.SaveFemBoundary(schema.Id, Scad, supports, springs, bodies,
                    new Dictionary<string, FemElementBoundaryProps> { ["10"] = new(0b100000, 0b100000, null), ["11"] = new(null, null, 1e7) });

                var copy = db.DuplicateFemSchema(schema.Id, "S2");
                Assert.Single(db.GetFemMeshNodeSupports(copy.Id));
                Assert.Equal(1e6, db.GetFemSprings(copy.Id).Single().Kz);
                Assert.Equal(["2", "3"], db.GetFemRigidBodies(copy.Id).Single().SlaveNodeTags);
                var elements = db.GetFemMeshElements(copy.Id).ToDictionary(e => e.ElemTag);
                Assert.Equal((0b100000, 0b100000), (elements["10"].ReleaseI, elements["10"].ReleaseJ));
                Assert.Equal(1e7, elements["11"].FoundationC1);

                db.DeleteFemSchema(copy);
                Assert.Empty(db.GetFemMeshNodeSupports(copy.Id));
                Assert.Empty(db.GetFemSprings(copy.Id));
                Assert.Empty(db.GetFemRigidBodies(copy.Id));
                Assert.Single(db.GetFemRigidBodies(schema.Id));
            }
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void SchemaVersion77Database_GetsV78TablesAndColumns()
    {
        string path = TempPath();
        try
        {
            using (var db = new DatabaseService(path)) { }
            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    DROP TABLE fem_mesh_node_supports;
                    DROP TABLE fem_springs;
                    DROP TABLE fem_rigid_bodies;
                    ALTER TABLE fem_elements DROP COLUMN release_i;
                    ALTER TABLE fem_elements DROP COLUMN release_j;
                    ALTER TABLE fem_elements DROP COLUMN foundation_c1;
                    UPDATE settings SET value_json='77' WHERE key='schema_version';
                """;
                command.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            var (db2, schema) = Seed(path);
            using (db2)
            {
                var (supports, springs, bodies) = ScadBoundary(1e6);
                db2.SaveFemBoundary(schema.Id, Scad, supports, springs, bodies,
                    new Dictionary<string, FemElementBoundaryProps> { ["11"] = new(null, null, 5e6) });
                Assert.Single(db2.GetFemSprings(schema.Id));
                Assert.Equal(5e6, db2.GetFemMeshElements(schema.Id).Single(e => e.ElemTag == "11").FoundationC1);
            }
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }
}
