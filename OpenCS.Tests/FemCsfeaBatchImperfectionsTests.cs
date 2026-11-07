using CScore;
using CScore.Combinations;
using CScore.Fem;
using CScore.Fem.Combinations;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Несовершенства EN 1993-1-1, срез 4: варианты пакета CSfea «элементы × несовершенства × знаки» (теги, выражения, элемент
/// с несовершенством не перемножается, досчёт), несовершенство в сочетаниях — как постоянное (длительная часть целиком),
/// признак <see cref="ForceSet.Imperfections"/> (БД v100, сочетание наборов СП 20) и примечание проверки стали по КЭ.
/// </summary>
public sealed class FemCsfeaBatchImperfectionsTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs_batch_imperfections_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }

    static readonly FemLoadCase G = new() { Id = 1, Tag = "G", Sp20Type = FemSp20Types.Permanent };
    static readonly FemLoadCase Q = new() { Id = 2, Tag = "Q", Sp20Type = FemSp20Types.ShortTerm, LongTermShare = 0.35 };
    static readonly FemLoadCase ImpX = new() { Id = 3, Tag = "ImpX", Sp20Type = FemSp20Types.Imperfection };
    static readonly FemLoadCase ImpY = new() { Id = 4, Tag = "ImpY", Sp20Type = FemSp20Types.Imperfection };
    static readonly IReadOnlyList<FemLoadCase> Cases = [G, Q, ImpX, ImpY];

    static string Expr(string? type, params (int Id, double K)[] terms) => new FemLoadExpression
    {
        Mode = FemLoadExpressionMode.Sp20, CombinationType = type,
        Terms = terms.Select(t => new FemLoadTerm { LoadCaseId = t.Id, Coefficient = t.K }).ToList(),
    }.ToJson();

    static FemCsfeaBatch Batch() => new()
    {
        Items =
        [
            new FemCsfeaBatchItem { Tag = "C1", Type = "N", LoadExpressionJson = Expr("N", (1, 1.0), (2, 1.0)) },
            new FemCsfeaBatchItem
            {
                Tag = "Q", Kind = FemCsfeaBatchItemKind.LoadCase, Type = "Q",
                LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [2] }.ToJson(),
            },
            // Несовершенство уже в выражении — элемент не перемножается.
            new FemCsfeaBatchItem { Tag = "C3", Type = "N", LoadExpressionJson = Expr("N", (1, 1.0), (4, -1.0)) },
        ],
        Imperfections = [new FemCsfeaBatchImperfection { LoadCaseId = 3, BothSigns = true }, new FemCsfeaBatchImperfection { LoadCaseId = 4 }],
    };

    static Dictionary<int, double> Factors(string json) =>
        FemLoadExpressionResolver.Terms(FemLoadExpression.Parse(json), Cases).ToDictionary(t => t.LoadCase.Id, t => t.Factor);

    [Fact]
    public void Variants_ItemsTimesImperfectionsTimesSigns()
    {
        var batch = Batch();
        var variants = batch.Variants(Cases);
        Assert.Equal(["C1 · ImpX+", "C1 · ImpX−", "C1 · ImpY", "Q · ImpX+", "Q · ImpX−", "Q · ImpY", "C3"], variants.Select(v => v.Tag));
        Assert.Equal(new Dictionary<int, double> { [1] = 1, [2] = 1, [3] = -1 }, Factors(variants[1].LoadExpressionJson));
        Assert.Equal(new Dictionary<int, double> { [2] = 1, [4] = 1 }, Factors(variants[5].LoadExpressionJson));
        Assert.Equal("N", FemLoadExpression.Parse(variants[0].LoadExpressionJson).CombinationType);
        Assert.Equal((FemCsfeaBatchItemKind.LoadCase, "Q"), (variants[3].Kind, variants[3].Type));
        Assert.Same(batch.Items[2], variants[6]);
        Assert.Equal(variants.Count, variants.Select(v => v.LoadExpressionJson).Distinct().Count());

        Assert.Equal(7, batch.ToStages(Cases).Count);
        // Без загружений (нельзя узнать вид) и без несовершенств — сами элементы.
        Assert.Equal(3, batch.ToStages().Count);
        batch.Imperfections = [new FemCsfeaBatchImperfection { LoadCaseId = 2 }];   // не несовершенство — пропуск
        Assert.Equal(3, batch.Variants(Cases).Count);
    }

    [Fact]
    public void ImperfectionsListChange_IsBatchItemsOnly()
    {
        var before = new FemAnalysisParams { Batch = Batch() };
        var after = new FemAnalysisParams { Batch = Batch() };
        after.Batch.Imperfections.RemoveAt(1);
        after.Batch.Imperfections[0].BothSigns = false;
        Assert.True(FemAnalysisParams.BatchItemsOnlyChanged(before.ToJson(), after.ToJson()));
        after.Batch.MaxLoadFactor = 2;
        Assert.False(FemAnalysisParams.BatchItemsOnlyChanged(before.ToJson(), after.ToJson()));
        Assert.Equal(2, FemAnalysisParams.Parse(before.ToJson()).Batch!.Imperfections.Count);
    }

    [Fact]
    public void Imperfection_IsPermanentInCombinations_LongPartWhole()
    {
        Assert.Equal(NormLoadType.Permanent, FemCombinationCoefficients.LoadType(ImpX));
        Assert.Equal(1.0, FemCombinationCoefficients.GammaF(new FemLoadCase { Sp20Type = FemSp20Types.Imperfection, GammaFUnfav = 1.3 },
            Sp20GammaDefaults.Standard));
        Assert.Equal(1.0, FemLongTermPair.Share(ImpX));

        // Длительная часть варианта N: несовершенство целиком, кратковременная — долей.
        var batch = Batch();
        batch.LongTermPairs = [FemCombinationTypes.N];
        var plans = FemCsfeaBatchLongTerm.Plans(batch, Cases);
        var minus = plans.Single(p => p.Item == 1);
        Assert.Equal(FemLongTermPairKind.Derived, minus.Pair.Kind);
        Assert.Equal(new Dictionary<int, double> { [1] = 1, [2] = 0.35, [3] = -1 }, Factors(minus.Pair.Long!.ToJson()));
        Assert.Equal(7, plans[0].Stage);   // стадии пар — после 7 вариантов

        // Только несовершенство и кратковременная без доли — длительная часть нулевая (несовершенство — не нагрузка).
        var zero = FemLongTermPair.Derive(FemLoadExpression.Parse(Expr("N", (2, 1.0), (3, 1.0))),
            [G, new FemLoadCase { Id = 2, Tag = "Q", Sp20Type = FemSp20Types.ShortTerm }, ImpX, ImpY], "N");
        Assert.Equal(FemLongTermPairKind.Zero, zero.Kind);
        // «Все загружения» несовершенства не включают и в длительной части.
        var all = FemLongTermPair.Derive(new FemLoadExpression { Mode = FemLoadExpressionMode.All }, Cases, "N");
        Assert.DoesNotContain(all.Long!.Terms, t => t.LoadCaseId is 3 or 4);
    }

    [Fact]
    public void ForceSetImperfections_SavedLoaded_AndV99Migrates()
    {
        using (var db = new DatabaseService(_path))
        {
            db.SaveForceSet(new ForceSet { Tag = "с креном", Kind = "bar", SourceType = "fea", SecondOrder = true, Imperfections = "−ImpX" });
            db.SaveForceSet(new ForceSet { Tag = "без", Kind = "bar" });
        }
        using (var again = new DatabaseService(_path))
        {
            again.LoadAll();
            Assert.Equal("−ImpX", again.ForceSets.Single(f => f.Tag == "с креном").Imperfections);
            Assert.Null(again.ForceSets.Single(f => f.Tag == "без").Imperfections);
        }
        SqliteConnection.ClearAllPools();
        using (var c = new SqliteConnection($"Data Source={_path}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "ALTER TABLE force_sets DROP COLUMN imperfections; "
                + "INSERT OR REPLACE INTO settings (key, value_json) VALUES ('schema_version', '99');";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        using var migrated = new DatabaseService(_path);
        migrated.LoadAll();
        var set = migrated.ForceSets.Single(f => f.Tag == "с креном");
        Assert.Null(set.Imperfections);
        set.Imperfections = "ImpX";
        migrated.SaveForceSet(set);
        using var reread = new DatabaseService(_path);
        reread.LoadAll();
        Assert.Equal("ImpX", reread.ForceSets.Single(f => f.Tag == "с креном").Imperfections);
    }

    [Fact]
    public void Sp20_ImperfectionsOf_CommonOrEmpty()
    {
        ForceSet S(string? imp) => new() { Kind = "bar", Imperfections = imp };
        Assert.Equal("ImpX", SP20Combinations.ImperfectionsOf([S("ImpX"), S("ImpX")], out bool mixed));
        Assert.False(mixed);
        Assert.Null(SP20Combinations.ImperfectionsOf([S("ImpX"), S("−ImpX")], out mixed));
        Assert.True(mixed);
        Assert.Null(SP20Combinations.ImperfectionsOf([S("ImpX"), S(null)], out mixed));
        Assert.True(mixed);
        Assert.Null(SP20Combinations.ImperfectionsOf([S(null), S("")], out mixed));
        Assert.False(mixed);
    }

    [Fact]
    public void BatchForceSets_CarryVariantImperfections()
    {
        var schema = new FemSchema { Id = 5, Tag = "Схема" };
        var fields = new CSfea.CScoreBridge.Structural.RcSecantStepFields
        {
            NodeIds = [], Displacements = [], ShellIds = [], ShellForces = [], ShellStates = [], ShellFlags = [],
            BeamIds = [1], BeamForces = new double[CSfea.CScoreBridge.Structural.RcSecantStepFields.BeamForceComponents], BeamFlags = [0],
        };
        var summary = new FemCsfeaResultSummary
        {
            SecondOrder = true,
            Batch =
            [
                new FemCsfeaBatchSummary { Index = 0, Tag = "C1 · ImpX+", Type = "C", Done = true, Completed = true, RecordN = 1, ImperfectionTags = "ImpX" },
                new FemCsfeaBatchSummary { Index = 1, Tag = "C1 · ImpX−", Type = "C", Done = true, Completed = true, RecordN = 2, ImperfectionTags = "−ImpX" },
                new FemCsfeaBatchSummary { Index = 2, Tag = "C2", Type = "N", Done = true, Completed = true, RecordN = 3 },
            ],
        };
        var result = FemCsfeaBatchForceSets.Build(schema, "Пакет", summary, _ => fields, ["C", "N"], [], null, [], null, default);
        var c = result.Sets.Single(s => s.Tag.Contains("(C)"));
        Assert.Equal("ImpX; −ImpX", c.Imperfections);
        Assert.True(c.SecondOrder);
        Assert.Null(result.Sets.Single(s => s.Tag.Contains("(N)")).Imperfections);
    }

    [Fact]
    public void SteelCheck_ImperfectionSets_NoteRows()
    {
        var section = FemSteelCheckParallelTests.BentChannel();
        var plain = FemSteelCheckParallelTests.Set();
        var imperfect = FemSteelCheckParallelTests.Set();
        imperfect.Id = 2;
        imperfect.Tag = "Пакет (C)";
        imperfect.SecondOrder = true;
        imperfect.Imperfections = "ImpX";
        var result = FemCheckRunner.RunPerElement(FemSteelCheckParallelTests.Check, FemSteelCheckParallelTests.Group,
            FemSteelCheckParallelTests.Scope(section.Id), [plain, imperfect], new FemPerElementInputs
            {
                BarSectionById = id => id == section.Id ? section : null,
            }, (task, sect, item) => TaskRunner.Run(task, sect, item));
        var rows = result.FemCheckRows!;
        var noted = rows.Where(r => r.WorstDescription.Contains(FemCheckRunner.ImperfectionNoteText)).ToList();
        Assert.Equal(rows.Count / 2, noted.Count);
        Assert.All(noted, r => Assert.Equal("Пакет (C)", r.ForceSetTag));
        using var data = System.Text.Json.JsonDocument.Parse(result.DataJson);
        var warnings = data.RootElement.GetProperty("summary").GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ToList();
        Assert.Contains(warnings, w => w!.Contains("строк из наборов с несовершенствами (Пакет (C))"));
    }
}
