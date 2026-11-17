using System.Text.Json;
using CScore;
using CScore.Sp63Punching;
using OpenCS.Reporting;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Капитель в задаче продавливания: параметры, проверка ввода, результат, отчёт.</summary>
[Collection("Loc language")]
public sealed class PunchingCapitalTaskTests
{
    /// <summary>Колонна 400×400, h = 200, ax = ay = 30 (h0 = 170), капитель 1200×1200, hc = 150, F = 900.</summary>
    static PunchingTaskParams Node(PunchingCapitalParams? cap = null) => new()
    {
        Area = new PunchingAreaParams { Shape = "rectangle", Bx = 0.4, By = 0.4 },
        Slab = new PunchingSlabParams { H = 0.2, Ax = 0.03, Ay = 0.03, RbtOverride = 1050 },
        Forces = new PunchingForceParams { F = 900 },
        Capital = cap ?? new PunchingCapitalParams { Enabled = true, Bx = 1.2, By = 1.2, Hc = 0.15 },
    };

    static CalcTask Task(PunchingTaskParams p) => new()
    {
        Id = 1, Tag = "Узел с капителью", Kind = PunchingRunner.Kind, CalcType = CalcType.C, ParamsJson = p.ToJson()
    };

    [Fact]
    public void Params_RoundTrip_OldJsonWithoutCapital()
    {
        var p = Node(new PunchingCapitalParams { Enabled = true, Bx = 1.6, By = 1.2, Hc = 0.15, Dx = 0.2 });
        var back = PunchingTaskParams.Parse(p.ToJson());
        Assert.Equal(p.ToJson(), back.ToJson());
        var input = back.ToInput(1050, 0);
        Assert.NotNull(input.Capital);
        Assert.Equal(0.2, input.Capital!.Offset.X, 12);
        Assert.Equal(0.2, input.H);

        var old = PunchingTaskParams.Parse("{\"area\":{\"shape\":\"rectangle\",\"bx\":0.4,\"by\":0.4}}");
        Assert.False(old.Capital.Enabled);
        Assert.Null(old.ToInput(1050, 0).Capital);
    }

    [Fact]
    public void Validate_CapitalErrors()
    {
        Assert.Empty(Node().Validate());
        Assert.Contains("capital_contains", Node(new PunchingCapitalParams { Enabled = true, Bx = 0.3, By = 1.2, Hc = 0.15 }).Validate());
        Assert.Contains("capital_values", Node(new PunchingCapitalParams { Enabled = true, Bx = 1.2, By = 1.2, Hc = 0 }).Validate());
        var wall = Node() with { Wall = new PunchingWallParams { Kind = "end", A1 = 0.2 } };
        Assert.Contains("capital_wall", wall.Validate());
        // Выключенная капитель ничего не проверяет.
        Assert.Empty(Node(new PunchingCapitalParams { Enabled = false, Bx = 0, Hc = 0 }).Validate());
    }

    [Fact]
    public void RoundColumn_GetsRoundCapital()
    {
        var p = Node(new PunchingCapitalParams { Enabled = true, D = 1.2, Hc = 0.15, Dx = 0.3 }) with
        {
            Area = new PunchingAreaParams { Shape = "circle", D = 0.4 }
        };
        Assert.Empty(p.Validate());
        var cap = p.ToInput(1050, 0).Capital!;
        Assert.IsType<PunchingLoadArea.Circle>(cap.Area);
        Assert.Equal(default, cap.Offset);
    }

    [Fact]
    public void Runner_TwoChecks_InDataJson_AndReport()
    {
        var result = PunchingRunner.Run(Task(Node()), CalcSettings.Default, null);
        Assert.Equal("ok", result.Status);
        using var doc = JsonDocument.Parse(result.DataJson);
        var details = doc.RootElement.GetProperty("details").EnumerateArray().ToList();
        var col = details.Single(d => d.GetProperty("id").GetString() == "closed");
        var cap = details.Single(d => d.GetProperty("id").GetString() == "cap_closed");
        Assert.Equal(0.32, col.GetProperty("h0").GetDouble(), 12);
        Assert.Equal(0.17, cap.GetProperty("h0").GetDouble(), 12);
        Assert.Equal("BeyondCapital", cap.GetProperty("zone").GetString());
        Assert.Equal(0.15, doc.RootElement.GetProperty("inputs").GetProperty("capitalHc").GetDouble(), 12);
        Assert.True(doc.RootElement.GetProperty("scene").TryGetProperty("capital", out _));

        var data = PunchingReportData.Parse(result.DataJson);
        Assert.Contains(data.Variants, v => v.Zone == PunchingZone.BeyondCapital);
        Assert.Contains("за капителью", PunchingReportText.VariantTitle(PunchingVariantKind.Closed, PunchingZone.BeyondCapital, "cap_closed"));
    }
}
