using CScore.Fem;
using CScore.Fem.Fragment;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Сервис сборки фрагмента: снимок → построитель → запись схемы одной транзакцией.</summary>
public sealed class StoreyFragmentServiceTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs-fragment-svc-{Guid.NewGuid():N}.db");
    static readonly StoreyFragmentRequest Band = new(1.5, 4.5, "Этаж 2");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }

    [Fact]
    public void Build_WritesFragmentSchema()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        db.SaveFemSchemaReinforcementFile(parent.Id, "каркас.rbt", [1, 2, 3]);
        parent.SourceType = "lira";
        db.SaveFemSchema(parent);
        var parentLc = db.GetFemLoadCases(parent.Id).Select(c => c.Id).ToHashSet();

        var result = StoreyFragmentService.Build(db, parent.Id, Band);
        var schema = Assert.IsType<FemSchema>(result.Schema);
        int sid = schema.Id;

        Assert.Equal("Этаж 2", schema.Tag);
        Assert.Equal(parent.Id, schema.ParentSchemaId);
        Assert.Equal((1.5, 4.5), (schema.FragmentZ1!.Value, schema.FragmentZ2!.Value));
        Assert.Equal("internal", schema.SourceType);
        Assert.Equal("lira", schema.ParentSourceType);
        Assert.Equal(StoreyFragmentFingerprint.Compute(db.ReadStoreyFragmentSnapshot(parent.Id, Band)), schema.ParentFingerprint);
        Assert.Equal(result.Draft.Diagnostics.Count, StoreyFragmentBuildReport.FromJson(schema.FragmentBuildReportJson).Count);
        Assert.Contains(db.FemSchemas, s => s.Id == sid);

        var members = db.GetFemMembers(sid);
        Assert.Equal(["C1 · верх", "C1 · низ", "S1"], members.Select(m => m.ElemTag).Order());
        var regions = db.GetPlanarRegions(sid);
        var slab = members.Single(m => m.ElemTag == "S1");
        Assert.Contains(regions, r => r.Id == slab.PlanarRegionId);
        Assert.NotEmpty(db.GetFemNodes(sid));

        var lcs = db.GetFemLoadCases(sid);
        Assert.Equal(["Пост.", "Полезная"], lcs.Select(c => c.Tag));
        Assert.Equal(1.1, lcs[0].SelfWeightFactor);
        Assert.Equal(0.35, lcs[1].LongTermShare);
        var lcIds = lcs.Select(c => c.Id).ToHashSet();
        Assert.Empty(lcIds.Intersect(parentLc));

        var memberIds = members.Select(m => m.Id).ToHashSet();
        var memberLoads = db.GetFemMemberLoads(sid);
        Assert.NotEmpty(memberLoads);
        Assert.All(memberLoads, l => { Assert.Contains(l.LoadCaseId, lcIds); Assert.Contains(l.MemberId, memberIds); });
        var elementLoads = db.GetFemElementLoads(sid);
        Assert.NotEmpty(elementLoads);
        Assert.All(elementLoads, l => Assert.Contains(l.LoadCaseId, lcIds));
        Assert.Contains(elementLoads, l => l.TargetKind == FemLoadTargetKinds.Members && l.TargetTags.Contains("S1"));
        Assert.All(db.GetFemNodeLoads(sid), l => Assert.Contains(l.LoadCaseId, lcIds));
        Assert.All(db.GetFragmentFieldLoads(sid), l => Assert.Contains(l.LoadCaseId, lcIds));

        var definition = Assert.Single(db.GetFemLoadDefinitions(sid));
        var terms = definition.GetExpression().Terms;
        Assert.Equal([lcs[0].Id, lcs[1].Id], terms.Select(t => t.LoadCaseId));
        Assert.Equal([FemLoadTermRoles.Permanent, FemLoadTermRoles.Leading], terms.Select(t => t.Role));
        Assert.Equal([FemLoadTerm.AutoMode, FemLoadTerm.ManualMode], terms.Select(t => t.Mode));

        Assert.Equal(2, db.GetFragmentCuts(sid).Count);
        Assert.Equal(18, db.GetFragmentRefElements(sid).Count);
        Assert.Equal([1, 2, 3], db.GetFragmentRebarSources(sid).Single(s => s.Kind == DatabaseService.FragmentRebarSourceLiraRbt).Data);
        Assert.Empty(db.GetFemMeshElements(sid));
    }

    [Theory]
    [InlineData(4.5, 1.5)]
    [InlineData(3.0, 6.0)]
    public void Build_Refusal_CreatesNoSchema(double z1, double z2)
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        long before = Count("fem_schemas");
        var result = StoreyFragmentService.Build(db, parent.Id, new StoreyFragmentRequest(z1, z2, "Отказ"));
        Assert.Null(result.Schema);
        Assert.True(result.Draft.HasErrors);
        Assert.Equal(before, Count("fem_schemas"));
    }

    [Fact]
    public void Create_FailureMidway_RollsBack()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        var draft = StoreyFragmentBuilder.Build(db.ReadStoreyFragmentSnapshot(parent.Id, Band), Band);
        draft.MemberLoads.Add(new FragmentMemberLoad("нет такого", new FemMemberLoad { LoadCaseId = draft.LoadCases[0].Id }));
        string[] tables = ["fem_schemas", "fem_nodes", "fem_members", "planar_regions", "fem_load_cases", "fem_load_definitions",
                           "fem_element_loads", "fem_fragment_cuts", "fem_fragment_ref_elements"];
        var before = tables.Select(Count).ToList();
        int cached = db.FemSchemas.Count;

        Assert.Throws<InvalidOperationException>(() => db.CreateStoreyFragmentSchema(parent.Id, Band, draft, "X"));
        Assert.Equal(before, tables.Select(Count).ToList());
        Assert.Equal(cached, db.FemSchemas.Count);
    }

    [Fact]
    public void Build_TransfersMeshNodeSpringsAndSupports()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        // 1007 — узел сетки S1 (−1,5; −1,5; 3), 2001 — S2 на z = 9 (вне полосы), 1013 — (0; 0; 3).
        var spring = new FemSpring { TargetKind = FemSpringTargetKinds.MeshNode, NodeTag = "1007", Kz = 2e6, SourceElemTag = "51" };
        var outside = new FemSpring { TargetKind = FemSpringTargetKinds.MeshNode, NodeTag = "2001", Kz = 1e9 };
        db.SaveFemBoundary(parent.Id, "import:scad", [new FemMeshNodeSupport { NodeTag = "1013", Mask = 7 }], [spring, outside], []);

        var fragment = StoreyFragmentService.Build(db, parent.Id, Band).Schema!;

        var s = Assert.Single(db.GetFemSprings(fragment.Id));
        Assert.Equal((FemSpringTargetKinds.Node, 2e6, "import:scad", "51"), (s.TargetKind, s.Kz, s.Origin, s.SourceElemTag));
        var nodes = db.GetFemNodes(fragment.Id);
        var node = nodes.Single(n => n.NodeTag == s.NodeTag);
        Assert.Equal((-1.5, -1.5, 3.0), (node.X, node.Y, node.Z));
        Assert.Equal(7, nodes.Single(n => n.X == 0 && n.Y == 0 && n.Z == 3).DofMask & 7);
        Assert.Single(FemFieldService.EffectiveSprings(db, fragment.Id));
        Assert.Equal(FragmentParentStatus.Unchanged, StoreyFragmentService.CheckParent(db, fragment));

        Exec($"UPDATE fem_springs SET kz=3e6 WHERE schema_id={parent.Id} AND node_tag='1007'");
        Assert.Equal(FragmentParentStatus.Changed, StoreyFragmentService.CheckParent(db, fragment));
    }

    [Fact]
    public void CheckParent_TracksBandChangesAndDeletion()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        var fragment = StoreyFragmentService.Build(db, parent.Id, Band).Schema!;
        Assert.Equal(FragmentParentStatus.Unchanged, StoreyFragmentService.CheckParent(db, fragment));
        Assert.Equal(FragmentParentStatus.NotFragment, StoreyFragmentService.CheckParent(db, parent));

        Exec($"UPDATE fem_element_loads SET values_json='[-1500,0.5,0.5]' WHERE schema_id={parent.Id} AND load_kind='point'");
        Assert.Equal(FragmentParentStatus.Unchanged, StoreyFragmentService.CheckParent(db, fragment));

        Exec($"UPDATE fem_element_loads SET values_json='[-6000]' WHERE schema_id={parent.Id} AND load_kind='uniform'");
        Assert.Equal(FragmentParentStatus.Changed, StoreyFragmentService.CheckParent(db, fragment));

        db.DeleteFemSchema(parent);
        Assert.Equal(FragmentParentStatus.ParentDeleted, StoreyFragmentService.CheckParent(db, fragment));
        Assert.NotEmpty(db.GetFemMembers(fragment.Id));
        Assert.NotEmpty(db.GetFragmentRefElements(fragment.Id));
    }

    void Exec(string sql)
    {
        using var c = new SqliteConnection($"Data Source={_path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    long Count(string table)
    {
        using var c = new SqliteConnection($"Data Source={_path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)cmd.ExecuteScalar()!;
    }
}
