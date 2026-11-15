using System.Text.Json;
using CScore;
using OpenCS.Utilites;
using OpenCS.Reporting;
using OpenCS.Tasks;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Срез 4 продавливания в задаче: крестообразная арматура рядами, п. 10.3.17, подбор, окно и отчёт (Пособие 2025, пример № 1).</summary>
[Collection("Loc language")]
public sealed class PunchingCrossTaskTests
{
    const double Tf = 9.80665;

    /// <summary>Пособие 2025, пример № 1: пилон 800×250 (наши оси), h0 = 541 мм, B25, крест 900/300 мм Ø10 A500С.</summary>
    static PunchingTaskParams Posobie(double diameter = 0.010) => new()
    {
        Area = new PunchingAreaParams { Shape = "rectangle", Bx = 0.8, By = 0.25 },
        Slab = new PunchingSlabParams { H = 0.6, Ax = 0.068, Ay = 0.050, RbtOverride = 1050 },
        Reinforcement = new PunchingReinforcementParams
        {
            Enabled = true, Diameter = diameter, Layout = "cross", RswOverride = 300_000,
            FirstRow = 0.2, RowStep = 0.15, RowCount = 6,
            CrossX = new PunchingCrossArmParams { Lines = 3, Step = 0.15 },
            CrossY = new PunchingCrossArmParams { Lines = 7, Step = 0.15 }
        },
        Forces = new PunchingForceParams { F = 286 * Tf, Mx = 1.56 * Tf, My = 2.34 * Tf, MomentsAreMloc = true },
        Options = new PunchingOptionParams { Pylon = "off" }
    };

    static CalcTask Task(PunchingTaskParams p) => new()
    {
        Id = 1, Num = 4, Tag = "Пособие № 1", Kind = PunchingRunner.Kind, CalcType = CalcType.C, ParamsJson = p.ToJson()
    };

    [Fact]
    public void Params_CrossRows_RoundTripAndCore()
    {
        var p = Posobie();
        Assert.Equal(p.ToJson(), PunchingTaskParams.Parse(p.ToJson()).ToJson());
        Assert.Equal(0.541, p.H0, 12);
        Assert.Empty(p.Validate());
        var core = p.Reinforcement.ToCore(300_000);
        Assert.Equal(0.3, core.Cross!.AlongX!.Width, 12);
        Assert.Equal(0.9, core.Cross.AlongY!.Width, 12);
        Assert.Equal(0.95, core.EffectiveZoneExtent!.Value, 12);

        var bad = p with { Reinforcement = p.Reinforcement with { CrossX = new PunchingCrossArmParams { Lines = 1 } } };
        Assert.Contains("sw_cross", bad.Validate());
        var badRows = p with { Reinforcement = p.Reinforcement with { RowCount = 0 } };
        Assert.Contains("sw_rows", badRows.Validate());
    }

    [Fact]
    public void Run_Posobie_KispDesignDetailing()
    {
        var res = PunchingRunner.Run(Task(Posobie()), CalcSettings.Default, null);
        Assert.Equal("ok", res.Status);
        using var doc = JsonDocument.Parse(res.DataJson);
        var root = doc.RootElement;
        Assert.Equal(0.86059, root.GetProperty("ratio").GetDouble(), 4);
        var design = root.GetProperty("design");
        Assert.Equal("by_threshold", design.GetProperty("state").GetString());
        Assert.Equal(0.010, design.GetProperty("bar_diameter").GetDouble(), 12);
        Assert.Equal(6, design.GetProperty("rows_req").GetInt32());
        Assert.True(design.GetProperty("provided_sufficient").GetBoolean());
        Assert.All(root.GetProperty("detailing").EnumerateArray().Where(c => c.GetProperty("code").GetString() != "anchorage"),
            c => Assert.Equal("ok", c.GetProperty("state").GetString()));
        Assert.True(root.GetProperty("scene").GetProperty("sw_cross").GetBoolean());

        var vm = new PunchingResultVM(res, Task(Posobie()));
        Assert.True(vm.HasDesign);
        Assert.Contains("Ø10", vm.DesignText);
        Assert.True(vm.HasDetailing);
        Assert.Contains("10.3.17", vm.DetailingHeader);
    }

    [Fact]
    public void ParamsVM_Design_PicksDiameter10AndSixRows()
    {
        var p = Posobie(diameter: 0.006) with { Reinforcement = Posobie().Reinforcement with { Diameter = 0.006, RowCount = 3 } };
        var vm = new PunchingParamsVM(Task(p), [], [], null);
        Assert.True(vm.IsSwCross);
        Assert.True(vm.SwByRows);
        vm.ApplyDesign();
        Assert.Equal(10, vm.Diameter, 9);
        Assert.Equal(6, vm.RowCount);
        Assert.Contains("Ø10", vm.DesignApplyText);
        Assert.Equal(Posobie().ToJson(), vm.ToParams().ToJson());
    }

    [Fact]
    public void ParamsVM_EnableRows_DefaultsFromH0()
    {
        var p = Posobie() with { Reinforcement = Posobie().Reinforcement with { Layout = "uniform", FirstRow = null, Step = 0.1, Count = 2 } };
        var vm = new PunchingParamsVM(Task(p), [], [], null);
        Assert.False(vm.SwByRows);
        vm.SwByRows = true;
        Assert.Equal(185, vm.FirstRow, 9);    // h0/3 = 180,3 → 185 мм
        Assert.Equal(180, vm.RowStep, 9);     // min(h0/3; 300) = 180,3 → 180 мм
        Assert.Equal(5, vm.RowCount);         // 185 + 4·180 = 905 ≥ 1,5h0 = 811,5
        Assert.Contains("пирамид", vm.SwText);
    }

    [Fact]
    public void Report_Cross_HasLayoutDetailingAndDesign()
    {
        var task = Task(Posobie());
        var res = PunchingRunner.Run(task, CalcSettings.Default, null);
        var doc = new PunchingReportProvider().Build(new ReportContext(task, res));
        var headings = doc.Blocks.OfType<ReportHeading>().Select(h => h.Text).ToList();
        Assert.Contains(headings, h => h.Contains("10.3.17"));
        Assert.Contains("Подбор поперечной арматуры", headings);
        var steps = doc.Blocks.OfType<ReportCalculationStep>().Select(s => s.StepId).ToList();
        Assert.Contains(steps, s => s.EndsWith(".lswx"));
        Assert.Contains(steps, s => s.EndsWith(".wswy"));
        Assert.Contains(doc.Blocks.OfType<ReportParagraph>(), p => p.Text.Contains("Ø10"));
    }
}
