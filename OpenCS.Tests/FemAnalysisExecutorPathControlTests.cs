using CScore.Fem;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

public class FemAnalysisExecutorPathControlTests
{
    [Fact]
    public void BuildNonlinearStages_BrokenPathControlDto_ThrowsNotSupportedExceptionWithStageTag()
    {
        var pars = new FemAnalysisParams
        {
            Stages =
            [
                new FemAnalysisStage
                {
                    Tag = "Стадия 1", LoadExpressionJson = "{}", LoadFactorStep = 0.1, MaxLoadFactor = 1.0,
                    PathControl = new FemAnalysisPathControl { Mode = "DisplacementControl" } // без обязательных полей
                }
            ]
        };
        var analysis = new FemAnalysis { Tag = "Постановка", Kind = "nonlinear", LoadExpressionJson = "{}" };

        var ex = Assert.Throws<NotSupportedException>(() =>
            FemAnalysisExecutor.BuildNonlinearStages(pars, analysis, [], [], [], []));

        Assert.Contains("Стадия 1", ex.Message);
    }

    [Fact]
    public void BuildNonlinearStages_ValidStage_ReturnsPathControlOnInput()
    {
        var pars = new FemAnalysisParams
        {
            Stages =
            [
                new FemAnalysisStage
                {
                    Tag = "Стадия 1", LoadExpressionJson = "{}", LoadFactorStep = 0.1, MaxLoadFactor = 1.0,
                    PathControl = new FemAnalysisPathControl
                    {
                        Mode = "DisplacementControl", ControlNodeId = 2, ControlDof = 3,
                        InitialIncrement = 0.001, MinIncrement = 0.0001, MaxIncrement = 0.01,
                        TargetDisplacement = -0.05, MaxSteps = 200
                    }
                }
            ]
        };
        var analysis = new FemAnalysis { Tag = "Постановка", Kind = "nonlinear", LoadExpressionJson = "{}" };

        var stages = FemAnalysisExecutor.BuildNonlinearStages(pars, analysis, [], [], [], []);

        var stage = Assert.Single(stages);
        Assert.NotNull(stage.PathControl);
        Assert.Equal(2, stage.PathControl!.DisplacementControl!.ControlNodeId);
    }
}
