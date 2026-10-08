using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.PlateRebar;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Сборка входа CSfea из БД (срез 4в-5): шаблон сечения пластины, материалы, стадии, диагностики.</summary>
public sealed class FemCsfeaInputBuilderTests : IDisposable
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

    string NewPath()
    {
        string path = Path.Combine(Path.GetTempPath(), "opencs_csfea_input_" + Guid.NewGuid().ToString("N") + ".db");
        _paths.Add(path);
        return path;
    }

    static string CatalogDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenCS.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "OpenCS", "DataSource");
    }

    sealed record Fixture(DatabaseService Db, FemSchema Schema, PlateSection Plate, FemLoadCase LoadCase);

    /// <summary>Плита из двух КЭ 2×2 м; КЭ 1 — в группе КЭ с сечением пластины, КЭ 2 — без сечения.</summary>
    Fixture Create(int? concreteId = null)
    {
        var db = new DatabaseService(NewPath());
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
            Tag = "Пл200", H = 0.2,
            ConcreteMaterialId = concreteId ?? concrete.Id,
            RebarMaterialId = rebar.Id,
            RebarLayers =
            [
                new PlateRebarLayer { Name = "низ", InputMode = "direct", Asx = 5e-4, Asy = 5e-4, Zsx = -z, Zsy = -z, Face = RebarFace.MinusN },
                new PlateRebarLayer { Name = "верх", InputMode = "direct", Asx = 5e-4, Asy = 5e-4, Zsx = z, Zsy = z, Face = RebarFace.PlusN },
            ],
        };
        db.SavePlateSection(plate);
        db.SaveFemMemberGroup(new FemMemberGroup
        {
            SchemaId = schema.Id, Tag = "Плита", Kind = FemMemberGroup.KindMesh, MemberTagsJson = "[1]", PlateSectionId = plate.Id,
        });
        var lc = new FemLoadCase { SchemaId = schema.Id, Tag = "Пост.", SelfWeightFactor = 1 };
        db.SaveFemLoadCase(lc);
        db.LoadAll();
        return new Fixture(db, schema, plate, lc);
    }

    static FemAnalysisStage Stage(string tag, FemLoadCase lc, double step, double max) => new()
    {
        Tag = tag, LoadFactorStep = step, MaxLoadFactor = max,
        LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [lc.Id] }.ToJson(),
    };

    [Fact]
    public void Build_PlateSectionFromGroup_AndStages()
    {
        var f = Create();
        using var db = f.Db;

        var input = FemCsfeaInputBuilder.Build(db, f.Schema.Id,
            new FemCsfeaSetup { Calc = CalcType.C, Stages = [Stage("Ст1", f.LoadCase, 0.25, 2.0)] });

        Assert.Equal(6, input.MeshNodes.Count);
        Assert.Equal(2, input.MeshElements.Count);
        var byTag = input.MeshElements.ToDictionary(e => e.ElemTag);
        var s1 = input.PlateSection!(byTag["1"]);
        Assert.NotNull(s1);
        Assert.Equal(0.2, s1!.Section.Section!.H);
        Assert.NotNull(s1.Materials);
        Assert.Contains($"|{CalcType.C}", s1.MaterialsKey);
        Assert.Null(input.PlateSection(byTag["2"]));
        Assert.Contains(input.Diagnostics, d => d.Code == "shell_no_template" && !d.IsError && d.SourceKeys!.SequenceEqual(["2"]));

        var stage = Assert.Single(input.Stages);
        Assert.Equal(8, stage.Steps);
        Assert.Equal([(f.LoadCase.Id, 2.0)], stage.Terms);

        // Адаптер: КЭ 1 — ЖБ-сечение, КЭ 2 — без сечения и без упругих свойств (жёсткостей ЛИРЫ нет) → ошибка.
        var result = FemRcModelAdapter.Adapt(input);
        Assert.Contains(result.Model.Shells, s => s.Id == 1 && s.Section.Plate != null);
        Assert.Contains(result.Diagnostics, d => d.Code == "shell_no_section" && d.SourceKeys!.SequenceEqual(["2"]));
    }

    [Fact]
    public void Build_GroupConflict_TakesFirstGroupAndWarns()
    {
        var f = Create();
        using var db = f.Db;
        var other = new PlateSection
        {
            Tag = "Пл300", H = 0.3, ConcreteMaterialId = f.Plate.ConcreteMaterialId, RebarMaterialId = f.Plate.RebarMaterialId,
        };
        db.SavePlateSection(other);
        db.SaveFemMemberGroup(new FemMemberGroup
        {
            SchemaId = f.Schema.Id, Tag = "Ещё", Kind = FemMemberGroup.KindMesh, MemberTagsJson = "[1, 2]", PlateSectionId = other.Id,
        });
        db.LoadAll();

        var input = FemCsfeaInputBuilder.Build(db, f.Schema.Id, new FemCsfeaSetup { Stages = [Stage("Ст1", f.LoadCase, 1, 1)] });

        var byTag = input.MeshElements.ToDictionary(e => e.ElemTag);
        Assert.Equal(0.2, input.PlateSection!(byTag["1"])!.Section.Section!.H);
        Assert.Equal(0.3, input.PlateSection(byTag["2"])!.Section.Section!.H);
        Assert.Contains(input.Diagnostics, d => d.Code == "shell_template_conflict" && d.SourceKeys!.SequenceEqual(["1"]));
    }

    [Fact]
    public void Build_MissingConcrete_IsError()
    {
        var f = Create(concreteId: 987654);
        using var db = f.Db;

        var input = FemCsfeaInputBuilder.Build(db, f.Schema.Id, new FemCsfeaSetup { Stages = [Stage("Ст1", f.LoadCase, 1, 1)] });

        Assert.Null(input.PlateSection?.Invoke(input.MeshElements.Single(e => e.ElemTag == "1")));
        Assert.Contains(input.Diagnostics, d => d.Code == "plate_materials" && d.IsError);
        Assert.True(FemRcModelAdapter.Adapt(input).HasErrors);
    }

    [Fact]
    public void Stages_Sp20WithoutTerms_IsError()
    {
        var lc = new FemLoadCase { Id = 5, Tag = "П" };
        var diag = new List<FemValidationDiagnostic>();
        var stages = FemCsfeaInputBuilder.Stages(
        [
            new FemAnalysisStage { Tag = "A", LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Sp20 }.ToJson() },
            new FemAnalysisStage { Tag = "B", MaxLoadFactor = 1.5, LoadFactorStep = 0.4,
                LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Sum,
                    Terms = [new FemLoadTerm { LoadCaseId = 5, Coefficient = 0.9 }] }.ToJson() },
        ], [lc], diag);

        Assert.Empty(stages[0].Terms);
        Assert.Contains(diag, d => d.Code == "stage_expression" && d.IsError);
        Assert.Equal(4, stages[1].Steps);
        Assert.Equal(5, stages[1].Terms[0].LoadCaseId);
        Assert.Equal(1.35, stages[1].Terms[0].Factor, 12);
    }
}
