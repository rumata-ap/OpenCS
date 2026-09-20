using CScore;
using CScore.CalculationTrace;
using CScore.Sp63.Normal;
using System.Text.Json;
using Xunit;

namespace CScore.Tests.Sp63Normal;

/// <summary>Проверяет порядок и исходные значения трассировки нормального расчёта.</summary>
public sealed class Sp63NormalTraceTests
{
    [Fact]
    public void BendingTrace_follows_calculation_order_and_keeps_raw_values()
    {
        var result = Sp63NormalChecker.Check(
            Sp63NormalFixtures.TwoLayerRectangle(0.30, 0.60,
                tensionArea: 0.0010, compressionArea: 0.0010),
            new LoadItem { N = 0.0, Mx = -20.0 }, CalcType.C,
            Sp63NormalFixtures.MemberOptions());

        Assert.Equal(Sp63NormalStatus.Calculated, result.Status);
        Assert.Equal(
        [
            "sp63.normal.bending.compression-zone",
            "sp63.normal.bending.relative-compression-zone",
            "sp63.normal.bending.limit-comparison",
            "sp63.normal.bending.capacity",
            "sp63.normal.bending.strength"
        ], result.TraceSteps.Select(step => step.StepId));

        var x = result.TraceSteps.Single(step =>
            step.StepId == "sp63.normal.bending.compression-zone");
        Assert.Equal(result.Variables["x"], x.Values["x"].Value, precision: 12);
        Assert.Equal(CalculationUnit.Meter, x.Values["x"].Unit);
    }

    [Fact]
    public void ResultJson_exposes_versioned_trace_contract()
    {
        var json = JsonSerializer.Serialize(new Sp63NormalResult
        {
            TraceSteps =
            [new CalculationTraceStep
            {
                StepId = "sp63.normal.bending.strength",
                Values = new Dictionary<string, TraceValue>
                {
                    ["ratio"] = new(0.75, CalculationUnit.Unitless)
                }
            }]
        });

        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("traceVersion").GetInt32());
        Assert.Equal("sp63.normal.bending.strength",
            document.RootElement.GetProperty("traceSteps")[0]
                .GetProperty("stepId").GetString());
    }

    [Fact]
    public void TeeAndCircularChecks_produce_shape_specific_trace_steps()
    {
        var tee = Sp63NormalChecker.Check(
            Sp63NormalFixtures.Tee(0.4, 0.8, 2.5, 0.2, flangeOnTop: false,
                tensionY: 0.25, compressionY: -0.35,
                tensionArea: 0.012, compressionArea: 1e-12),
            new LoadItem { Mx = 1000.0 }, CalcType.C,
            Sp63NormalFixtures.MemberOptions() with
            {
                ShapeKind = Sp63NormalShapeKind.Tee
            }, spanLength: 6.3);

        var circleSection = Sp63NormalFixtures.CircleSection(0.25);
        Sp63NormalFixtures.AddPolarBars(circleSection, 8, 0.20, 3.0e-4,
            Sp63NormalFixtures.Rebar(2, 340_000.0, 340_000.0));
        var circle = Sp63NormalChecker.Check(
            circleSection,
            new LoadItem { Mx = 100.0 }, CalcType.C,
            new Sp63NormalOptions(Sp63NormalShapeKind.Circular, Sp63NormalAxis.Mx,
                new Sp63MemberContext(6.0, Sp63StructuralScheme.StaticallyIndeterminate,
                    4.2, Sp63NormalStabilityMode.SectionOnlyExplicit, 0.0)));

        Assert.Contains(tee.TraceSteps,
            step => step.StepId == "sp63.normal.tee.bending.compression-zone");
        Assert.Contains(circle.TraceSteps,
            step => step.StepId == "sp63.normal.circular_bending.resultant-moment");
    }
}
