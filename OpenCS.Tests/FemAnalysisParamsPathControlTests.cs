using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

public class FemAnalysisParamsPathControlTests
{
    [Fact]
    public void ToJson_RoundTrip_PreservesPathControlAndContinueWith()
    {
        var pars = new FemAnalysisParams
        {
            Stages =
            [
                new FemAnalysisStage
                {
                    Tag = "Pushover", LoadExpressionJson = "{}", LoadFactorStep = 0.1, MaxLoadFactor = 1.0,
                    PathControl = new FemAnalysisPathControl
                    {
                        Mode = "LoadControl",
                    },
                    ContinueWith = new FemAnalysisPathControl
                    {
                        Mode = "DisplacementControl",
                        ControlNodeId = 4, ControlDof = 3,
                        InitialIncrement = 0.001, MinIncrement = 0.0001, MaxIncrement = 0.01,
                        TargetDisplacement = -0.05, MaxSteps = 200
                    }
                }
            ]
        };

        var json = pars.ToJson();
        var restored = FemAnalysisParams.Parse(json);

        var stage = Assert.Single(restored.Stages);
        Assert.Equal("LoadControl", stage.PathControl!.Mode);
        Assert.Equal("DisplacementControl", stage.ContinueWith!.Mode);
        Assert.Equal(4, stage.ContinueWith.ControlNodeId);
        Assert.Equal(-0.05, stage.ContinueWith.TargetDisplacement);
    }

    [Fact]
    public void Parse_LegacyJsonWithoutPathControl_DefaultsToNull()
    {
        const string legacyJson = """{"Stages":[{"Tag":"Стадия 1","LoadExpressionJson":"{}","LoadFactorStep":0.1,"MaxLoadFactor":1.0}]}""";
        var pars = FemAnalysisParams.Parse(legacyJson);
        var stage = Assert.Single(pars.Stages);
        Assert.Null(stage.PathControl);
        Assert.Null(stage.ContinueWith);
    }
}
