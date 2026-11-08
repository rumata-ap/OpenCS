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

    /// <summary>P-Δ источника: в старом JSON поля нет — выключен; круговой JSON сохраняет; флажок меняет JSON постановки.</summary>
    [Fact]
    public void SourcePDelta_DefaultsOff_RoundTrips()
    {
        // JSON постановки до 21.10.2026 — без поля SourcePDelta.
        string oldJson = System.Text.RegularExpressions.Regex.Replace(
            new FemAnalysisParams { Csfea = new FemCsfeaParams { GeomNonlinear = true } }.ToJson(),
            @"""SourcePDelta""\s*:\s*(true|false)\s*,?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Assert.DoesNotContain("SourcePDelta", oldJson, StringComparison.OrdinalIgnoreCase);
        var old = FemAnalysisParams.Parse(oldJson);
        Assert.False(old.Csfea!.SourcePDelta);
        var pars = new FemAnalysisParams { Csfea = new FemCsfeaParams { SourcePDelta = true } };
        string json = pars.ToJson();
        Assert.True(FemAnalysisParams.Parse(json).Csfea!.SourcePDelta);
        pars.Csfea.SourcePDelta = false;
        Assert.NotEqual(json, pars.ToJson());
    }

    /// <summary>
    /// Охват второго порядка (срез 1а): старый JSON без поля — Full; LocalOnly сохраняется; «только P-δ» в опциях ядра —
    /// только при геомнелине без «Больших перемещений».
    /// </summary>
    [Fact]
    public void SecondOrderScope_DefaultsFull_MapsToLocalPDelta()
    {
        string oldJson = System.Text.RegularExpressions.Regex.Replace(
            new FemAnalysisParams { Csfea = new FemCsfeaParams { GeomNonlinear = true } }.ToJson(),
            @"""SecondOrderScope""\s*:\s*""\w+""\s*,?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Assert.DoesNotContain("SecondOrderScope", oldJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(FemCsfeaSecondOrderScope.Full, FemAnalysisParams.Parse(oldJson).Csfea!.SecondOrderScope);
        Assert.False(FemCsfeaSetup.SecantOptions(FemAnalysisParams.Parse(oldJson).Csfea).LocalPDelta);

        var local = new FemCsfeaParams { GeomNonlinear = true, SecondOrderScope = FemCsfeaSecondOrderScope.LocalOnly };
        var back = FemAnalysisParams.Parse(new FemAnalysisParams { Csfea = local }.ToJson()).Csfea!;
        Assert.Equal(FemCsfeaSecondOrderScope.LocalOnly, back.SecondOrderScope);
        Assert.True(FemCsfeaSetup.SecantOptions(back).LocalPDelta);
        local.GeomLargeDisplacements = true;
        Assert.False(FemCsfeaSetup.SecantOptions(local).LocalPDelta);
        local.GeomLargeDisplacements = false;
        local.GeomNonlinear = false;
        Assert.False(FemCsfeaSetup.SecantOptions(local).LocalPDelta);
    }

    /// <summary>
    /// Проверки «только P-δ»: без геомнелина — предупреждение; CR — ошибка; λ_cr не посчитан / &lt; 10 — предупреждение
    /// <c>fragment_pdelta_local_sway</c>, ≥ 10 — сведения; стена-трапеция — ошибка <c>fragment_pdelta_local_no_level_line</c>.
    /// </summary>
    [Fact]
    public void LocalPDeltaDiagnostics_Codes()
    {
        var p = new FemCsfeaParams { GeomNonlinear = true, SecondOrderScope = FemCsfeaSecondOrderScope.LocalOnly };
        var firstOrder = FemCsfeaRunner.LocalPDeltaDiagnostics(new FemCsfeaParams(), null, null, true);
        Assert.Contains(firstOrder, d => d.Code == "fragment_pdelta_local_first_order" && !d.IsError);
        var cr = FemCsfeaRunner.LocalPDeltaDiagnostics(new FemCsfeaParams { GeomNonlinear = true, GeomLargeDisplacements = true }, null, null, true);
        Assert.Contains(cr, d => d.Code == "fragment_pdelta_local_cr" && d.IsError);
        Assert.Contains(FemCsfeaRunner.LocalPDeltaDiagnostics(p, null, null, true), d => d.Code == "fragment_pdelta_local_sway" && !d.IsError);
        Assert.Contains(FemCsfeaRunner.LocalPDeltaDiagnostics(p, null, (6.5, "Устойчивость"), true),
            d => d.Code == "fragment_pdelta_local_sway" && d.Message.Contains("6", StringComparison.Ordinal));
        var ok = FemCsfeaRunner.LocalPDeltaDiagnostics(p, null, (12.0, "Устойчивость"), true);
        Assert.DoesNotContain(ok, d => d.Code == "fragment_pdelta_local_sway");
        Assert.Contains(ok, d => d.Code == "fragment_pdelta_local");

        // Стена-трапеция: низ 0…1 м, верх 0,25…0,75 м — у краевых узлов нижних рядов нет точки верхней линии уровня.
        var m = new RcStructuralModel();
        var sec = new RcShellSection("стена") { Elastic = new Laminate([new Ply(new OrthotropicMaterial(30e9, 30e9, 0.2, 12.5e9), 0, 0.2)]) };
        const int nx = 4, nz = 4;
        for (int k = 0; k <= nz; k++)
            for (int i = 0; i <= nx; i++)
            {
                double x0 = 0.25 * k / nz, x1 = 1 - 0.25 * k / nz;
                m.Nodes.Add(new RcNode(k * (nx + 1) + i, x0 + (x1 - x0) * i / nx, 0, 3.0 * k / nz));
            }
        for (int k = 0; k < nz; k++)
            for (int i = 0; i < nx; i++)
            {
                int a = k * (nx + 1) + i;
                m.Shells.Add(new RcShell(k * nx + i + 1, [a, a + 1, a + nx + 2, a + nx + 1], sec));
            }
        var wall = FemCsfeaRunner.LocalPDeltaDiagnostics(p, m, (12.0, "Устойчивость"), true);
        Assert.Contains(wall, d => d.Code == "fragment_pdelta_local_no_level_line" && d.IsError && d.SourceKeys is { Count: > 0 });
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
                RecordSectionStrains = true, ControlNodeTag = "125", ControlDof = 1,
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
        Assert.True(c.RecordSectionStrains);
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
    [InlineData(FemCsfeaRunner.LinearAnalysisKind, true, true, true)]     // линейный — по умолчанию включено
    [InlineData(FemCsfeaRunner.LinearAnalysisKind, true, false, false)]   // выключено в постановке
    [InlineData("csfea_secant", true, true, false)]                       // физическая нелинейность — без коэффициентов
    [InlineData("csfea_secant", false, true, true)]                       // только геомнелин — с коэффициентами
    [InlineData("csfea_secant", false, false, false)]
    public void FromAnalysis_ImportedStiffnessFactors_OnlyWhereSectionsAreElastic(
        string kind, bool physical, bool factors, bool expected)
    {
        var pars = new FemAnalysisParams
        {
            Csfea = new FemCsfeaParams { PhysicalNonlinearity = physical, ImportedStiffnessFactors = factors },
        };
        var setup = FemCsfeaSetup.FromAnalysis(new FemAnalysis { Kind = kind, Tag = "А", ParamsJson = pars.ToJson() });

        Assert.Equal(expected, setup.ReducedStiffness);
    }

    [Fact]
    public void ImportedStiffnessFactors_DefaultOn_AndKeptInJson()
    {
        Assert.True(new FemCsfeaParams().ImportedStiffnessFactors);
        var json = new FemAnalysisParams { Csfea = new FemCsfeaParams { ImportedStiffnessFactors = false } }.ToJson();
        Assert.False(FemAnalysisParams.Parse(json).Csfea!.ImportedStiffnessFactors);
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
