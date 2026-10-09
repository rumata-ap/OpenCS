using CScore;
using CScore.Fem;
using CScore.Planar;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Общие шаги сетки схемы (v80): хранение, копия схемы, миграция шага области в свой шаг пластины.</summary>
public sealed class FemMeshStepPersistenceTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), "opencs_mesh_step_" + Guid.NewGuid().ToString("N") + ".db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }

    (int SchemaId, int MemberId) Create(DatabaseService db)
    {
        var schema = new FemSchema { Tag = "S" };
        db.SaveFemSchema(schema);
        var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] }, tag: "P1");
        region.MeshMaxElementSizeM = 0.35;
        db.AddPlanarRegion(region, schema.Id);
        var plate = new FemMember { SchemaId = schema.Id, ElemTag = "P1", ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = region.Id };
        db.SaveFemMember(plate);
        return (schema.Id, plate.Id);
    }

    [Fact]
    public void Steps_SavedReloadedAndCopied()
    {
        int schemaId;
        using (var db = new DatabaseService(_path))
        {
            schemaId = Create(db).SchemaId;
            var schema = db.FemSchemas.Single(s => s.Id == schemaId);
            schema.MeshBarStepM = 0.5;
            schema.MeshPlateStepM = 0.25;
            db.UpdateFemSchemaMeshSteps(schema);
        }
        using (var db = new DatabaseService(_path))
        {
            db.LoadAll();
            var schema = db.FemSchemas.Single(s => s.Id == schemaId);
            Assert.Equal(0.5, schema.MeshBarStepM);
            Assert.Equal(0.25, schema.MeshPlateStepM);
            Assert.Equal((0.5, 0.25), db.GetFemSchemaMeshSteps(schemaId));
            var copy = db.DuplicateFemSchema(schemaId, "S-копия");
            Assert.Equal((0.5, 0.25), db.GetFemSchemaMeshSteps(copy.Id));
        }
    }

    [Fact]
    public void MigrationV80_RegionSizeBecomesLocalPlateStep()
    {
        int schemaId, memberId;
        using (var db = new DatabaseService(_path))
            (schemaId, memberId) = Create(db);
        using (var connection = new SqliteConnection($"Data Source={_path};Pooling=False"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "ALTER TABLE fem_schemas DROP COLUMN mesh_bar_step_m; ALTER TABLE fem_schemas DROP COLUMN mesh_plate_step_m; " +
                              "UPDATE fem_members SET target_mesh_length_m = NULL; " +
                              "UPDATE settings SET value_json = '79' WHERE key = 'schema_version'";
            cmd.ExecuteNonQuery();
        }

        using (var db = new DatabaseService(_path))
        {
            Assert.Equal(0.35, db.GetFemMembers(schemaId).Single(m => m.Id == memberId).TargetMeshLengthM);
            Assert.Equal((null, null), db.GetFemSchemaMeshSteps(schemaId));
        }
    }
}
