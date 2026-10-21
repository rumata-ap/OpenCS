using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.Fem.Fields;
using CScore.PlateRebar;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Потребители C1 и пружин видят значения полей: вход CSfea, адаптированная модель, данные проверок.</summary>
public sealed class FemFieldConsumersTests : IDisposable
{
    readonly List<string> _paths = [];

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in _paths)
            try { File.Delete(path); } catch (IOException) { }
    }

    static FemMeshNode N(string tag, double x, double y) =>
        new() { NodeTag = tag, X = x, Y = y, Z = 3, Origin = FemMember.MeshSourceImported };

    static FemElement Shell(string tag, params int[] nodes) => new()
    {
        ElemTag = tag, ElemType = "shell", LocalAxisAngleDeg = 0, Origin = FemMember.MeshSourceImported,
        NodeIdsJson = JsonSerializer.Serialize(new[] { nodes[0], nodes[1], nodes[3], nodes[2] }),
    };

    static string CatalogDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenCS.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "OpenCS.App", "DataSource");
    }

    sealed record Fixture(DatabaseService Db, FemSchema Schema, FemLoadCase LoadCase);

    /// <summary>Плита из двух КЭ 2×2 м (как в FemCsfeaInputBuilderTests), обе в группе с сечением пластины.</summary>
    Fixture Create()
    {
        string path = Path.Combine(Path.GetTempPath(), "opencs_field_consumers_" + Guid.NewGuid().ToString("N") + ".db");
        _paths.Add(path);
        var db = new DatabaseService(path);
        var schema = new FemSchema { Tag = "Схема", SourceType = "lira" };
        db.SaveFemSchema(schema);
        db.SaveFemMeshSnapshot(schema.Id,
            [N("1", 0, 0), N("2", 2, 0), N("3", 2, 2), N("4", 0, 2), N("5", 4, 0), N("6", 4, 2)],
            [Shell("1", 1, 2, 3, 4), Shell("2", 2, 5, 6, 3)]);
        var concrete = MaterialCatalog.CreateHeavyConcrete("B25", CatalogDirectory())!;
        var rebar = MaterialCatalog.CreateRebar("А500С", CatalogDirectory())!;
        db.AddMaterial(concrete);
        db.AddMaterial(rebar);
        const double z = 0.065;
        var plate = new PlateSection
        {
            Tag = "Пл200", H = 0.2, ConcreteMaterialId = concrete.Id, RebarMaterialId = rebar.Id,
            RebarLayers =
            [
                new PlateRebarLayer { Name = "низ", InputMode = "direct", Asx = 5e-4, Asy = 5e-4, Zsx = -z, Zsy = -z, Face = RebarFace.MinusN },
                new PlateRebarLayer { Name = "верх", InputMode = "direct", Asx = 5e-4, Asy = 5e-4, Zsx = z, Zsy = z, Face = RebarFace.PlusN },
            ],
        };
        db.SavePlateSection(plate);
        db.SaveFemMemberGroup(new FemMemberGroup
        {
            SchemaId = schema.Id, Tag = "Плита", Kind = FemMemberGroup.KindMesh, MemberTagsJson = "[1, 2]", PlateSectionId = plate.Id,
        });
        var lc = new FemLoadCase { SchemaId = schema.Id, Tag = "Пост.", SelfWeightFactor = 1 };
        db.SaveFemLoadCase(lc);
        db.LoadAll();
        return new Fixture(db, schema, lc);
    }

    static FemCsfeaSetup Setup(FemLoadCase lc) => new()
    {
        Calc = CalcType.C,
        Stages =
        [
            new FemAnalysisStage
            {
                Tag = "Ст1", LoadFactorStep = 1, MaxLoadFactor = 1,
                LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [lc.Id] }.ToJson(),
            },
        ],
    };

    static FemField C1Everywhere(int schemaId, double nPerM3) => new()
    {
        SchemaId = schemaId, Name = "C1", Quantity = FemFieldQuantities.FoundationC1,
        Definition = new FemFieldDefinition
        {
            Columns = [new FemFieldColumn("C1", "c1", "Н/м³", 1)],
            Settings = new FieldSettings(FieldMethod.Nearest, MaxDistance: 10, PlaneTolerance: 0.5),
        },
        Points = [new(new(2, 1, 0), [nPerM3])],
    };

    [Fact]
    public void CsfeaInput_And_Model_UseFieldC1_SameAsDirectC1()
    {
        var f = Create();
        using var db = f.Db;
        var field = C1Everywhere(f.Schema.Id, 5e6);
        db.SaveFemField(field);
        FemFieldService.Recompute(db, field);

        var input = FemCsfeaInputBuilder.Build(db, f.Schema.Id, Setup(f.LoadCase));
        Assert.All(input.MeshElements, e => Assert.Equal(5e6, e.FoundationC1));
        var viaField = FemRcModelAdapter.Adapt(input).Model.Shells.Select(s => s.FoundationC1).ToList();
        Assert.All(viaField, c1 => Assert.Equal(5e6, c1));

        // Тот же C1 прямо в КЭ, без поля — модель совпадает.
        db.DeleteFemField(field.Id);
        var mesh = db.GetFemMeshElements(f.Schema.Id);
        foreach (var e in mesh) e.FoundationC1 = 5e6;
        db.SaveFemMeshSnapshot(f.Schema.Id, db.GetFemMeshNodes(f.Schema.Id), mesh);
        var direct = FemRcModelAdapter.Adapt(FemCsfeaInputBuilder.Build(db, f.Schema.Id, Setup(f.LoadCase))).Model.Shells
            .Select(s => s.FoundationC1).ToList();
        Assert.Equal(direct, viaField);
    }

    [Fact]
    public void CsfeaInput_UsesFieldSprings_PerDof()
    {
        var f = Create();
        using var db = f.Db;
        db.SaveFemBoundary(f.Schema.Id, FemLoadOrigin.Import("lira"), [],
            [new FemSpring { NodeTag = "5", Kx = 1e5, Kz = 2e5, SourceElemTag = "56" }], []);
        var field = new FemField
        {
            SchemaId = f.Schema.Id, Name = "Rz", Quantity = FemFieldQuantities.Spring,
            Definition = new FemFieldDefinition
            {
                Columns = [new FemFieldColumn("Rz", "kz", "Н/м", 1)],
                Settings = new FieldSettings(FieldMethod.Match, MatchTolerance: 0.5),
            },
            Points = [new(new(4.1, 0, 0), [9e6])],
        };
        db.SaveFemField(field);
        FemFieldService.Recompute(db, field);

        var spring = Assert.Single(FemCsfeaInputBuilder.Build(db, f.Schema.Id, Setup(f.LoadCase)).Springs, s => s.NodeTag == "5");
        Assert.Equal([1e5, 0, 9e6, 0, 0, 0], spring.Stiffnesses);
    }

    [Fact]
    public void CheckSchemaData_UsesFieldC1()
    {
        var f = Create();
        using var db = f.Db;
        var field = C1Everywhere(f.Schema.Id, 4e6);
        db.SaveFemField(field);
        FemFieldService.Recompute(db, field);
        Assert.All(FemCheckSchemaData.Load(db, f.Schema.Id).Mesh, e => Assert.Equal(4e6, e.FoundationC1));
    }
}
