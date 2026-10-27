using CScore.Fem;
using CScore.Fem.Fragment;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Копия схемы (фрагмента и обычной с областями) и каскадное удаление (v92).</summary>
public sealed class StoreyFragmentCopyDeleteTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs-fragment-copy-{Guid.NewGuid():N}.db");
    static readonly StoreyFragmentRequest Band = new(1.5, 4.5, "Этаж 2");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }

    [Fact]
    public void Duplicate_Fragment_CopiesFragmentData()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        db.SaveFemSchemaReinforcementFile(parent.Id, "каркас.rbt", [7]);
        db.SaveFemSchemaStiffnesses(parent.Id, [new CScore.Import.LiraStiffnessRecord(3, 10, "Колонна", "40x40", 0.01)]);
        var fragment = StoreyFragmentService.Build(db, parent.Id, Band).Schema!;
        db.ReplaceStoreyFragmentData(fragment.Id, db.GetFragmentCuts(fragment.Id), db.GetFragmentRefElements(fragment.Id),
            [new FragmentFieldLoad(fragment.LoadCases[1].Id, FemElementLoadKinds.Nodal, "z", [new("1", [1, 2, 3, 4])])]);

        var copy = db.DuplicateFemSchema(fragment.Id, "Этаж 2 (копия)");

        Assert.True(copy.IsFragment);
        Assert.Equal((parent.Id, 1.5, 4.5, fragment.ParentFingerprint),
            (copy.ParentSchemaId!.Value, copy.FragmentZ1!.Value, copy.FragmentZ2!.Value, copy.ParentFingerprint));
        Assert.Equal(fragment.FragmentBuildReportJson, copy.FragmentBuildReportJson);
        Assert.Equal(db.GetFragmentCuts(fragment.Id).Count, db.GetFragmentCuts(copy.Id).Count);
        Assert.Equal(db.GetFragmentRefElements(fragment.Id).Select(e => e.Tag), db.GetFragmentRefElements(copy.Id).Select(e => e.Tag));
        var field = Assert.Single(db.GetFragmentFieldLoads(copy.Id));
        Assert.Equal(copy.LoadCases[1].Id, field.LoadCaseId);
        Assert.Equal([7], db.GetFragmentRebarSources(copy.Id).Single().Data);
        Assert.Equal("Колонна", db.GetFemSchemaStiffnesses(copy.Id)[3].Name);

        var regionIds = db.GetPlanarRegions(copy.Id).Select(r => r.Id).ToHashSet();
        var slab = db.GetFemMembers(copy.Id).Single(m => m.ElemTag == "S1");
        Assert.Contains(slab.PlanarRegionId!.Value, regionIds);
        Assert.DoesNotContain(slab.PlanarRegionId!.Value, db.GetPlanarRegions(fragment.Id).Select(r => r.Id));

        db.DeleteFemSchema(copy);
        Assert.NotEmpty(db.GetFragmentCuts(fragment.Id));
        Assert.NotEmpty(db.GetPlanarRegions(fragment.Id));
        Assert.Single(db.GetFemMembers(fragment.Id), m => m.ElemTag == "S1");
    }

    [Fact]
    public void Delete_Fragment_RemovesAllRows()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        db.SaveFemSchemaReinforcementFile(parent.Id, "каркас.rbt", [7]);
        var fragment = StoreyFragmentService.Build(db, parent.Id, Band).Schema!;
        int id = fragment.Id;

        db.DeleteFemSchema(fragment);

        foreach (var table in new[] { "fem_fragment_cuts", "fem_fragment_ref_elements", "fem_fragment_field_loads",
                                       "fem_fragment_rebar_sources", "planar_regions", "fem_members", "fem_nodes" })
            Assert.Equal(0L, Count($"SELECT COUNT(*) FROM {table} WHERE schema_id={id}"));
        Assert.NotEmpty(db.GetPlanarRegions(parent.Id));
    }

    [Fact]
    public void Duplicate_PlainSchema_KeepsRegionsAndTermRoles()
    {
        using var db = new DatabaseService(_path);
        var parent = StoreyFragmentDbFixture.CreateParent(db);
        var copy = db.DuplicateFemSchema(parent.Id, "Каркас (копия)");

        Assert.False(copy.IsFragment);
        Assert.Equal(2, db.GetPlanarRegions(copy.Id).Count);
        var copyRegions = db.GetPlanarRegions(copy.Id).Select(r => r.Id).ToHashSet();
        Assert.All(db.GetFemMembers(copy.Id).Where(m => m.ElemType == "shell"),
            m => Assert.Contains(m.PlanarRegionId!.Value, copyRegions));

        var lcs = db.GetFemLoadCases(copy.Id);
        var terms = Assert.Single(db.GetFemLoadDefinitions(copy.Id)).GetExpression().Terms;
        Assert.Equal(lcs.Select(c => c.Id), terms.Select(t => t.LoadCaseId));
        Assert.Equal([FemLoadTermRoles.Permanent, FemLoadTermRoles.Leading], terms.Select(t => t.Role));
        Assert.Equal([FemLoadTerm.AutoMode, FemLoadTerm.ManualMode], terms.Select(t => t.Mode));

        db.DeleteFemSchema(copy);
        Assert.Equal(0L, Count($"SELECT COUNT(*) FROM planar_regions WHERE schema_id={copy.Id}"));
        Assert.Equal(2, db.GetPlanarRegions(parent.Id).Count);
    }

    long Count(string sql)
    {
        using var c = new SqliteConnection($"Data Source={_path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return (long)cmd.ExecuteScalar()!;
    }
}
