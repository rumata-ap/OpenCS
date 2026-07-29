using CScore.Fem;
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
}
