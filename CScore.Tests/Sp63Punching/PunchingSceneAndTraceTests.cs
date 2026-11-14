using System.Text.Json;
using System.Text.RegularExpressions;
using CScore.Sp63Punching;
using Xunit;

namespace CScore.Tests.Sp63Punching;

/// <summary>Срез 1: сцена для рисунка и трассировка с полной численной подстановкой.</summary>
public class PunchingSceneAndTraceTests
{
    // Базовый пример Гоши: 250×800, h0 = 165, B25, 2Ø6 A240 с шагом 50, F = 600, Mx = 30, My = 20.
    static PunchingInput Gosha(IReadOnlyList<PunchingSlabEdge>? edges = null, IReadOnlyList<PunchingHole>? holes = null)
        => new(new PunchingLoadArea.Rectangle(0.25, 0.8), 0.165, 1050,
            new PunchingForces(600, 30, 20),
            new PunchingShearReinforcement(170_000, 2 * Math.PI * 0.006 * 0.006 / 4, 0.05),
            edges, holes);

    [Fact]
    public void Scene_FromResult_RoundTripsThroughJson()
    {
        var input = Gosha(holes: [new PunchingHole([new(-0.1, 0.8), new(0.1, 0.8), new(0.1, 1.0), new(-0.1, 1.0)])]);
        var result = PunchingChecker.Check(input);
        var scene = PunchingScene.From(input, result);

        using var doc = JsonDocument.Parse(PunchingSceneJson.ToJson(scene));
        var back = PunchingSceneJson.Read(doc.RootElement);

        Assert.Equal(scene.AreaPolygon, back.AreaPolygon);
        Assert.Single(back.Holes);
        Assert.Equal(scene.Variants.Count, back.Variants.Count);
        for (int i = 0; i < scene.Variants.Count; i++)
        {
            var a = scene.Variants[i];
            var b = back.Variants[i];
            Assert.Equal(a.Id, b.Id);
            Assert.Equal(a.Kind, b.Kind);
            Assert.Equal(a.Governs, b.Governs);
            Assert.Equal(a.Utilization, b.Utilization, 12);
            Assert.Equal(a.Pieces.Sum(p => p.Sum(e => e.Length)), b.Pieces.Sum(p => p.Sum(e => e.Length)), 12);
            Assert.Equal(a.Excluded.Sum(e => e.Length), b.Excluded.Sum(e => e.Length), 12);
        }
        Assert.Contains(back.Variants, v => v.Excluded.Count > 0);
        Assert.Single(back.Variants, v => v.Governs);
    }

    [Fact]
    public void Scene_CircleKeepsDiameter_EdgesKeepNormal()
    {
        var input = new PunchingInput(new PunchingLoadArea.Ring(0.5, 0.1), 0.18, 1050, new PunchingForces(100, 0, 0),
            Edges: [new PunchingSlabEdge(new PlanPoint(0, -0.3), new PlanPoint(0, 2))]);
        var scene = PunchingScene.From(input, PunchingChecker.Check(input));
        using var doc = JsonDocument.Parse(PunchingSceneJson.ToJson(scene));
        var back = PunchingSceneJson.Read(doc.RootElement);
        Assert.Equal(0.5, back.AreaDiameter);
        Assert.Equal(0.3, back.AreaInnerDiameter!.Value, 12);
        Assert.Empty(back.AreaPolygon);
        Assert.Equal(new PlanPoint(0, 1), back.Edges[0].InwardNormal);
        var (xmin, xmax, _, _) = back.Bounds();
        Assert.True(xmin <= -0.25 && xmax >= 0.25);
    }

    [Fact]
    public void Trace_EveryStepSubstitutesAllTokens_AndResultIsSingleValue()
    {
        var result = PunchingChecker.Check(Gosha());
        var token = new Regex(@"\{(?<n>[A-Za-z0-9_]+)\}");
        foreach (var v in result.Variants)
            foreach (var s in v.Trace)
            {
                foreach (Match m in token.Matches(s.SubstitutionLatexTemplate ?? ""))
                    Assert.True(s.Values.ContainsKey(m.Groups["n"].Value), $"{s.StepId}: нет значения {m.Value}");
                var r = token.Match(s.ResultLatexTemplate ?? "");
                Assert.True(r.Success && s.Values.ContainsKey(r.Groups["n"].Value), s.StepId);
                Assert.StartsWith(s.FormulaLatex!.Split('=')[0].Trim(), s.FormulaLatex);
                Assert.Contains("=", s.FormulaLatex);
            }
    }

    [Fact]
    public void Trace_Closed_FbStepReproducesProduct_AndCheckEqualsUtilization()
    {
        var result = PunchingChecker.Check(Gosha());
        var v = result.Variants.Single(x => x.Variant.Id == "closed");
        var fb = v.Trace.Single(s => s.StepId == "sp63.punching.closed.fb");
        Assert.Equal(fb.Values["Rbt"].Value * fb.Values["u"].Value * fb.Values["h0"].Value, fb.Values["Fb"].Value, 9);
        var u = v.Trace.Single(s => s.StepId == "sp63.punching.closed.u");
        Assert.Equal(4, u.Values.Count(kv => kv.Key.StartsWith('L')));
        var check = v.Trace.Single(s => s.StepId == "sp63.punching.closed.check");
        Assert.Equal(v.Utilization, check.Values["K"].Value, 12);
        Assert.Equal("(8.96)", check.FormulaCode);
        // Режим Loading: M*x = |Mx| + F·|ey|.
        var mx = v.Trace.Single(s => s.StepId == "sp63.punching.closed.mx");
        Assert.Equal(mx.Values["M0Abs"].Value + mx.Values["F"].Value * mx.Values["eAbs"].Value, mx.Values["M"].Value, 9);
        Assert.Contains(v.Trace, s => s.StepId.EndsWith(".fult", StringComparison.Ordinal));
    }

    [Fact]
    public void Trace_NoReinforcement_NoSwSteps()
    {
        var input = Gosha() with { Reinforcement = null };
        var v = PunchingChecker.Check(input).Variants.Single(x => x.Variant.Id == "closed");
        Assert.DoesNotContain(v.Trace, s => s.StepId.EndsWith(".fsw", StringComparison.Ordinal));
        Assert.DoesNotContain(v.Trace, s => s.StepId.EndsWith(".fult", StringComparison.Ordinal));
        Assert.Equal("(8.95)", v.Trace[^1].FormulaCode);
    }
}
