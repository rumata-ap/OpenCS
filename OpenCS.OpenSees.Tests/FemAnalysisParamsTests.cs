using CScore.Fem;
using OpenCS.Services;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public class FemAnalysisParamsTests
{
    [Fact]
    public void ToJson_Parse_RoundTripsStages()
    {
        var pars = new FemAnalysisParams
        {
            Stages =
            [
                new FemAnalysisStage { Tag = "Сжатие", LoadExpressionJson = "{\"Mode\":0,\"LoadCaseIds\":[1]}" },
                new FemAnalysisStage { Tag = "Изгиб", LoadExpressionJson = "{\"Mode\":0,\"LoadCaseIds\":[2]}" }
            ]
        };

        var parsed = FemAnalysisParams.Parse(pars.ToJson());

        Assert.Equal(2, parsed.Stages.Count);
        Assert.Equal("Сжатие", parsed.Stages[0].Tag);
        Assert.Equal("Изгиб", parsed.Stages[1].Tag);
    }

    [Fact]
    public void Batch_RoundTrips_AndIsOmittedWhenNull()
    {
        Assert.DoesNotContain("Batch", new FemAnalysisParams().ToJson());
        var pars = new FemAnalysisParams
        {
            Batch = new FemCsfeaBatch
            {
                Items =
                [
                    new FemCsfeaBatchItem { Tag = "C 001", LoadExpressionJson = "{\"Mode\":0,\"LoadCaseIds\":[1]}", Type = "C" },
                    new FemCsfeaBatchItem { Tag = "Снег", Kind = FemCsfeaBatchItemKind.LoadCase, Type = "Q" },
                ],
                LoadFactorStep = 0.2, MaxLoadFactor = 1.5, StopAtLimit = true, Recording = FemCsfeaBatchRecording.Envelopes,
            }
        };

        var batch = FemAnalysisParams.Parse(pars.ToJson()).Batch!;

        Assert.Equal(["C 001", "Снег"], batch.Items.Select(i => i.Tag));
        Assert.Equal(FemCsfeaBatchItemKind.LoadCase, batch.Items[1].Kind);
        Assert.Equal("Q", batch.Items[1].Type);
        Assert.True(batch.StopAtLimit);
        Assert.Equal(FemCsfeaBatchRecording.Envelopes, batch.Recording);
        Assert.True(batch.SameSettings(pars.Batch));
        Assert.False(batch.SameSettings(new FemCsfeaBatch()));
        var stages = batch.ToStages();
        Assert.Equal(2, stages.Count);
        Assert.Equal(batch.Items[0].LoadExpressionJson, stages[0].LoadExpressionJson);
        Assert.All(stages, s => { Assert.Equal(0.2, s.LoadFactorStep); Assert.Equal(1.5, s.MaxLoadFactor); });
    }

    [Fact]
    public void BatchItemsOnlyChanged_ListYes_SettingsOrOtherParamsNo()
    {
        static string Json(Action<FemAnalysisParams> edit)
        {
            var p = new FemAnalysisParams
            {
                Csfea = new FemCsfeaParams(),
                Batch = new FemCsfeaBatch { Items = [new FemCsfeaBatchItem { Tag = "A" }] },
            };
            edit(p);
            return p.ToJson();
        }
        string baseline = Json(_ => { });
        Assert.True(FemAnalysisParams.BatchItemsOnlyChanged(baseline, Json(p => p.Batch!.Items.Add(new FemCsfeaBatchItem { Tag = "B" }))));
        Assert.False(FemAnalysisParams.BatchItemsOnlyChanged(baseline, Json(p => p.Batch!.LoadFactorStep = 0.05)));
        Assert.False(FemAnalysisParams.BatchItemsOnlyChanged(baseline, Json(p => p.Csfea!.MaxIterations = 7)));
        Assert.False(FemAnalysisParams.BatchItemsOnlyChanged(baseline, Json(p => p.Batch = null)));
        // Флажки длительных частей — как правка списка: недостающее досчитывается, лишнее удаляется.
        Assert.True(FemAnalysisParams.BatchItemsOnlyChanged(baseline, Json(p => p.Batch!.LongTermPairs = ["N", "C"])));
        Assert.True(FemAnalysisParams.BatchItemsOnlyChanged(baseline, Json(p => p.Batch!.LongTermDeflection = false)));
    }

    [Fact]
    public void BatchLongTerm_RoundTrips_OldJsonWithoutPairs()
    {
        var pars = new FemAnalysisParams { Batch = new FemCsfeaBatch { LongTermPairs = ["N"], LongTermDeflection = false } };
        var batch = FemAnalysisParams.Parse(pars.ToJson()).Batch!;
        Assert.True(batch.HasLongTermPairs("N"));
        Assert.False(batch.HasLongTermPairs("C"));
        Assert.False(batch.HasLongTermDeflection);

        var old = FemAnalysisParams.Parse("{\"Batch\":{\"Items\":[],\"LoadFactorStep\":0.1}}").Batch!;
        Assert.Empty(old.LongTermPairs);
        Assert.False(old.HasLongTermDeflection);
    }

    [Fact]
    public void BatchLongTermEstimate_ByKindAndType()
    {
        FemLoadCase[] cases =
        [
            new() { Id = 1, Tag = "G", Sp20Type = "permanent" },
            new() { Id = 2, Tag = "Q", Sp20Type = "short_term", LongTermShare = 0.35 },
        ];
        static FemCsfeaBatchItem Comb(string tag, string type, params (int Id, double K)[] terms) => new()
        {
            Tag = tag, Type = type,
            LoadExpressionJson = new FemLoadExpression
            {
                Mode = FemLoadExpressionMode.Sum, CombinationType = type,
                Terms = terms.Select(t => new FemLoadTerm { LoadCaseId = t.Id, Coefficient = t.K }).ToList(),
            }.ToJson(),
        };
        var batch = new FemCsfeaBatch
        {
            Items =
            [
                Comb("N1", "N", (1, 1), (2, 1)),     // пара + прогиб
                Comb("N2", "N", (1, 1)),             // совпадает с полной — только прогиб
                Comb("N3", "N", (2, 0)),             // нулевая
                Comb("C1", "C", (1, 1.1), (2, 1.4)), // C не отмечен
                Comb("NL1", "NL", (1, 1)),           // пары у NL нет
                new FemCsfeaBatchItem { Tag = "G", Kind = FemCsfeaBatchItemKind.LoadCase, Type = "G" },
            ],
            LongTermPairs = ["N"],
        };
        var e = FemCsfeaBatchLongTerm.Estimate(batch, cases);
        Assert.Equal(new FemCsfeaBatchLongTermEstimate(1, 1, 1, 0, 2), e);
        Assert.Equal(3, e.Runs);

        batch.LongTermPairs = ["N", "C"];
        batch.LongTermDeflection = false;
        Assert.Equal(new FemCsfeaBatchLongTermEstimate(2, 1, 1, 0, 0),
            FemCsfeaBatchLongTerm.Estimate(batch, cases));
        Assert.False(FemCsfeaBatchLongTerm.HasCombinations(batch.Items, "CL"));
    }

    [Fact]
    public void ResolveStages_EmptyStages_SynthesizesSingleStageFromAnalysis()
    {
        var pars = new FemAnalysisParams();   // легаси-постановка: Stages пуст
        var analysis = new FemAnalysis { Tag = "Загружение 1", LoadExpressionJson = "{\"Mode\":0,\"LoadCaseIds\":[1]}" };

        var stages = pars.ResolveStages(analysis);

        var stage = Assert.Single(stages);
        Assert.Equal("Загружение 1", stage.Tag);
        Assert.Equal(analysis.LoadExpressionJson, stage.LoadExpressionJson);
        Assert.Equal(pars.LoadFactorStep, stage.LoadFactorStep);
        Assert.Equal(pars.MaxLoadFactor, stage.MaxLoadFactor);
    }

    [Fact]
    public void Parse_LegacyStagesWithoutPerStageFields_MigratesGlobalValuesIntoEachStage()
    {
        // Легаси JSON: до появления per-stage LoadFactorStep/MaxLoadFactor стадии хранили
        // только Tag/LoadExpressionJson, шаг/предел были едиными на постановку.
        const string legacyJson = """
        {
            "LoadFactorStep": 0.05,
            "MaxLoadFactor": 5.0,
            "Stages": [
                { "Tag": "Сжатие", "LoadExpressionJson": "{}" },
                { "Tag": "Изгиб", "LoadExpressionJson": "{}" }
            ]
        }
        """;

        var parsed = FemAnalysisParams.Parse(legacyJson);

        Assert.Equal(0.05, parsed.Stages[0].LoadFactorStep);
        Assert.Equal(5.0, parsed.Stages[0].MaxLoadFactor);
        Assert.Equal(0.05, parsed.Stages[1].LoadFactorStep);
        Assert.Equal(5.0, parsed.Stages[1].MaxLoadFactor);
    }

    [Fact]
    public void Parse_StagesWithOwnPerStageFields_DoesNotOverwriteThem()
    {
        const string json = """
        {
            "LoadFactorStep": 0.1,
            "MaxLoadFactor": 10.0,
            "Stages": [
                { "Tag": "Сжатие", "LoadExpressionJson": "{}", "LoadFactorStep": 0.02, "MaxLoadFactor": 2.0 }
            ]
        }
        """;

        var parsed = FemAnalysisParams.Parse(json);

        Assert.Equal(0.02, parsed.Stages[0].LoadFactorStep);
        Assert.Equal(2.0, parsed.Stages[0].MaxLoadFactor);
    }

    [Fact]
    public void ResolveStages_NonEmptyStages_ReturnsStagesAsIs()
    {
        var pars = new FemAnalysisParams { Stages = [new FemAnalysisStage { Tag = "A", LoadExpressionJson = "{}" }] };
        var analysis = new FemAnalysis { Tag = "Ignored", LoadExpressionJson = "{}" };

        var stages = pars.ResolveStages(analysis);

        var stage = Assert.Single(stages);
        Assert.Equal("A", stage.Tag);
    }

    [Fact]
    public void Parse_LegacyConcreteModel_MigratesToMainMaterialModel()
    {
        var parsed = FemAnalysisParams.Parse("{\"ConcreteModel\":\"Concrete0102\"}");

        Assert.Equal("Concrete0102", parsed.MainMaterialModel);
        Assert.DoesNotContain("ConcreteModel", parsed.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void NewParams_DefaultMainMaterialModelIsConcrete04()
    {
        var parsed = FemAnalysisParams.Parse("{}");

        Assert.Equal("Concrete04", parsed.MainMaterialModel);
    }

    [Fact]
    public void NewParams_DefaultSteelHardeningModulusIsZero()
        => Assert.Equal(0, new FemAnalysisParams().SteelHardeningModulusMpa);

    [Fact]
    public void SteelHardeningModulus_RoundTripsInJson()
    {
        var parsed = FemAnalysisParams.Parse(
            new FemAnalysisParams { SteelHardeningModulusMpa = 1.25 }.ToJson());

        Assert.Equal(1.25, parsed.SteelHardeningModulusMpa);
    }

    [Fact]
    public void LegacyJsonWithoutSteelHardeningModulusKeepsNullSentinel()
    {
        var parsed = FemAnalysisParams.Parse("{\"SteelHardeningRatioOverride\":0.01}");

        Assert.Null(parsed.SteelHardeningModulusMpa);
        Assert.Equal(0.01, parsed.SteelHardeningRatioOverride);
    }

    [Fact]
    public void ToJson_ContainsSteelHardeningModulusProperty()
    {
        string json = new FemAnalysisParams().ToJson();

        Assert.Contains("SteelHardeningModulusMpa", json, StringComparison.Ordinal);
        Assert.Contains("0", json, StringComparison.Ordinal);
    }
}
