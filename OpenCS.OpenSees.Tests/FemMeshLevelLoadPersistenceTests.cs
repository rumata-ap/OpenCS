using CScore.Fem;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;

namespace OpenCS.OpenSees.Tests;

/// <summary>Хранение нагрузок сеточного уровня (v77): кругооборот, перезапись схемы, удаления, копия, миграция.</summary>
public sealed class FemMeshLevelLoadPersistenceTests
{
    static string TempPath() => Path.Combine(Path.GetTempPath(), $"opencs-mesh-loads-{Guid.NewGuid():N}.db");

    static FemElementLoad Pressure(int loadCaseId, string origin = FemLoadOrigin.Manual)
    {
        var load = new FemElementLoad { LoadCaseId = loadCaseId, Origin = origin, LoadKind = FemElementLoadKinds.Nodal,
            CoordinateSystem = "local", Axis = "z" };
        load.SetTargetTags(["10", "11"]);
        load.SetValues([1.5, 2.5, 3.5, 4.5]);
        return load;
    }

    static (DatabaseService Db, FemSchema Schema, FemLoadCase Case) Seed(string path)
    {
        var db = new DatabaseService(path);
        var schema = new FemSchema { Tag = "S", SourceType = "scad" };
        db.SaveFemSchema(schema);
        var lc = new FemLoadCase { Id = -1, Tag = "L1", SelfWeightFactor = 1.1, Origin = FemLoadOrigin.Import("scad"), SourceLoadNum = 3 };
        var group = new FemMemberGroup { Tag = "G", Kind = FemMemberGroup.KindMesh };
        group.SetTags(["10"]);
        db.SaveFemSchemaEdit(schema.Id, [], [], [group], [lc], [], [], [], null,
            [Pressure(-1)], [new FemMeshNodeLoad { LoadCaseId = -1, MeshNodeTag = "5", Fz = -100, Origin = "import:scad" }]);
        return (db, schema, lc);
    }

