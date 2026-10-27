using System.Text.Json;
using CScore.Fem;
using CScore.Fem.Fields;
using CScore.Fem.Fragment;
using CScore.Planar;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Фрагмент этажа в расчёте (срез 4): схема-фрагмент с сеткой из двух КЭ «1» (x 0…2) и «2» (x 2…4) на z = 3 и опорной
/// сеткой из КЭ родителя «101» и «102» на тех же местах.
/// </summary>
public sealed class StoreyFragmentAnalysisTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs-fragment-calc-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { }
    }

    static FemMeshNode N(string tag, double x, double y) => new() { NodeTag = tag, X = x, Y = y, Z = 3 };

    static FemElement Shell(string tag, params int[] nodes) => new()
    {
        ElemTag = tag, ElemType = "shell", NodeIdsJson = JsonSerializer.Serialize(new[] { nodes[0], nodes[1], nodes[3], nodes[2] }),
    };

    static FragmentRefElement Ref(string tag, double x0, double x1, double? c1 = null, string? rebarTypes = null) =>
        new(tag, "shell", [new PlanarVector3(x0, 0, 3), new PlanarVector3(x1, 0, 3), new PlanarVector3(x0, 2, 3), new PlanarVector3(x1, 2, 3)],
            foundationC1: c1, reinforcementTypeIds: rebarTypes);

    sealed record Fixture(DatabaseService Db, FemSchema Schema, FemLoadCase LoadCase);

    Fixture Create(string parentSourceType = "lira", params FragmentRefElement[] refs)
    {
        var db = new DatabaseService(_path);
        var schema = new FemSchema
        {
            Tag = "Этаж", ParentSchemaId = 999, FragmentZ1 = 1.5, FragmentZ2 = 4.5, ParentSourceType = parentSourceType,
        };
        db.SaveFemSchema(schema);
        db.SaveFemMeshSnapshot(schema.Id,
            [N("1", 0, 0), N("2", 2, 0), N("3", 2, 2), N("4", 0, 2), N("5", 4, 0), N("6", 4, 2)],
            [Shell("1", 1, 2, 3, 4), Shell("2", 2, 5, 6, 3)]);
        var lc = new FemLoadCase { SchemaId = schema.Id, Tag = "Полезная" };
        db.SaveFemLoadCase(lc);
        if (refs.Length == 0) refs = [Ref("101", 0, 2), Ref("102", 2, 4)];
        db.ReplaceStoreyFragmentData(schema.Id, [], refs,
            [new FragmentFieldLoad(lc.Id, FemElementLoadKinds.Uniform, "z", [new("101", [-1000]), new("102", [-3000])])]);
        db.LoadAll();
        return new Fixture(db, db.FemSchemas.Single(s => s.Id == schema.Id), lc);
    }

    [Fact]
    public void CsfeaInput_FieldLoadsResolvedOnFragmentMesh()
    {
        var f = Create();

        var input = FemCsfeaInputBuilder.Build(f.Db, f.Schema.Id, new FemCsfeaSetup());

        var field = input.ElementLoads.Where(l => l.LoadKind == FemElementLoadKinds.Uniform && l.TargetKind == FemLoadTargetKinds.Elements).ToList();
        Assert.Equal(2, field.Count);
        Assert.All(field, l => Assert.Equal((f.LoadCase.Id, "z", "global"), (l.LoadCaseId, l.Axis, l.CoordinateSystem)));
        Assert.Equal(-1000, field.Single(l => l.TargetTags.SequenceEqual(["1"])).Values[0]);
        Assert.Equal(-3000, field.Single(l => l.TargetTags.SequenceEqual(["2"])).Values[0]);
    }

    [Fact]
    public void Effective_ParentC1OnFragmentMesh_ManualOverrideWins()
    {
        var f = Create("lira", Ref("101", 0, 2, c1: 5000), Ref("102", 2, 4));
        int sid = f.Schema.Id;

        var e = FemFieldService.Effective(f.Db, sid).Elements.ToDictionary(x => x.ElemTag);
        Assert.Equal(5000, e["1"].FoundationC1);
        Assert.Null(e["2"].FoundationC1);
        Assert.Equal(1, FemFieldService.FoundationElementCount(f.Db, sid));
        Assert.Null(f.Db.GetFemMeshElements(sid).Single(x => x.ElemTag == "1").FoundationC1);

        f.Db.ReplaceFemParamOverrides(sid,
        [
            new FemParamOverride(FemFieldTargetKind.Shell, "1", FemFieldQuantities.FoundationC1, "c1", 100),
            new FemParamOverride(FemFieldTargetKind.Shell, "2", FemFieldQuantities.FoundationC1, "c1", 7000),
        ]);
        e = FemFieldService.Effective(f.Db, sid).Elements.ToDictionary(x => x.ElemTag);
        Assert.Equal(100, e["1"].FoundationC1);
        Assert.Equal(7000, e["2"].FoundationC1);
    }

    [Fact]
    public void Effective_FragmentWithoutParentC1_RawElements()
    {
        var f = Create();
        Assert.False(f.Db.HasFemFieldLayers(f.Schema.Id));
        Assert.All(FemFieldService.Effective(f.Db, f.Schema.Id).Elements, x => Assert.Null(x.FoundationC1));
    }

    [Fact]
    public void ElementLoads_PlainSchema_OnlyStoredLoads()
    {
        var f = Create();
        var plain = new FemSchema { Tag = "Обычная" };
        f.Db.SaveFemSchema(plain);

        Assert.Empty(FragmentLoadService.ElementLoads(f.Db, plain.Id, [], [], [], []));
    }

    [Fact]
    public void ElementLoads_FragmentWithoutMesh_FieldsSkipped()
    {
        var f = Create();
        f.Db.SaveFemMeshSnapshot(f.Schema.Id, [], []);

        var diag = new List<FemValidationDiagnostic>();
        Assert.Empty(FragmentLoadService.ElementLoads(f.Db, f.Schema.Id, [], [], [], diag));
    }
}
