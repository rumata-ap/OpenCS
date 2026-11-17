using System.IO;
using System.Text.Json;
using CScore;
using CScore.Sp63Punching;
using OpenCS.Cli;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Срез 1 продавливания: параметры задачи, исполнитель, эталоны Гоши через задачу, CLI.</summary>
[Collection("Loc language")]
public sealed class PunchingTaskTests
{
    /// <summary>Базовый пример книги Гоши: 250×800, h = 200, ax = 40, ay = 30, 2Ø6 A240 шаг 50, F = 600, Mx = 30, My = 20.</summary>
    static PunchingTaskParams Gosha(params PunchingEdgeParams[] edges) => new()
    {
        Area = new PunchingAreaParams { Shape = "rectangle", Bx = 0.25, By = 0.8 },
        Slab = new PunchingSlabParams { H = 0.2, Ax = 0.04, Ay = 0.03, RbtOverride = 1050 },
        Edges = [.. edges],
        Reinforcement = new PunchingReinforcementParams
        {
            Enabled = true, Diameter = 0.006, Count = 2, Step = 0.05, RswOverride = 170_000
        },
        Forces = new PunchingForceParams { F = 600, Mx = 30, My = 20 }
    };

    static CalcTask Task(PunchingTaskParams p) => new()
    {
        Id = 1, Tag = "Узел", Kind = PunchingRunner.Kind, CalcType = CalcType.C, ParamsJson = p.ToJson()
    };

    static JsonElement Detail(JsonDocument doc, string id)
        => doc.RootElement.GetProperty("details").EnumerateArray().Single(d => d.GetProperty("id").GetString() == id);

    [Fact]
    public void Params_RoundTripJson_AndDefaults()
    {
        var p = Gosha(new PunchingEdgeParams { Side = "-y", C = 0.05, AngleDeg = 10 }) with
        {
            Holes = [new PunchingHoleParams { Shape = "circle", Cx = 0.1, Cy = 1, D = 0.15 }],
            Options = new PunchingOptionParams { EccentricityX = "auto", Wb = "stress_side", PylonReduced = true, Mu = 0.008 }
        };
        var back = PunchingTaskParams.Parse(p.ToJson());
        Assert.Equal(p.ToJson(), back.ToJson());
        Assert.Contains("\"rbtOverride\"", p.ToJson());

        var d = PunchingTaskParams.Parse("{}");
        Assert.Equal("rectangle", d.Area.Shape);
        Assert.Empty(d.Edges);
        Assert.Equal(0.5 * (d.H0x + d.H0y), d.H0, 12);
        Assert.Equal(0.165, Gosha().H0, 12);
    }

    [Fact]
    public void Edges_SideDistanceAngle_GiveCoreEdges()
    {
        var p = Gosha(new PunchingEdgeParams { Side = "-y", C = 0.05 }, new PunchingEdgeParams { Side = "-x", C = 0.02 });
        var edges = p.BuildEdges();
        Assert.Equal(new PlanPoint(0, 1), edges[0].N);
        Assert.Equal(-(0.4 + 0.05), edges[0].PointOnEdge.Y, 12);
        Assert.Equal(new PlanPoint(1, 0), edges[1].N);
        Assert.Equal(-(0.125 + 0.02), edges[1].PointOnEdge.X, 12);

        // Скошенный край у круга: наименьшее расстояние от круга до края — c.
        var circle = new PunchingTaskParams
        {
            Area = new PunchingAreaParams { Shape = "circle", D = 0.4 },
            Edges = [new PunchingEdgeParams { Side = "+x", C = 0.1, AngleDeg = 30 }]
        };
        var e = circle.BuildEdges()[0];
        Assert.Equal(0.2 + 0.1, e.SignedDistance(new PlanPoint(0, 0)), 12);
        // Скошенный край у прямоугольника: ближайшая вершина на расстоянии c.
        var rect = Gosha(new PunchingEdgeParams { Side = "+y", C = 0.03, AngleDeg = 20 }).BuildEdges()[0];
        double min = new[] { new PlanPoint(-0.125, -0.4), new(0.125, -0.4), new(0.125, 0.4), new(-0.125, 0.4) }
            .Min(rect.SignedDistance);
        Assert.Equal(0.03, min, 12);
    }

    [Theory]
    [InlineData("", "closed", 0.9272591179416902)]
    [InlineData("", "pylon_broken", 1.0181060000790736)]
    [InlineData("-y", "open_edge_0", 1.2622792060118404)]
    [InlineData("-y,-x", "open_corner_0_1", 2.264830180334002)]
    public void Runner_GoshaReference_ThroughTaskParams(string sides, string variant, double expected)
    {
        var edges = sides.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => new PunchingEdgeParams { Side = s, C = 0 }).ToArray();
        var result = PunchingRunner.Run(Task(Gosha(edges)), CalcSettings.Default, null);