    [Fact]
    public void SaveFemSchemaEdit_RoundTripsLoadCaseExtrasAndMeshLevelLoads()
    {
        string path = TempPath();
        try
        {
            var (db, schema, _) = Seed(path);
            using (db)
            {
                db.LoadAll();
                var lc = db.GetFemLoadCases(schema.Id).Single();
                Assert.Equal(1.1, lc.SelfWeightFactor);
                Assert.Equal("import:scad", lc.Origin);
                Assert.Equal(3, lc.SourceLoadNum);
                Assert.Equal(1.1, db.FemSchemas.Single(s => s.Id == schema.Id).LoadCases.Single().SelfWeightFactor);

                var load = db.GetFemElementLoads(schema.Id).Single();
                Assert.Equal(lc.Id, load.LoadCaseId);
                Assert.Equal(["10", "11"], load.TargetTags);
                Assert.Equal([1.5, 2.5, 3.5, 4.5], load.Values);
                Assert.Equal(("nodal", "local", "z"), (load.LoadKind, load.CoordinateSystem, load.Axis));
                var nodeLoad = db.GetFemMeshNodeLoads(schema.Id).Single();
                Assert.Equal(("5", -100.0, "import:scad", lc.Id), (nodeLoad.MeshNodeTag, nodeLoad.Fz, nodeLoad.Origin, nodeLoad.LoadCaseId));
            }
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void SaveFemSchemaEdit_WithoutMeshLevelLoads_KeepsThemOnRenumberedLoadCases()
    {
        string path = TempPath();
        try
        {
            var (db, schema, _) = Seed(path);
            using (db)
            {
                var cases = db.GetFemLoadCases(schema.Id);
                var extra = new FemLoadCase { Tag = "L2" };
                // Старый вызов без нагрузок сеточного уровня: Id загружений меняются, нагрузки следуют за ними.
                db.SaveFemSchemaEdit(schema.Id, [], [], [], [extra, cases[0]], []);
                var l1 = db.GetFemLoadCases(schema.Id).Single(c => c.Tag == "L1");
                Assert.Equal(l1.Id, db.GetFemElementLoads(schema.Id).Single().LoadCaseId);
                Assert.Equal(l1.Id, db.GetFemMeshNodeLoads(schema.Id).Single().LoadCaseId);

                // Загружение удалено — его нагрузки тоже.
                db.SaveFemSchemaEdit(schema.Id, [], [], [], [db.GetFemLoadCases(schema.Id).Single(c => c.Tag == "L2")], []);
                Assert.Empty(db.GetFemElementLoads(schema.Id));
                Assert.Empty(db.GetFemMeshNodeLoads(schema.Id));
            }
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void SaveFemSchemaEdit_RejectsOnlyOneOfMeshLevelLoadLists()
    {
        string path = TempPath();
        try
        {
            var (db, schema, lc) = Seed(path);
            using (db)
                Assert.Throws<ArgumentException>(() =>
                    db.SaveFemSchemaEdit(schema.Id, [], [], [], [lc], [], [], [], null, [], null));
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void DeleteLoadCaseAndSchema_RemoveMeshLevelLoads()
    {
        string path = TempPath();
        try
        {
            var (db, schema, _) = Seed(path);
            using (db)
            {
                db.DeleteFemLoadCase(db.GetFemLoadCases(schema.Id).Single());
                Assert.Empty(db.GetFemElementLoads(schema.Id));
                Assert.Empty(db.GetFemMeshNodeLoads(schema.Id));
            }
            var (db2, schema2, _) = Seed(path);
            using (db2)
            {
                db2.LoadAll();
                db2.DeleteFemSchema(db2.FemSchemas.Single(s => s.Id == schema2.Id));
                Assert.Empty(db2.GetFemElementLoads(schema2.Id));
                Assert.Empty(db2.GetFemMeshNodeLoads(schema2.Id));
            }
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void DuplicateFemSchema_CopiesMeshLevelLoadsWithMappedCaseAndGroup()
    {
        string path = TempPath();
        try
        {
            var (db, schema, _) = Seed(path);
            using (db)
            {
                db.LoadAll();
                var source = db.FemSchemas.Single(s => s.Id == schema.Id);
                var load = db.GetFemElementLoads(schema.Id).Single();
                var byGroup = new FemElementLoad { LoadCaseId = load.LoadCaseId, TargetKind = FemLoadTargetKinds.Group,
                    GroupId = source.MemberGroups.Single().Id };
                byGroup.SetValues([-1000]);
                db.SaveFemSchemaEdit(schema.Id, [], [], source.MemberGroups.ToList(), db.GetFemLoadCases(schema.Id), [], [], [],
                    null, [load, byGroup], db.GetFemMeshNodeLoads(schema.Id));

                var copy = db.DuplicateFemSchema(schema.Id, "S (копия)");
                var copyCase = copy.LoadCases.Single();
                Assert.Equal((1.1, 3), (copyCase.SelfWeightFactor, copyCase.SourceLoadNum));
                var loads = db.GetFemElementLoads(copy.Id);
                Assert.Equal(2, loads.Count);
                Assert.All(loads, l => Assert.Equal(copyCase.Id, l.LoadCaseId));
                Assert.Equal(copy.MemberGroups.Single().Id, loads.Single(l => l.TargetKind == FemLoadTargetKinds.Group).GroupId);
                Assert.Equal(copyCase.Id, db.GetFemMeshNodeLoads(copy.Id).Single().LoadCaseId);
                Assert.Equal(2, db.GetFemElementLoads(schema.Id).Count);
            }
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void SchemaVersion76Database_GetsV77TablesAndColumns()
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
                    DROP TABLE fem_element_loads;
                    DROP TABLE fem_mesh_node_loads;
                    ALTER TABLE fem_load_cases DROP COLUMN self_weight_factor;
                    ALTER TABLE fem_load_cases DROP COLUMN origin;
                    ALTER TABLE fem_load_cases DROP COLUMN source_load_num;
                    UPDATE settings SET value_json='76' WHERE key='schema_version';
                """;
                command.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            var (db2, schema, _) = Seed(path);
            using (db2)
            {
                Assert.Equal(1.1, db2.GetFemLoadCases(schema.Id).Single().SelfWeightFactor);
                Assert.Single(db2.GetFemElementLoads(schema.Id));
            }
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }
}
