using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Planar;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Фрагмент этажа в БД (v92–v93): миграция, поля схемы-фрагмента.</summary>
public sealed class StoreyFragmentDbTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs-fragment-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }

    static readonly string[] FragmentTables =
        ["fem_fragment_cuts", "fem_fragment_ref_elements", "fem_fragment_field_loads", "fem_fragment_rebar_sources"];

    static readonly string[] FragmentColumns =
        ["parent_schema_id", "fragment_z1", "fragment_z2", "parent_fingerprint", "fragment_build_report_json", "parent_source_type"];

    [Fact]
    public void NewDatabase_HasV93TablesAndColumns()
    {
        using (new DatabaseService(_path)) { }
        Assert.True(DatabaseService.SchemaVersion >= 93);
        Assert.Equal(DatabaseService.SchemaVersion.ToString(), Scalar("SELECT value_json FROM settings WHERE key='schema_version'"));
        foreach (var t in FragmentTables)
            Assert.Equal(1L, Scalar($"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{t}'"));
        foreach (var c in FragmentColumns)
            Assert.Equal(1L, Scalar($"SELECT COUNT(*) FROM pragma_table_info('fem_schemas') WHERE name='{c}'"));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='idx_fem_mesh_nodes_schema_z'"));
    }

    [Fact]
    public void V91Database_MigratesToV92()
    {
        using (new DatabaseService(_path)) { }
        Exec(string.Join(";", FragmentTables.Select(t => $"DROP TABLE {t}"))
             + ";UPDATE settings SET value_json='91' WHERE key='schema_version'");
        using (new DatabaseService(_path)) { }
        Assert.Equal(DatabaseService.SchemaVersion.ToString(), Scalar("SELECT value_json FROM settings WHERE key='schema_version'"));
        foreach (var t in FragmentTables)
            Assert.Equal(1L, Scalar($"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{t}'"));
        using (new DatabaseService(_path)) { }
    }

    [Fact]
    public void V92Database_MigratesToV93_KeepsRefElements()
    {
        using (new DatabaseService(_path)) { }
        Exec("""
            DROP TABLE fem_fragment_ref_elements;
            CREATE TABLE fem_fragment_ref_elements (id INTEGER PRIMARY KEY AUTOINCREMENT, schema_id INTEGER NOT NULL,
                tag TEXT NOT NULL, elem_type TEXT NOT NULL, points_blob BLOB NOT NULL, stiffness_num INTEGER,
                foundation_c1 REAL, source_member_tag TEXT);
            INSERT INTO fem_schemas (tag, source_type, created) VALUES ('Ф', 'internal', '');
            ALTER TABLE fem_schemas DROP COLUMN parent_source_type;
            UPDATE settings SET value_json='92' WHERE key='schema_version';
            """);
        Exec("INSERT INTO fem_fragment_ref_elements (schema_id, tag, elem_type, points_blob) VALUES (1, '501', 'beam', " +
             $"X'{Convert.ToHexString(FragmentStoreCodec.EncodePoints([new(0, 0, 0), new(0, 0, 3)]))}')");
        using (var db = new DatabaseService(_path))
        {
            Assert.Equal(DatabaseService.SchemaVersion.ToString(), Scalar("SELECT value_json FROM settings WHERE key='schema_version'"));
            var r = db.GetFragmentRefElements(1).Single();
            Assert.Equal("501", r.Tag);
            Assert.Null(r.ReinforcementTypeIds);
            Assert.Null(r.ThicknessM);
        }
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM pragma_table_info('fem_schemas') WHERE name='parent_source_type'"));
    }

    [Fact]
    public void FragmentFields_RoundTrip()
    {
        int id;
        using (var db = new DatabaseService(_path))
        {
            var plain = new FemSchema { Tag = "Родитель" };
            db.SaveFemSchema(plain);
            var schema = new FemSchema
            {
                Tag = "Этаж 2", ParentSchemaId = plain.Id, FragmentZ1 = 1.5, FragmentZ2 = 4.5,
                ParentFingerprint = "ABC", FragmentBuildReportJson = "[]", ParentSourceType = "scad",
            };
            db.SaveFemSchema(schema);
            id = schema.Id;
        }
        using (var db = new DatabaseService(_path))
        {
            db.LoadAll();
            var s = db.FemSchemas.Single(x => x.Id == id);
            Assert.True(s.IsFragment);
            Assert.Equal(1.5, s.FragmentZ1);
            Assert.Equal(4.5, s.FragmentZ2);
            Assert.Equal("ABC", s.ParentFingerprint);
            Assert.Equal("[]", s.FragmentBuildReportJson);
            Assert.Equal("scad", s.ParentSourceType);
            Assert.False(db.FemSchemas.Single(x => x.Tag == "Родитель").IsFragment);
        }
    }

    [Fact]
    public void FragmentData_RoundTrip()
    {
        using var db = new DatabaseService(_path);
        var schema = new FemSchema { Tag = "Фрагмент" };
        db.SaveFemSchema(schema);
        FragmentCut[] cuts =
        [
            new("C1 · низ", FragmentCutKind.End, FragmentCutSide.Bottom, 1.5, "17", []),
            new("W1", FragmentCutKind.Edge, FragmentCutSide.Top, 4.5, null,
                [(new PlanarVector3(0, 0, 4.5), new PlanarVector3(1, 0, 4.5)), (new PlanarVector3(2, 0, 4.5), new PlanarVector3(3, 0, 4.5))]),
        ];
        FragmentRefElement[] refs =
        [
            new("501", "beam", [new(0, 0, 1.5), new(0, 0, 3)], stiffnessNum: 3, sourceMemberTag: "C1"),
            new("1", "shell", [new(-3, -3, 3), new(-1.5, -3, 3), new(-3, -1.5, 3), new(-1.5, -1.5, 3)], foundationC1: 2e7,
                reinforcementTypeIds: "1 2 4", thicknessM: 0.2),
        ];
        FragmentFieldLoad[] fields =
        [
            new(7, FemElementLoadKinds.Nodal, "z", [new("1", [1, 2, 3, 4])]),
            new(8, FemElementLoadKinds.Edge, "x", [new("1", [1, 2, -5, -6]), new("2", [2, 4, 1, 1])]),
        ];
        db.ReplaceStoreyFragmentData(schema.Id, cuts, refs, fields);

        var c = db.GetFragmentCuts(schema.Id);
        Assert.Equal(2, c.Count);
        Assert.Equal(("C1 · низ", FragmentCutKind.End, FragmentCutSide.Bottom, 1.5, "17"), (c[0].MemberTag, c[0].Kind, c[0].Side, c[0].Z, c[0].NodeTag));
        Assert.Empty(c[0].Segments);
        Assert.Equal(cuts[1].Segments, c[1].Segments);
        Assert.Null(c[1].NodeTag);

        var r = db.GetFragmentRefElements(schema.Id);
        Assert.Equal(["501", "1"], r.Select(e => e.Tag));
        Assert.True(r[0].IsBar);
        Assert.Equal(3, r[0].StiffnessNum);
        Assert.Equal("C1", r[0].SourceMemberTag);
        Assert.Equal(refs[1].Points, r[1].Points);
        Assert.Equal(2e7, r[1].FoundationC1);
        Assert.Equal("1 2 4", r[1].ReinforcementTypeIds);
        Assert.Equal(0.2, r[1].ThicknessM);
        Assert.Null(r[0].ReinforcementTypeIds);
        Assert.Null(r[0].ThicknessM);

        var f = db.GetFragmentFieldLoads(schema.Id);
        Assert.Equal([7, 8], f.Select(x => x.LoadCaseId));
        Assert.Equal("x", f[1].Axis);
        Assert.Equal(FemElementLoadKinds.Edge, f[1].LoadKind);
        Assert.Equal([2.0, 4, 1, 1], f[1].Entries[1].Values);

        db.ReplaceStoreyFragmentData(schema.Id, [], [], []);
        Assert.Empty(db.GetFragmentCuts(schema.Id));
        Assert.Empty(db.GetFragmentRefElements(schema.Id));
        Assert.Empty(db.GetFragmentFieldLoads(schema.Id));
    }

    [Fact]
    public void RebarSources_CopiedFromParent_WithoutAnalysisModel()
    {
        using var db = new DatabaseService(_path);
        var parent = new FemSchema { Tag = "Родитель" };
        var fragment = new FemSchema { Tag = "Фрагмент" };
        db.SaveFemSchema(parent);
        db.SaveFemSchema(fragment);
        db.SaveFemSchemaReinforcementFile(parent.Id, "a.rbt", [1, 2]);
        db.SaveFemSchemaSelectedReinforcementFile(parent.Id, "a.asp", [3]);
        db.SaveFemSchemaSourceFile(parent.Id, FemSchemaSourceFileKind.ScadAssignedRebar, "assigned.json", [4]);
        db.SaveFemSchemaSourceFile(parent.Id, FemSchemaSourceFileKind.ScadAnalysisModel, "model.spr", [5]);
        db.SaveFemSchemaStiffnesses(parent.Id, [new CScore.Import.LiraStiffnessRecord(3, 10, "Колонна", "40x40", 0.01)]);

        db.CopyFragmentRebarSources(parent.Id, fragment.Id);
        db.CopyFragmentRebarSources(parent.Id, fragment.Id);

        var sources = db.GetFragmentRebarSources(fragment.Id);
        Assert.Equal([DatabaseService.FragmentRebarSourceLiraAsp, DatabaseService.FragmentRebarSourceLiraRbt,
                      FemSchemaSourceFileKind.ScadAssignedRebar], sources.Select(s => s.Kind));
        Assert.Equal([1, 2], sources.Single(s => s.Kind == DatabaseService.FragmentRebarSourceLiraRbt).Data);
        Assert.Equal("assigned.json", sources[2].FileName);
        Assert.Equal("Колонна", db.GetFemSchemaStiffnesses(fragment.Id)[3].Name);
        Assert.Null(db.GetFemSchemaReinforcementFile(fragment.Id));
    }

    /// <summary>Импортное АЖТ «по точкам» (ведущий на колонне, ведомые в плите) пишется во фрагмент тегами его узлов,
    /// копируется с копией схемы и удаляется со схемой; v95 → v96 создаёт таблицу.</summary>
    [Fact]
    public void NodeRigidBodies_WrittenWithFragment_CopiedAndDeleted()
    {
        using (var db = new DatabaseService(_path))
        {
            var parent = StoreyFragmentDbFixture.CreateParent(db);
            var body = new FemRigidBody { MasterNodeTag = "102", SourceElemTag = "125938" };
            body.SetSlaveNodeTags(["1012", "1014"]);
            db.SaveFemBoundary(parent.Id, "import:scad", [], [], [body]);

            var fragment = StoreyFragmentService.Build(db, parent.Id, new StoreyFragmentRequest(1.0, 5.0, "Этаж")).Schema!;
            var stored = Assert.Single(db.GetFemNodeRigidBodies(fragment.Id));
            Assert.Equal(("import:scad", "125938"), (stored.Origin, stored.SourceElemTag));
            var nodes = db.GetFemNodes(fragment.Id).ToDictionary(n => n.NodeTag);
            Assert.Equal((0.0, 0.0, 1.5), (nodes[stored.MasterNodeTag].X, nodes[stored.MasterNodeTag].Y, nodes[stored.MasterNodeTag].Z));
            Assert.Equal([-1.5, 1.5], stored.SlaveNodeTags.Select(t => nodes[t].X).Order());

            var copy = db.DuplicateFemSchema(fragment.Id, "Копия");
            Assert.Equal(stored.SlaveNodeTagsJson, Assert.Single(db.GetFemNodeRigidBodies(copy.Id)).SlaveNodeTagsJson);
            db.DeleteFemSchema(copy);
        }
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM fem_node_rigid_bodies"));

        Exec("DROP TABLE fem_node_rigid_bodies; UPDATE settings SET value_json='95' WHERE key='schema_version'");
        using (new DatabaseService(_path)) { }
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM fem_node_rigid_bodies"));
    }

    object? Scalar(string sql)
    {
        using var c = new SqliteConnection($"Data Source={_path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    void Exec(string sql)
    {
        using var c = new SqliteConnection($"Data Source={_path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
