using System.Text.Json;
using CScore;
using CScore.Sp63Punching;
using OpenCS.Reporting;
using OpenCS.Tasks;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Срез 5б продавливания: узлы стен (СП 430 п. 6.3.8) в задаче, пакете, отчёте и окне параметров.</summary>
[Collection("Loc language")]
public sealed class PunchingWallTaskTests
{
    /// <summary>Торец стены 200 мм, h = 220, ax = ay = 40 (h0 = 180), Rbt = 1050, F = 150, ΔM = 10.</summary>
    static PunchingTaskParams End(params PunchingEdgeParams[] edges) => new()
    {
        Wall = new PunchingWallParams { Kind = "end", A1 = 0.2 },
        Slab = new PunchingSlabParams { H = 0.22, Ax = 0.04, Ay = 0.04, RbtOverride = 1050 },
        Edges = [.. edges],
        Forces = new PunchingForceParams { F = 150, Mx = 10, My = 7 }
    };

    static PunchingTaskParams Corner() => End() with
    {
        Wall = new PunchingWallParams { Kind = "corner", A1 = 0.2, A2 = 0.25 },
        Forces = new PunchingForceParams { F = 200, Mx = 40, My = 30 }
    };

    static CalcTask Task(PunchingTaskParams p, string kind = PunchingRunner.Kind) => new()
    {
        Id = 5, Num = 2, Tag = "Торец Ст-1", Kind = kind, CalcType = CalcType.C, ParamsJson = p.ToJson()
    };

    [Fact]
    public void Params_RoundTrip_OldJsonIsColumn()
    {
        var p = Corner();
        Assert.Equal(p.ToJson(), PunchingTaskParams.Parse(p.ToJson()).ToJson());
        var old = PunchingTaskParams.Parse("{\"area\":{\"shape\":\"rectangle\",\"bx\":0.4,\"by\":0.4}}");
        Assert.Equal("none", old.Wall.Kind);
        Assert.Null(old.ToInput(1050, 0).Wall);
    }

    [Fact]
    public void ToInput_EndWithEdgeBehind_GivesContourOnePrime()
    {
        // Край за торцом на c = 50 мм — сторона +x узла торца.
        var p = End(new PunchingEdgeParams { Side = "+x", C = 0.05 });
        Assert.Empty(p.Validate());
        var input = p.ToInput(1050, 0);
        Assert.Equal(PunchingWallKind.End, input.Wall!.Kind);
        Assert.Equal(0.05, input.Edges![0].PointOnEdge.X, 12);
        var r = PunchingChecker.Check(input);
        Assert.Contains(r.Variants, v => v.Variant.Id == "wall_end_edge_0");
        Assert.Equal(2 * (0.2 + 0.05), r.Variants.Single(v => v.Variant.Id == "wall_end_edge_0").Geometry.U, 12);
    }

    [Fact]
    public void Validate_WallSizeAndEdgeSides()
    {
        Assert.Contains("wall_size", (End() with { Wall = new PunchingWallParams { Kind = "end", A1 = 0 } }).Validate());
        Assert.Contains("wall_size", (Corner() with { Wall = new PunchingWallParams { Kind = "corner", A1 = 0.2, A2 = 0 } }).Validate());
        // У торца край позади стены (−x) не задаётся; у угла — только снаружи (−x, −y).
        Assert.Contains("wall_edge_side", End(new PunchingEdgeParams { Side = "-x", C = 0.1 }).Validate());
        Assert.Contains("wall_edge_side", (Corner() with { Edges = [new PunchingEdgeParams { Side = "+x", C = 0.1 }] }).Validate());
        Assert.Empty((Corner() with { Edges = [new() { Side = "-x", C = 0.1 }, new() { Side = "-y", C = 0.05 }] }).Validate());
        // Отверстие внутри полки Г — пересечение с площадью.
        var hole = new PunchingHoleParams { Shape = "rectangle", Cx = 0.1, Cy = 0.4, Bx = 0.05, By = 0.05 };
        Assert.Contains("hole_overlaps_area", (Corner() with { Holes = [hole] }).Validate());
        Assert.DoesNotContain("hole_overlaps_area", (Corner() with { Holes = [hole with { Cx = 0.5, Cy = 0.5 }] }).Validate());
    }

