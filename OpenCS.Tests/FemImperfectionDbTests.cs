using System.Text.Json;
using CScore.Fem;
using CScore.Fem.Imperfections;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Загружение «Несовершенство» (срез 1): таблица <c>fem_imperfections</c> (v99) — миграция, CRUD, удаление загружения и
/// схемы, копия и пересохранение схемы; стадии постановки (объединение, конфликт коэффициентов) и сборка входа CSfea
/// (учёт только во втором порядке).
/// </summary>
public sealed class FemImperfectionDbTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs-imperfections-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }

    static FemMeshNode N(string tag, double x, double y, double z) => new() { NodeTag = tag, X = x, Y = y, Z = z, Origin = FemMember.MeshSourceImported };

    static FemElement Shell(string tag, params int[] nodes) => new()
    {
        ElemTag = tag, ElemType = "shell", LocalAxisAngleDeg = 0, Origin = FemMember.MeshSourceImported,
        NodeIdsJson = JsonSerializer.Serialize(new[] { nodes[0], nodes[1], nodes[3], nodes[2] }),
    };

    sealed record Fixture(DatabaseService Db, FemSchema Schema, FemLoadCase Load, FemLoadCase Imp, FemImperfection Sway);

    /// <summary>Стена из двух КЭ в плоскости XZ; загружения «П» и «Imp» (несовершенство с креном 1/200 по +X).</summary>
    Fixture Create()
    {
        var db = new DatabaseService(_path);
        var schema = new FemSchema { Tag = "Схема", SourceType = "lira" };
        db.SaveFemSchema(schema);
        db.SaveFemMeshSnapshot(schema.Id,
            [N("1", 0, 0, 0), N("2", 2, 0, 0), N("3", 2, 0, 3), N("4", 0, 0, 3), N("5", 4, 0, 0), N("6", 4, 0, 3)],
            [Shell("1", 1, 2, 3, 4), Shell("2", 2, 5, 6, 3)]);
        var load = new FemLoadCase { SchemaId = schema.Id, Tag = "П", Sp20Type = FemSp20Types.Permanent };
        var imp = new FemLoadCase { SchemaId = schema.Id, Tag = "Imp", Sp20Type = FemSp20Types.Imperfection };
        db.SaveFemLoadCase(load);
        db.SaveFemLoadCase(imp);
        var sway = new FemImperfection
        {
            SchemaId = schema.Id, LoadCaseId = imp.Id, Kind = FemImperfectionKinds.Sway, Tag = "Крен X", Comment = "в запас",
            ParamsJson = new FemSwayParams { PhiInv = 200 }.ToJson(),
        };
        db.SaveFemImperfection(sway);
        db.LoadAll();
        return new Fixture(db, db.FemSchemas.Single(s => s.Id == schema.Id), load, imp, sway);
    }

    long Scalar(string sql)
    {
        using var c = new SqliteConnection($"Data Source={_path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    void Exec(string sql)
    {
        using var c = new SqliteConnection($"Data Source={_path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void NewDatabase_HasTable_AndV98Migrates()
    {
        using (new DatabaseService(_path)) { }
        Assert.True(DatabaseService.SchemaVersion >= 99);
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='fem_imperfections'"));

        Exec("DROP TABLE fem_imperfections; UPDATE settings SET value_json='98' WHERE key='schema_version'");
        using (var db = new DatabaseService(_path)) Assert.Empty(db.GetFemImperfections(1));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='fem_imperfections'"));
        Assert.Equal(DatabaseService.SchemaVersion, (int)Scalar("SELECT CAST(value_json AS INTEGER) FROM settings WHERE key='schema_version'"));
    }

    [Fact]
    public void Crud_RoundTrip_AndDeleteLoadCase()
    {
        var f = Create();
        using var db = f.Db;
        var bow = new FemImperfection
        {
            SchemaId = f.Schema.Id, LoadCaseId = f.Imp.Id, Kind = FemImperfectionKinds.Bow,
            ParamsJson = new FemBowParams { MemberTags = ["К1"], LOverE0 = 300 }.ToJson(),
        };
        db.SaveFemImperfection(bow);
        var read = db.GetFemImperfections(f.Schema.Id);
        Assert.Equal(2, read.Count);
        var s = read[0];
        Assert.Equal((f.Sway.Id, f.Imp.Id, FemImperfectionKinds.Sway, "Крен X", "в запас"), (s.Id, s.LoadCaseId, s.Kind, s.Tag, s.Comment));
        Assert.Equal(200, FemSwayParams.Parse(s.ParamsJson).PhiInv);
        Assert.Null(read[1].Comment);

        bow.Tag = "Погибь";
        bow.Comment = "К1";
        db.SaveFemImperfection(bow);
        Assert.Equal("Погибь", db.GetFemImperfections(f.Schema.Id).Single(i => i.Id == bow.Id).Tag);
        db.DeleteFemImperfection(bow);
        Assert.Single(db.GetFemImperfections(f.Schema.Id));

        db.DeleteFemLoadCase(f.Imp);
        Assert.Empty(db.GetFemImperfections(f.Schema.Id));
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM fem_imperfections"));
    }

    [Fact]
    public void Duplicate_Edit_AndDeleteSchema()
    {
        var f = Create();
        using var db = f.Db;

        var copy = db.DuplicateFemSchema(f.Schema.Id, "Копия");
        var copied = Assert.Single(db.GetFemImperfections(copy.Id));
        var copyImp = db.GetFemLoadCases(copy.Id).Single(c => c.Tag == "Imp");
        Assert.Equal(copyImp.Id, copied.LoadCaseId);
        Assert.Equal((f.Sway.Kind, f.Sway.Tag, f.Sway.ParamsJson, f.Sway.Comment), (copied.Kind, copied.Tag, copied.ParamsJson, copied.Comment));

        // Пересохранение схемы даёт загружениям новые Id: элемент переходит к новому Id «Imp», удалённого загружения — исчезает.
        var cases = db.GetFemLoadCases(f.Schema.Id);
        db.SaveFemSchemaEdit(f.Schema.Id, [], [], f.Schema.MemberGroups.ToList(), cases, []);
        var newImp = db.GetFemLoadCases(f.Schema.Id).Single(c => c.Tag == "Imp");
        Assert.NotEqual(f.Imp.Id, newImp.Id);
        Assert.Equal(newImp.Id, Assert.Single(db.GetFemImperfections(f.Schema.Id)).LoadCaseId);
        db.SaveFemSchemaEdit(f.Schema.Id, [], [], f.Schema.MemberGroups.ToList(),
            db.GetFemLoadCases(f.Schema.Id).Where(c => c.Tag != "Imp").ToList(), []);
        Assert.Empty(db.GetFemImperfections(f.Schema.Id));

        db.DeleteFemSchema(copy);
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM fem_imperfections"));
    }

    static FemAnalysisStage Stage(string tag, double max, params (int Id, double K)[] terms) => new()
    {
        Tag = tag, MaxLoadFactor = max, LoadFactorStep = max,
        LoadExpressionJson = new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum, Terms = terms.Select(t => new FemLoadTerm { LoadCaseId = t.Id, Coefficient = t.K }).ToList(),
        }.ToJson(),
    };

    [Fact]
    public void Stages_ImperfectionsLeaveLoad_UnionAndMismatch()
    {
        var p = new FemLoadCase { Id = 1, Tag = "П" };
        var a = new FemLoadCase { Id = 2, Tag = "ImpA", Sp20Type = FemSp20Types.Imperfection };
        var b = new FemLoadCase { Id = 3, Tag = "ImpB", Sp20Type = FemSp20Types.Imperfection };
        var diag = new List<FemValidationDiagnostic>();
        var stages = FemCsfeaInputBuilder.Stages([Stage("1", 2.0, (1, 1.0), (2, -1.0)), Stage("2", 1.0, (1, 0.5), (2, -1.0), (3, 0.5))],
            [p, a, b], diag, out var imperfections);
        Assert.Empty(diag);
        Assert.Equal([(1, 2.0)], stages[0].Terms);
        Assert.Equal([(1, 0.5)], stages[1].Terms);
        // Коэффициент несовершенства — без максимума коэффициента нагрузки стадии.
        Assert.Equal([(2, -1.0), (3, 0.5)], imperfections);

        FemCsfeaInputBuilder.Stages([Stage("1", 1.0, (1, 1.0), (2, 1.0)), Stage("2", 1.0, (2, 0.5))], [p, a], diag, out var mixed);
        var error = Assert.Single(diag);
        Assert.Equal(("imperfection_factor_mismatch", true), (error.Code, error.IsError));
        Assert.Equal([(2, 1.0)], mixed);
    }

    [Fact]
    public void Build_SecondOrderOnly()
    {
        var f = Create();
        using var db = f.Db;
        var stages = new[] { Stage("Ст1", 1.0, (f.Load.Id, 1.0), (f.Imp.Id, -0.5)) };

        var second = FemCsfeaInputBuilder.Build(db, f.Schema.Id, new FemCsfeaSetup { Stages = stages, SecondOrder = true }, sections: false);
        var sway = Assert.IsType<FemResolvedSway>(Assert.Single(second.Imperfections));
        Assert.Equal(-0.5 / 200, sway.Phi, 15);
        Assert.Null(sway.ElemTags);
        Assert.Equal([(f.Load.Id, 1.0)], Assert.Single(second.Stages).Terms);
        Assert.DoesNotContain(second.Diagnostics, d => d.Code.StartsWith("imperfection", StringComparison.Ordinal));

        var first = FemCsfeaInputBuilder.Build(db, f.Schema.Id, new FemCsfeaSetup { Stages = stages }, sections: false);
        Assert.Empty(first.Imperfections);
        Assert.Contains(first.Diagnostics, d => d.Code == "imperfection_ignored_first_order" && !d.IsError && d.Message.Contains("«Imp»"));

        var cr = FemCsfeaInputBuilder.Build(db, f.Schema.Id, new FemCsfeaSetup { Stages = stages, LargeDisplacements = true }, sections: false);
        Assert.Contains(cr.Diagnostics, d => d.Code == "imperfection_cr" && d.IsError);
        var batch = FemCsfeaInputBuilder.Build(db, f.Schema.Id,
            new FemCsfeaSetup { Stages = stages, SecondOrder = true, IndependentStages = true }, sections: false);
        Assert.Contains(batch.Diagnostics, d => d.Code == "imperfection_batch" && d.IsError);

        // Нагрузка в загружении-несовершенстве — ошибка (в нагрузку стадий оно не входит).
        db.SaveFemLoadCasesAndMeshLoads(f.Schema.Id, db.GetFemLoadCases(f.Schema.Id), [],
            [new FemMeshNodeLoad { SchemaId = f.Schema.Id, LoadCaseId = f.Imp.Id, MeshNodeTag = "3", Fx = 1 }]);
        var loaded = FemCsfeaInputBuilder.Build(db, f.Schema.Id, new FemCsfeaSetup { Stages = stages, SecondOrder = true }, sections: false);
        Assert.Contains(loaded.Diagnostics, d => d.Code == FemCanonicalValidator.ImperfectionHasLoadsCode && d.IsError);
    }

    [Fact]
    public void Setup_FromAnalysis_Flags()
    {
        FemCsfeaSetup Setup(string kind, bool geom, bool large, bool batch = false)
        {
            var p = new FemAnalysisParams { Csfea = new FemCsfeaParams { GeomNonlinear = geom, GeomLargeDisplacements = large } };
            if (batch) p.Batch = new FemCsfeaBatch();
            return FemCsfeaSetup.FromAnalysis(new FemAnalysis { Kind = kind, Tag = "A", ParamsJson = p.ToJson() });
        }
        Assert.True(Setup(FemCsfeaRunner.AnalysisKind, true, false).SecondOrder);
        Assert.False(Setup(FemCsfeaRunner.AnalysisKind, false, false).SecondOrder);
        Assert.False(Setup(FemCsfeaRunner.LinearAnalysisKind, true, false).SecondOrder);
        var cr = Setup(FemCsfeaRunner.AnalysisKind, true, true);
        Assert.True(cr.LargeDisplacements && !cr.SecondOrder);
        Assert.True(Setup(FemCsfeaRunner.AnalysisKind, true, false, batch: true).IndependentStages);
    }
}
