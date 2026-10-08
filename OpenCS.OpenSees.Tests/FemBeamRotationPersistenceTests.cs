using CScore.Fem;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;

namespace OpenCS.OpenSees.Tests;

/// <summary>Поворот сечения импортных стержней (v79): кругооборот, копия схемы, замена после повторного чтения, миграция.</summary>
public sealed class FemBeamRotationPersistenceTests
{
    static string TempPath() => Path.Combine(Path.GetTempPath(), $"opencs-rotation-{Guid.NewGuid():N}.db");

    static FemElement Element(string tag, string type, string nodes, double? rotation = null) => new()
    {
        ElemTag = tag, ElemType = type, NodeIdsJson = nodes, Origin = FemMember.MeshSourceImported, BeamRotationDeg = rotation,
    };

    static (DatabaseService Db, FemSchema Schema) Seed(string path)
    {
        var db = new DatabaseService(path);
        var schema = new FemSchema { Tag = "S", SourceType = "scad" };
        db.SaveFemSchema(schema);
        db.SaveFemMeshSnapshot(schema.Id,
            [new FemMeshNode { NodeTag = "1", Origin = FemMember.MeshSourceImported },
             new FemMeshNode { NodeTag = "2", X = 1, Origin = FemMember.MeshSourceImported },
             new FemMeshNode { NodeTag = "3", Y = 1, Origin = FemMember.MeshSourceImported }],
            [Element("10", "beam", "[1,2]", -90), Element("12", "beam", "[1,3]"), Element("11", "shell", "[1,2,3]")]);
        return (db, schema);
    }

    static Dictionary<string, double?> Rotations(DatabaseService db, int schemaId) =>
        db.GetFemMeshElements(schemaId).ToDictionary(e => e.ElemTag, e => e.BeamRotationDeg);

    [Fact]
    public void BeamRotation_RoundTrips_CopiesAndIsReplaced()
    {
        string path = TempPath();
        try
        {
            var (db, schema) = Seed(path);
            using (db)
            {
                Assert.Equal(new Dictionary<string, double?> { ["10"] = -90, ["12"] = null, ["11"] = null }, Rotations(db, schema.Id));

                var copy = db.DuplicateFemSchema(schema.Id, "S (копия)");
                Assert.Equal(-90, Rotations(db, copy.Id)["10"]);

                // Повторное чтение осей: заданные — свои углы, прочие импортные стержни — «не прочитаны», пластины не трогаются.
                int updated = db.ReplaceFemElementBeamRotations(schema.Id,
                    new Dictionary<string, double> { ["12"] = 30, ["11"] = 5, ["99"] = 1 });
                Assert.Equal(1, updated);
                Assert.Equal(new Dictionary<string, double?> { ["10"] = null, ["12"] = 30, ["11"] = null }, Rotations(db, schema.Id));
                Assert.Equal(-90, Rotations(db, copy.Id)["10"]);
            }
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void SchemaVersion78Database_GetsBeamRotationColumn()
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
                    ALTER TABLE fem_elements DROP COLUMN beam_rotation_deg;
                    UPDATE settings SET value_json='78' WHERE key='schema_version';
                """;
                command.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            var (db2, schema) = Seed(path);
            using (db2)
            {
                Assert.Equal(79, DatabaseService.SchemaVersion);
                Assert.Equal(-90, Rotations(db2, schema.Id)["10"]);
            }
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }
}