    [Fact]
    public void Run_End_HalfDeltaM_TraceAndReport()
    {
        var task = Task(End());
        var result = PunchingRunner.Run(task, CalcSettings.Default, null);
        using var doc = JsonDocument.Parse(result.DataJson);
        var root = doc.RootElement;
        Assert.Equal("end", root.GetProperty("inputs").GetProperty("wall").GetString());
        var detail = root.GetProperty("details").EnumerateArray().Single(d => d.GetProperty("id").GetString() == "wall_end");
        // K = F/Fb + ½ΔM/Mbx — ручной расчёт ядра (PunchingWallTests.End_HandCalc_HalfDeltaMOnly): 0,8532.
        Assert.Equal(0.8532, detail.GetProperty("ratio").GetDouble(), 4);
        Assert.DoesNotContain(root.GetProperty("traceCommon").EnumerateArray(), s => s.GetProperty("stepId").GetString()!.StartsWith("sp63.punching.mloc"));

        var report = new PunchingReportProvider().Build(new ReportContext(task, result));
        var texts = report.Blocks.Select(b => b switch { ReportHeading h => h.Text, ReportParagraph p => p.Text, _ => "" }).ToList();
        Assert.Contains(texts, t => t.StartsWith("П-образный контур у торца стены", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.Contains("½ΔM", StringComparison.Ordinal));
        var wallStep = report.Blocks.OfType<ReportCalculationStep>().Single(s => s.StepId == "sp63.punching.wall_end.wall");
        Assert.Equal("Усилия у узла стены (СП 430, п. 6.3.8)", wallStep.Title);
        Assert.Contains("10", wallStep.Formula.Source);
        var table = report.Blocks.OfType<ReportTable>().First();
        Assert.Contains(table.Rows, r => r[0] == "ΔM");
        Assert.DoesNotContain(table.Rows, r => r[0] == "b_x");
    }

    [Fact]
    public void Run_Corner_ForceOnly()
    {
        var task = Task(Corner());
        var result = PunchingRunner.Run(task, CalcSettings.Default, null);
        using var doc = JsonDocument.Parse(result.DataJson);
        var detail = doc.RootElement.GetProperty("details").EnumerateArray().Single(d => d.GetProperty("id").GetString() == "wall_corner");
        double u = 2 * (0.09 + 0.45) + Math.Sqrt(0.2 * 0.2 + 0.25 * 0.25);
        Assert.Equal(200 / (1050 * u * 0.18), detail.GetProperty("ratio").GetDouble(), 9);
        var report = new PunchingReportProvider().Build(new ReportContext(task, result));
        Assert.Contains(report.Blocks.OfType<ReportHeading>(), h => h.Text.StartsWith("Контур у угла стен (СП 430, рис. 6.2)", StringComparison.Ordinal));
    }

    [Fact]
    public void Batch_End_RowMxIsDeltaM_WarningOnce()
    {
        var p = End();
        var s = new PunchingStrengths(1050, 0, "", "", []);
        LoadItem[] items =
        [
            new() { Id = 1, Num = 1, Label = "1", N = -150, Mx = 10, My = 7 },
            new() { Id = 2, Num = 2, Label = "2", N = -100, Mx = -20, My = 3 },
        ];
        var outcome = PunchingBatchRunner.Evaluate(p, s, items);
        var single = PunchingChecker.Check(PunchingBatchRunner.RowParams(p, items[0]).ToInput(1050, 0), includeTrace: false);
        Assert.Equal(single.Utilization, outcome.Rows[0].Result.Utilization, 12);
        Assert.Single(outcome.Warnings, w => w.Code == "wall_batch_end_moments");

        var corner = PunchingBatchRunner.Evaluate(Corner(), s, items);
        Assert.Single(corner.Warnings, w => w.Code == "wall_batch_corner_moments");
        Assert.Equal("СП 430.1325800, п. 6.3.8, рис. 6.2 (ред. с Изм. № 1)", PunchingBatchRunner.Reference(PunchingVariantKind.WallCorner, PunchingZone.Main));
    }

    [Fact]
    public void Vm_NodeKindSwitch_SidesAndFieldsFollow()
    {
        var p = End() with { Area = new PunchingAreaParams { Bx = 0.2, By = 1.0 } };
        var vm = new PunchingParamsVM(Task(p with { Wall = new PunchingWallParams() }), [], [], null);
        Assert.True(vm.IsColumnNode);
        Assert.True(vm.ShowWallHint);          // 1000/200 = 5 > 4 — по п. 5.1.8 это стена
        Assert.Equal(4, vm.Sides.Count);

        vm.Position = "edge";
        vm.Edge1Side = "-x";
        vm.NodeKind = "end";
        Assert.True(vm.IsWallEnd);
        Assert.False(vm.ShowWallHint);
        Assert.False(vm.ShowMy);
        Assert.Equal("ΔM", vm.MxLabel);
        Assert.Equal(["+x", "+y", "-y"], vm.Sides.Select(s => s.Id));
        Assert.Equal("+x", vm.Edge1Side);      // −x за стеной недопустим — край перенесён за торец
        vm.WallA1 = 200;
        vm.Edge1C = 50;
        Assert.False(vm.HasErrors, vm.ErrorText);
        Assert.Contains(vm.Preview!.Rows, r => r.Id == "wall_end_edge_0");

        // Помощник ΔM: моменты по сторонам стены.
        vm.WallMPlus = 25;
        vm.WallMMinus = -5;
        Assert.Equal(30, vm.Mx, 12);

        var back = vm.ToParams();
        Assert.Equal("end", back.Wall.Kind);
        Assert.Equal(0.2, back.Wall.A1, 12);

        vm.NodeKind = "corner";
        Assert.False(vm.ShowMx);
        Assert.Equal(["-x", "-y"], vm.Sides.Select(s => s.Id));
        Assert.Equal("-x", vm.Edge1Side);
        Assert.False(vm.HasErrors, vm.ErrorText);
    }

    /// <summary>
    /// Ручная проверка 16.11: у загруженного угла без краёв «Положение → у края» давало пустую сторону и неактивное
    /// «Сохранить» — сторона по умолчанию (+Y) у угла недопустима. Стороны нормализуются при загрузке и смене положения.
    /// </summary>
    [Fact]
    public void Vm_LoadedWallNode_EdgeSidesValidOnPositionChange()
    {
        var vm = new PunchingParamsVM(Task(Corner()), [], [], null);
        Assert.Equal("inside", vm.Position);
        vm.Position = "edge";
        Assert.Equal("-x", vm.Edge1Side);
        Assert.Contains(vm.Sides, s => s.Id == vm.Edge1Side);
        vm.Edge1C = 50;
        Assert.False(vm.HasErrors, vm.ErrorText);

        vm.Position = "corner";
        Assert.Equal("-y", vm.Edge2Side);
        vm.Edge2C = 50;
        Assert.False(vm.HasErrors, vm.ErrorText);
        vm.Edge1Side = "-y";                   // второй край уходит на соседнюю допустимую сторону
        Assert.Equal("-x", vm.Edge2Side);
        Assert.False(vm.HasErrors, vm.ErrorText);
        Assert.Equal(["-y", "-x"], vm.ToParams().Edges.Select(e => e.Side));

        var end = new PunchingParamsVM(Task(End()), [], [], null);
        end.Position = "corner";
        Assert.Equal("+x", end.Edge1Side);     // у торца — край за торцом (контур 1′)
        Assert.Contains(end.Edge2Side, new[] { "+y", "-y" });
    }

    /// <summary>У торца My не участвует, у угла — моменты вовсе: в таблице вариантов и сводке отчёта «—», а не нули.</summary>
    [Fact]
    public void ResultAndReport_WallVariants_DashForUnusedMoments()
    {
        foreach (var (p, noMx) in new[] { (End(), false), (Corner(), true) })
        {
            var task = Task(p);
            var result = PunchingRunner.Run(task, CalcSettings.Default, null);
            var vm = new PunchingResultVM(result, task);
            Assert.NotEmpty(vm.Rows);
            Assert.All(vm.Rows, r =>
            {
                Assert.Equal("—", r.Myult);
                Assert.Equal("—", r.MyStar);
                Assert.Equal(noMx, r.Mxult == "—");
                Assert.Equal(noMx, r.MxStar == "—");
                Assert.Equal(noMx, r.KM == "—");
            });
            var report = new PunchingReportProvider().Build(new ReportContext(task, result));
            var summary = report.Blocks.OfType<ReportTable>().Single(t => t.Headers.Contains("M*y, кН·м"));
            int my = summary.Headers.ToList().IndexOf("M*y, кН·м"), mx = summary.Headers.ToList().IndexOf("M*x, кН·м");
            Assert.All(summary.Rows, r => Assert.Equal("—", r[my]));
            Assert.All(summary.Rows, r => Assert.Equal(noMx, r[mx] == "—"));
        }
    }
}
