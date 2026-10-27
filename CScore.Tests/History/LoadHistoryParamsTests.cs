using CScore.History;
using Xunit;

namespace CScore.Tests.History;

public class LoadHistoryParamsTests
{
    static LoadHistoryStageParams Load(int? part = null, double mx = -50, int steps = 2) =>
        new() { Tag = "э", ActivatePart = part, Steps = steps, Force = new StageForce { Mode = "manual", Mx = mx } };

    [Fact]
    public void Json_RoundTrip_KeepsEverything()
    {
        var p = new LoadHistoryParams
        {
            Stages = [Load(), Load(1, -80), new() { Tag = "р", Kind = StageKind.Failure, Force = new StageForce { Mode = "manual", Mx = -1 } }],
            Failure = new FailureOptions { Steps = 40, DropRatio = 0.8, CMax = 0.05, SofteningStrain = 0.001 },
            PartRoles = [new() { PartIndex = 0, Concrete = PartLimitRole.Fails, Rebar = PartLimitRole.Ignore }],
            CompareWithoutHistory = false,
            Fallback = LoadStepFallback.WithoutConcreteTension,
        };
        var q = LoadHistoryParams.Parse(p.ToJson());
        Assert.Equal(3, q.Stages.Count);
        Assert.Equal(1, q.Stages[1].ActivatePart);
        Assert.Equal(StageKind.Failure, q.Stages[2].Kind);
        Assert.Equal(0.05, q.Failure.CMax);
        Assert.Equal(PartLimitRole.Fails, q.PartRoles[0].Concrete);
        Assert.False(q.CompareWithoutHistory);
        Assert.Equal(LoadStepFallback.WithoutConcreteTension, q.Fallback);
        Assert.Contains("\"Fails\"", p.ToJson()); // перечисления — строками
    }

    [Fact]
    public void Json_RoundTrip_Hysteresis()
    {
        var p = new LoadHistoryParams
        {
            Stages = [Load()],
            Hysteresis = new CScore.Hysteresis.HysteresisOptions { Steel = CScore.Hysteresis.SteelHysteresisModel.Bilinear, R0 = 15 },
        };
        Assert.Contains("\"Bilinear\"", p.ToJson());
        var q = LoadHistoryParams.Parse(p.ToJson());
        Assert.Equal(CScore.Hysteresis.SteelHysteresisModel.Bilinear, q.Hysteresis!.Steel);
        Assert.Equal(15, q.Hysteresis.R0);
        Assert.Null(LoadHistoryParams.Parse(new LoadHistoryParams { Stages = [Load()] }.ToJson()).Hysteresis);
    }

    [Fact]
    public void Parse_EmptyOrBroken_Defaults()
    {
        Assert.Empty(LoadHistoryParams.Parse(null).Stages);
        Assert.Equal(LoadStepFallback.CurvatureControl, LoadHistoryParams.Parse("{ broken").Fallback);
    }

    [Theory]
    [InlineData(LoadHistoryParamErrors.NoStages)]
    [InlineData(LoadHistoryParamErrors.FailureNotLast)]
    [InlineData(LoadHistoryParamErrors.BadActivation)]
    [InlineData(LoadHistoryParamErrors.Steps)]
    [InlineData(LoadHistoryParamErrors.ForceMode)]
    [InlineData(LoadHistoryParamErrors.DropRatio)]
    [InlineData(LoadHistoryParamErrors.Softening)]
    [InlineData(LoadHistoryParamErrors.FailureSteps)]
    [InlineData(LoadHistoryParamErrors.CMax)]
    [InlineData(LoadHistoryParamErrors.Hysteresis)]
    public void Validate_ReportsEachError(string code)
    {
        var p = new LoadHistoryParams { Stages = [Load(), Load(1)] };
        switch (code)
        {
            case LoadHistoryParamErrors.NoStages: p.Stages.Clear(); break;
            case LoadHistoryParamErrors.FailureNotLast: p.Stages.Insert(0, new() { Kind = StageKind.Failure, Force = new StageForce { Mode = "manual", Mx = -1 } }); break;
            case LoadHistoryParamErrors.BadActivation: p.Stages[1].ActivatePart = 2; break;
            case LoadHistoryParamErrors.Steps: p.Stages[0].Steps = 0; break;
            case LoadHistoryParamErrors.ForceMode: p.Stages[0].Force.Mode = "set"; break;
            case LoadHistoryParamErrors.DropRatio: p.Failure.DropRatio = 1.0; break;
            case LoadHistoryParamErrors.Softening: p.Failure.SofteningStrain = 0; break;
            case LoadHistoryParamErrors.FailureSteps: p.Failure.Steps = 0; break;
            case LoadHistoryParamErrors.CMax: p.Failure.CMax = -0.01; break;
            case LoadHistoryParamErrors.Hysteresis: p.Hysteresis = new() { R0 = 0 }; break;
        }
        Assert.Contains(code, p.Validate(partCount: 2));
    }

    [Fact]
    public void Validate_Correct_NoErrors() =>
        Assert.Empty(new LoadHistoryParams { Stages = [Load(), Load(1)] }.Validate(2));

    [Fact]
    public void ToRoles_MapsParts()
    {
        var p = new LoadHistoryParams { PartRoles = [new() { PartIndex = 1, Concrete = PartLimitRole.Ignore, Rebar = PartLimitRole.Fails }] };
        var r = Assert.Single(p.ToRoles());
        Assert.Equal(new PartRoles(1, PartLimitRole.Ignore, PartLimitRole.Fails), r);
    }
}