        using var doc = JsonDocument.Parse(result.DataJson);
        Assert.Equal(expected, Detail(doc, variant).GetProperty("ratio").GetDouble(), 9);
        Assert.Equal("not_passed", result.Status);
    }

    [Fact]
    public void Runner_DataJson_HasSceneInputsAndTrace()
    {
        var result = PunchingRunner.Run(Task(Gosha()), CalcSettings.Default, null);
        using var doc = JsonDocument.Parse(result.DataJson);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("passed").GetBoolean());
        Assert.Equal("pylon_broken", root.GetProperty("governing").GetString());
        Assert.Equal("reinforcement_required", root.GetProperty("status").GetString());
        Assert.Equal(0.165, root.GetProperty("inputs").GetProperty("h0").GetDouble(), 12);
        Assert.True(root.GetProperty("traceCommon").GetArrayLength() >= 4);
        Assert.True(Detail(doc, "closed").GetProperty("trace").GetArrayLength() > 10);

        var scene = PunchingSceneJson.Read(root.GetProperty("scene"));
        Assert.Equal(2, scene.Variants.Count);
        Assert.Single(scene.Variants, v => v.Governs);
        Assert.Equal(4, scene.AreaPolygon.Count);
    }

    [Fact]
    public void Runner_InvalidInput_ErrorWithCodes()
    {
        var p = Gosha() with { Slab = new PunchingSlabParams { H = 0.2, Ax = 0.25, Ay = 0.03, RbtOverride = 1050 } };
        var result = PunchingRunner.Run(Task(p), CalcSettings.Default, null);
        Assert.Equal("error", result.Status);
        using var doc = JsonDocument.Parse(result.DataJson);
        Assert.Contains(doc.RootElement.GetProperty("errors").EnumerateArray(), e => e.GetString() == "h0_nonpositive");

        var noConcrete = Gosha() with { Slab = new PunchingSlabParams { H = 0.2, Ax = 0.04, Ay = 0.03 } };
        var r2 = PunchingRunner.Run(Task(noConcrete), CalcSettings.Default, null);
        Assert.Equal("error", r2.Status);
        Assert.Contains("concrete_missing", r2.DataJson);
    }

    [Fact]
    public void Validate_HoleOverlap_AndOppositeEdges()
    {
        var overlap = Gosha() with { Holes = [new PunchingHoleParams { Cx = 0, Cy = 0.45, Bx = 0.2, By = 0.2 }] };
        Assert.Contains("hole_overlaps_area", overlap.Validate());
        var near = Gosha() with { Holes = [new PunchingHoleParams { Cx = 0, Cy = 0.55, Bx = 0.2, By = 0.2 }] };
        Assert.DoesNotContain("hole_overlaps_area", near.Validate());
        // Вплотную к грани (касание) — допускается: у торца пилона (y = 0,4) и у длинной грани (x = 0,125).
        var flushEnd = Gosha() with { Holes = [new PunchingHoleParams { Cx = 0, Cy = 0.5, Bx = 0.2, By = 0.2 }] };
        Assert.Empty(flushEnd.Validate());
        var flushSide = Gosha() with { Holes = [new PunchingHoleParams { Cx = 0.225, Cy = 0, Bx = 0.2, By = 0.3 }] };
        Assert.Empty(flushSide.Validate());
        Assert.Contains("edges_opposite", Gosha(new() { Side = "+x" }, new() { Side = "-x" }).Validate());
        Assert.Contains("edges_same_side", Gosha(new() { Side = "+x" }, new() { Side = "+x" }).Validate());
        Assert.Empty(Gosha(new() { Side = "+x" }, new() { Side = "-y" }).Validate());
    }

    [Fact]
    public void Strengths_FromProjectMaterials_ByCalcType()
    {
        var concrete = MaterialCatalog.CreateHeavyConcrete("B25")!;
        concrete.Id = 11;
        var rebar = MaterialCatalog.CreateRebar("А240")!;
        rebar.Id = 12;
        var p = Gosha() with
        {
            Slab = new PunchingSlabParams { H = 0.2, Ax = 0.04, Ay = 0.03, ConcreteMaterialId = 11 },
            Reinforcement = Gosha().Reinforcement with { MaterialId = 12, RswOverride = null }
        };
        var c = PunchingRunner.ResolveStrengths(p, [concrete, rebar], CalcType.C, out var errors);
        Assert.Empty(errors);
        Assert.Equal(Math.Abs(concrete.GetChars(CalcType.C)!.Ft), c.Rbt, 9);
        Assert.Equal(170_000, c.Rsw, 6);
        Assert.Equal(concrete.Tag, c.ConcreteTag);
        var cl = PunchingRunner.ResolveStrengths(p, [concrete, rebar], CalcType.CL, out _);
        Assert.True(cl.Rbt <= c.Rbt);
    }

    [Fact]
    public async Task Cli_RunTask_ReturnsPunchingData()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"opencs_punch_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string project = Path.Combine(dir, "Проект.db");
        try
        {
            TestProjectDb.Create(project);
            int taskId;
            using (var db = new DatabaseService(project))
            {
                db.LoadAll();
                var concrete = db.Materials.First(m => m.Type == MatType.Concrete);
                var p = Gosha() with { Slab = new PunchingSlabParams { H = 0.2, Ax = 0.04, Ay = 0.03, ConcreteMaterialId = concrete.Id } };
                var task = new CalcTask { Num = 2, Tag = "Продавливание", Kind = PunchingRunner.Kind, CalcType = CalcType.C, ParamsJson = p.ToJson() };
                db.SaveCalcTask(task);
                taskId = task.Id;
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            var stdout = new StringWriter();
            int code = await CliApp.RunAsync(["run", "task", project, taskId.ToString(), "--json", "--with-data", "-q"], stdout, new StringWriter());
            Assert.Equal(CliExitCode.Ok, code);
            using var json = JsonDocument.Parse(stdout.ToString());
            var item = Assert.Single(json.RootElement.EnumerateArray().ToList());
            Assert.Equal("not_passed", item.GetProperty("status").GetString());
            var data = item.GetProperty("data");
            Assert.True(data.GetProperty("ratio").GetDouble() > 1);
            Assert.Equal("pylon_broken", data.GetProperty("governing").GetString());
        }
        finally
        {
            Loc.SetLanguage(null);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
