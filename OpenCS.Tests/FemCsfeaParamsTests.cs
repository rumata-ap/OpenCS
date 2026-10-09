using CScore;
using CScore.Fem;
using CSfea.Core;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Параметры секущего расчёта CSfea в постановке (4д, срез 2): JSON, умолчания ядра, перевод в опции.</summary>
public sealed class FemCsfeaParamsTests
{
    [Fact]
    public void Defaults_MatchKernelDefaults()
    {
        var options = FemCsfeaSetup.SecantOptions(new FemCsfeaParams());
        var core = new RcSecantOptions();
        var solver = new SecantPicardOptions();

        Assert.Equal(core.TensionConcrete, options.TensionConcrete);
        Assert.Equal(core.Psi, options.Psi);
        Assert.Equal(core.PlateCrackRule, options.PlateCrackRule);
        Assert.Equal(core.BeamShear, options.BeamShear);
        Assert.Equal(core.PoissonUncracked, options.PoissonUncracked);
        Assert.Equal(solver.MaxIterations, options.Solver.MaxIterations);
        Assert.Equal(solver.TolDisplacement, options.Solver.TolDisplacement);
        Assert.Equal(solver.TolStiffness, options.Solver.TolStiffness);
        Assert.Equal(solver.MaxBisections, options.Solver.MaxBisections);
        Assert.Equal(solver.Omega0, options.Solver.Omega0);
        Assert.Equal(solver.Geometric, options.Solver.Geometric);
    }

    [Fact]
    public void ToJson_Parse_RoundTripsCsfea()
    {
        var pars = new FemAnalysisParams
        {
            CalcType = CalcType.C,
            Csfea = new FemCsfeaParams
            {
                PlateRebarSource = FemCheckRebarSource.Layout, TensionConcrete = false, Psi = false,
                PlateCrackRule = PlateCrackRule.Section, BeamShear = false, PoissonUncracked = 0.2, GeomNonlinear = true,
                MaxIterations = 80, TolDisplacement = 1e-5, TolStiffness = 5e-4, MaxBisections = 6, Omega0 = 0.5,
                ResultRecording = FemCsfeaRecording.Selected, RecordSteps = "5, 10", RecordStageEnds = false,
                ControlNodeTag = "125", ControlDof = 1,
            },
        };

        string json = pars.ToJson();
        var c = FemAnalysisParams.Parse(json).Csfea;

        Assert.Contains("\"PlateCrackRule\":\"Section\"", json);
        Assert.NotNull(c);
        Assert.Equal(FemCheckRebarSource.Layout, c.PlateRebarSource);
        Assert.False(c.TensionConcrete);
        Assert.False(c.Psi);
        Assert.Equal(PlateCrackRule.Section, c.PlateCrackRule);
        Assert.False(c.BeamShear);
        Assert.Equal(0.2, c.PoissonUncracked);
        Assert.True(c.GeomNonlinear);
        Assert.Equal(80, c.MaxIterations);
        Assert.Equal(1e-5, c.TolDisplacement);
        Assert.Equal(5e-4, c.TolStiffness);
        Assert.Equal(6, c.MaxBisections);
        Assert.Equal(0.5, c.Omega0);
        Assert.Equal(FemCsfeaRecording.Selected, c.ResultRecording);
        Assert.Equal("5, 10", c.RecordSteps);
        Assert.False(c.RecordStageEnds);
        Assert.Equal("125", c.ControlNodeTag);
        Assert.Equal(1, c.ControlDof);
    }

    [Fact]
    public void Parse_WithoutCsfea_KeepsNullAndOmitsField()
    {
        var pars = FemAnalysisParams.Parse("{\"CalcType\":1,\"Stages\":[]}");

        Assert.Null(pars.Csfea);
        Assert.DoesNotContain("Csfea", new FemAnalysisParams().ToJson());
    }

    [Fact]
    public void SecantOptions_MapsAllFields()
    {
        var p = new FemCsfeaParams
        {
            TensionConcrete = true, Psi = false, PlateCrackRule = PlateCrackRule.Section, BeamShear = false,
            PoissonUncracked = 0.15, GeomNonlinear = true, MaxIterations = 30, TolDisplacement = 2e-4, TolStiffness = 2e-3,
            MaxBisections = 2, Omega0 = 0.9,
        };
        var log = new List<string>();

        var o = FemCsfeaSetup.SecantOptions(p, maxDegreeOfParallelism: 3, log: log.Add);

        Assert.True(o.TensionConcrete);
        Assert.False(o.Psi);
        Assert.Equal(PlateCrackRule.Section, o.PlateCrackRule);
        Assert.False(o.BeamShear);
        Assert.Equal(0.15, o.PoissonUncracked);
        Assert.True(o.Solver.Geometric);
        Assert.Equal(30, o.Solver.MaxIterations);
        Assert.Equal(2e-4, o.Solver.TolDisplacement);
        Assert.Equal(2e-3, o.Solver.TolStiffness);
        Assert.Equal(2, o.Solver.MaxBisections);
        Assert.Equal(0.9, o.Solver.Omega0);
        Assert.Equal(3, o.Solver.MaxDegreeOfParallelism);
        o.Solver.Log!("строка");
        Assert.Equal(["строка"], log);
    }

    [Fact]
    public void FromAnalysis_TakesRebarSourceAndBeamShear()
    {
        var pars = new FemAnalysisParams
        {
            CalcType = CalcType.C,
            Stages = [new FemAnalysisStage { Tag = "L1", LoadExpressionJson = "{\"Mode\":0,\"LoadCaseIds\":[1]}", LoadFactorStep = 0.2, MaxLoadFactor = 1 }],
            Csfea = new FemCsfeaParams { PlateRebarSource = FemCheckRebarSource.Layout, BeamShear = false },
        };
        var analysis = new FemAnalysis { Kind = "csfea_secant", Tag = "А", ParamsJson = pars.ToJson() };

        var setup = FemCsfeaSetup.FromAnalysis(analysis);

        Assert.Equal(CalcType.C, setup.Calc);
        Assert.Equal(FemCheckRebarSource.Layout, setup.PlateRebarSource);
        Assert.False(setup.BeamShear);
        Assert.Equal("L1", Assert.Single(setup.Stages).Tag);
    }

    [Fact]
    public void FromAnalysis_WithoutCsfea_UsesDefaults()
    {
        var setup = FemCsfeaSetup.FromAnalysis(new FemAnalysis { Kind = "csfea_secant", ParamsJson = "{}" });

        Assert.Equal(FemCheckRebarSource.Section, setup.PlateRebarSource);
        Assert.True(setup.BeamShear);
    }

    [Theory]
    [InlineData("", new int[0])]
    [InlineData("5,10,15", new[] { 5, 10, 15 })]
    [InlineData(" 15; 5 10,5 ", new[] { 5, 10, 15 })]
    public void ParseRecordSteps_Valid(string text, int[] expected)
    {
        var steps = FemCsfeaParams.ParseRecordSteps(text, out var error);

        Assert.Null(error);
        Assert.Equal(expected, steps);
    }

    [Theory]
    [InlineData("5,a")]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("2.5")]
    public void ParseRecordSteps_Invalid(string text)
    {
        var steps = FemCsfeaParams.ParseRecordSteps(text, out var error);

        Assert.Null(steps);
        Assert.NotNull(error);
    }
}
