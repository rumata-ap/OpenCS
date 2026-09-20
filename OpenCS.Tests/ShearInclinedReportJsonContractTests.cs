using System.Text.Json;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Проверяет версионированный trace в JSON наклонного расчёта.</summary>
public sealed class ShearInclinedReportJsonContractTests
{
    [Fact]
    public void Run_writes_versioned_trace_with_station_identity()
    {
        var result = new ShearInclinedHandler().Run(
            new CScore.CalcTask
            {
                Id = 1,
                Kind = "shear_inclined",
                CalcType = CScore.CalcType.C,
                ParamsJson = new ShearInclinedParams
                {
                    Planes = "vy",
                    ConstructiveRequirements103Confirmed = true
                }.ToJson()
            },
            ShearInclinedFixtures.Beam(),
            new CScore.LoadItem { Vy = 150.0, Mx = -120.0 },
            CalcSettings.Default);

        using var json = JsonDocument.Parse(result.DataJson);
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("traceVersion").GetInt32());
        var steps = root.GetProperty("traceSteps").EnumerateArray().ToList();
        Assert.NotEmpty(steps);
        Assert.All(steps, step =>
        {
            Assert.StartsWith("sp63.shear.", step.GetProperty("stepId").GetString());
            Assert.True(step.TryGetProperty("stationIndex", out _));
            Assert.True(step.TryGetProperty("formulaCode", out _));
            Assert.True(step.TryGetProperty("criticalC", out _));
        });
    }
}
