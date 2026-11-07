using System.Text.Json;
using CScore.Fem;
using CScore.Fem.Imperfections;
using CScore.Planar;
using Xunit;

namespace CScore.Tests;

/// <summary>
/// Срез 3 (показ u₀ результата): решённые несовершенства по сохранённому отчёту (<see cref="FemImperfectionReport.Apply"/>)
/// и h по умолчанию для (5.5) в диалоге (<see cref="FemImperfectionResolver.SwaySpanZ"/>).
/// </summary>
public class FemImperfectionReportApplyTests
{
    static FemResolvedBow Bow(string label, double e0) =>
        new(label, ["1", "2"], e0, new PlanarVector3(1, 0, 0), 6) { SignAuto = true };

    static FemResolvedSway Sway(string label) => new(label, 1.0 / 200, new PlanarVector3(1, 0, 0), null);

    [Fact]
    public void Apply_TakesSignedE0_AndDropsInactive()
    {
        var items = new FemResolvedImperfection[] { Sway("Крен"), Bow("A", 0.024), Bow("B", 0.024) };
        var report = new FemImperfectionReport
        {
            Sways = [new FemSwayReportRow { Label = "Крен", Phi = 1.0 / 200 }],
            Bows =
            [
                new FemBowReportRow { Label = "A", E0 = -0.024, Active = true, Sign = "−" },
                new FemBowReportRow { Label = "B", E0 = 0, Active = false },
            ],
        };
        var applied = report.Apply(items);
        Assert.NotNull(applied);
        Assert.Equal(2, applied!.Count);
        Assert.IsType<FemResolvedSway>(applied[0]);
        var a = Assert.IsType<FemResolvedBow>(applied[1]);
        Assert.Equal("A", a.Label);
        Assert.Equal(-0.024, a.E0, 15);
        Assert.Equal(["1", "2"], a.ElemTags);
    }

    [Fact]
    public void Apply_Mismatch_ReturnsNull()
    {
        var report = new FemImperfectionReport { Bows = [new FemBowReportRow { Label = "A", E0 = 0.01 }] };
        Assert.Null(report.Apply([Bow("A", 0.01), Bow("B", 0.01)]));
        Assert.Null(report.Apply([Bow("X", 0.01)]));
        Assert.Null(report.Apply([Sway("Крен"), Bow("A", 0.01)]));
        Assert.NotNull(report.Apply([Bow("A", 0.01)]));
    }

    [Fact]
    public void SwaySpanZ_AllAndList()
    {
        // Две стойки: «C» z 0…6, «D» z 0…9 (узлы 1–3 и 11–13).
        var nodes = new List<FemMeshNode>
        {
            new() { NodeTag = "1", Z = 0 }, new() { NodeTag = "2", Z = 3 }, new() { NodeTag = "3", Z = 6 },
            new() { NodeTag = "11", X = 5, Z = 0 }, new() { NodeTag = "12", X = 5, Z = 4.5 }, new() { NodeTag = "13", X = 5, Z = 9 },
        };
        FemElement Beam(int tag, int i, int j, string member) => new()
        {
            ElemTag = tag.ToString(), ElemType = "beam", NodeIdsJson = JsonSerializer.Serialize(new[] { i, j }), SourceMemberTag = member,
        };
        var elements = new List<FemElement> { Beam(1, 1, 2, "C"), Beam(2, 2, 3, "C"), Beam(11, 11, 12, "D"), Beam(12, 12, 13, "D") };
        Assert.Equal(9, FemImperfectionResolver.SwaySpanZ(new FemSwayParams(), elements, nodes), 12);
        Assert.Equal(6, FemImperfectionResolver.SwaySpanZ(new FemSwayParams { Scope = FemSwayParams.ScopeList, MemberTags = ["C"] }, elements, nodes), 12);
        Assert.Equal(4.5, FemImperfectionResolver.SwaySpanZ(new FemSwayParams { Scope = FemSwayParams.ScopeList, ElementTags = ["12", "нет"] }, elements, nodes), 12);
        Assert.Equal(0, FemImperfectionResolver.SwaySpanZ(new FemSwayParams { Scope = FemSwayParams.ScopeList }, elements, nodes));
    }
}
