using CScore.Fem;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;

namespace OpenCS.OpenSees.Tests;

/// <summary>Параметры авто-АЖТ (v90): флажок и порог угла схемы, режим/маска/вид/след стержня — кругооборот,
/// полная замена редактором, копия схемы, миграция с v89.</summary>
public sealed class FemRigidJunctionPersistenceTests
{
    const string Trace = "[[0,0,0],[1,0,0],[0,1,0]]";

    static string TempPath() => Path.Combine(Path.GetTempPath(), $"opencs-rigid-junction-{Guid.NewGuid():N}.db");

    static FemMember Member(int schemaId) => new()
    {
        SchemaId = schemaId, ElemTag = "1", ElemType = "beam", NodeIdsJson = "[1,2]",
        RigidJunctionMode = FemMember.RigidJunctionOff, RigidJunctionMask = 7,
        RigidJunctionKind = FemMember.RigidJunctionCoupled, RigidJunctionTraceJson = Trace,
    };

    static void AssertMember(FemMember m)
    {
        Assert.Equal(FemMember.RigidJunctionOff, m.RigidJunctionMode);
        Assert.Equal(7, m.RigidJunctionMask);
        Assert.Equal(FemMember.RigidJunctionCoupled, m.RigidJunctionKind);
        Assert.Equal(Trace, m.RigidJunctionTraceJson);
    }

    [Fact]
    public void SchemaAndMemberParams_RoundTrip()
    {
        string path = TempPath();
        try
        {
            int schemaId;
            using (var db = new DatabaseService(path))
            {
                var schema = new FemSchema { Tag = "S", SourceType = "internal" };
                db.SaveFemSchema(schema);
                schemaId = schema.Id;
                Assert.Equal((true, 10.0), db.GetFemSchemaRigidJunctions(schemaId));

                schema.AutoRigidJunctions = false;
                schema.RigidJunctionAngleDeg = 15;
                db.UpdateFemSchemaRigidJunctions(schema);
                Assert.Equal((false, 15.0), db.GetFemSchemaRigidJunctions(schemaId));

                var member = Member(schemaId);
                db.SaveFemMember(member);
                AssertMember(Assert.Single(db.GetFemMembers(schemaId)));

                // UPDATE-ветка: сброс к значениям по умолчанию.
                var reloaded = db.GetFemMembers(schemaId).Single();
                reloaded.RigidJunctionMode = null; reloaded.RigidJunctionMask = null;
                reloaded.RigidJunctionKind = null; reloaded.RigidJunctionTraceJson = null;
                db.SaveFemMember(reloaded);
                var cleared = Assert.Single(db.GetFemMembers(schemaId));
                Assert.Null(cleared.RigidJunctionMode);
                Assert.Null(cleared.RigidJunctionMask);
                Assert.Null(cleared.RigidJunctionKind);
                Assert.Null(cleared.RigidJunctionTraceJson);
            }
            SqliteConnection.ClearAllPools();

            using (var db2 = new DatabaseService(path))
            {
                db2.LoadAll();
                var schema = db2.FemSchemas.Single(s => s.Id == schemaId);
                Assert.False(schema.AutoRigidJunctions);
                Assert.Equal(15, schema.RigidJunctionAngleDeg);
            }
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void SaveFemSchemaEdit_KeepsMemberParams()
    {
        string path = TempPath();
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "S", SourceType = "internal" };
            db.SaveFemSchema(schema);
            var member = Member(0);
            member.SchemaId = 0;

            db.SaveFemSchemaEdit(schema.Id, [], [member], [], [], []);

            AssertMember(Assert.Single(db.GetFemMembers(schema.Id)));
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void DuplicateFemSchema_CopiesSchemaAndMemberParams()
    {
        string path = TempPath();
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "S", SourceType = "internal", AutoRigidJunctions = false, RigidJunctionAngleDeg = 25 };
            db.SaveFemSchema(schema);
            db.SaveFemMember(Member(schema.Id));

            var copy = db.DuplicateFemSchema(schema.Id, "S (копия)");

            Assert.False(copy.AutoRigidJunctions);
            Assert.Equal(25, copy.RigidJunctionAngleDeg);
            Assert.Equal((false, 25.0), db.GetFemSchemaRigidJunctions(copy.Id));
            AssertMember(Assert.Single(db.GetFemMembers(copy.Id)));
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void SchemaVersion89Database_GetsRigidJunctionColumnsWithDefaults()
    {
        string path = TempPath();
        try
        {
            int schemaId;
            using (var db = new DatabaseService(path))
            {
                var schema = new FemSchema { Tag = "S", SourceType = "internal" };
                db.SaveFemSchema(schema);
                schemaId = schema.Id;
            }
            SqliteConnection.ClearAllPools();
            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    ALTER TABLE fem_schemas DROP COLUMN auto_rigid_junctions;
                    ALTER TABLE fem_schemas DROP COLUMN rigid_junction_angle_deg;
                    ALTER TABLE fem_members DROP COLUMN rigid_junction_mode;
                    ALTER TABLE fem_members DROP COLUMN rigid_junction_mask;
                    ALTER TABLE fem_members DROP COLUMN rigid_junction_kind;
                    ALTER TABLE fem_members DROP COLUMN rigid_junction_trace_json;
                    INSERT INTO fem_members (schema_id, elem_tag, elem_type, node_ids_json) VALUES ($sid, '1', 'beam', '[1,2]');
                    UPDATE settings SET value_json='89' WHERE key='schema_version';
                """;
                command.Parameters.AddWithValue("$sid", schemaId);
                command.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            using var db2 = new DatabaseService(path);
            Assert.True(DatabaseService.SchemaVersion >= 90);
            Assert.Equal((true, 10.0), db2.GetFemSchemaRigidJunctions(schemaId));
            db2.LoadAll();
            var schema2 = db2.FemSchemas.Single(s => s.Id == schemaId);
            Assert.True(schema2.AutoRigidJunctions);
            Assert.Equal(10, schema2.RigidJunctionAngleDeg);
            var m = Assert.Single(db2.GetFemMembers(schemaId));
            Assert.Null(m.RigidJunctionMode);
            Assert.Null(m.RigidJunctionMask);
            Assert.Null(m.RigidJunctionKind);
            Assert.Null(m.RigidJunctionTraceJson);
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }
}
