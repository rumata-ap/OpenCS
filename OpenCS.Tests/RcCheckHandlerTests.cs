using System.Text.Json;
using CScore;
using CScore.Fem;
using OpenCS.Tasks;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Проверка «rc_check» стержня схемы МКЭ — прочность по НДМ по каждой строке.
/// Регрессия 26.09.2026: обработчика не было, проверка группы показывала «24 прошло»
/// с Кисп = 0,000, хотя ни одна строка не считалась.
/// </summary>
public sealed class RcCheckHandlerTests
{
    // Балка 300×600, 2Ø28 A500 понизу (y = −0,25): растяжение низа — отрицательный Mx.
    // M_u ≈ Rs·As·z ≈ 435 000 · 0,001232 · 0,5 ≈ 270 кН·м.

    [Fact]
    public void RegisteredInTaskRunner() => Assert.Contains("rc_check", TaskRunner.KindList);

    [Fact]
    public void ModerateMomentPassesWithUtilizationBelowOne()
    {
        var r = Run(mx: -100.0);

        Assert.Equal("ok", r.Status);
        using var doc = JsonDocument.Parse(r.DataJson);
        double u = doc.RootElement.GetProperty("utilization").GetDouble();
        Assert.InRange(u, 0.01, 1.0);
        Assert.True(doc.RootElement.GetProperty("passed").GetBoolean());
        Assert.Equal(2, doc.RootElement.GetProperty("details").GetArrayLength());
    }

    [Fact]
    public void MomentAboveCapacityFails()
    {
        var r = Run(mx: -600.0);

        Assert.Contains(r.Status, new[] { "not_passed", "not_converged" });
        using var doc = JsonDocument.Parse(r.DataJson);
        Assert.False(doc.RootElement.GetProperty("passed").GetBoolean());
    }

    [Fact]
    public void FemCheckRunnerCountsRealRcCheckRows()
    {
        var check  = new FemCheck { SchemaId = 1, ElementId = 7, NormCode = "rc_check", Tag = "rc" };
        var member = new FemMember { Id = 7, SchemaId = 1, ElemTag = "7", ElemType = "beam" };
        var fs = new ForceSet
        {
            Id = 1, Tag = "Балка — РСУ (C)",
            Items = [new LoadItem { Label = "мал", Mx = -100 }, new LoadItem { Label = "вел", Mx = -600 }]
        };

        var result = FemCheckRunner.RunMulti(check, member, ReportFixtures.BuildBeam(), null, [fs],
            (task, sect, item) => TaskRunner.Run(task, sect, item));

        using var doc = JsonDocument.Parse(result.DataJson);
        Assert.Equal(1, doc.RootElement.GetProperty("passedRows").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("failedRows").GetInt32());
        Assert.Equal("not_passed", result.Status);
    }

    static CalcResult Run(double mx) => TaskRunner.Run(
        new CalcTask { Kind = "rc_check", Tag = "rc", CalcType = CalcType.C },
        ReportFixtures.BuildBeam(),
        new LoadItem { Label = "1", Mx = mx });
}
