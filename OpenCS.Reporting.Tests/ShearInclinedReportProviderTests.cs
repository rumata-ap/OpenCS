using CScore;
using CScore.ParametricRc;
using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверяет структуру человекочитаемого отчёта наклонного сечения.</summary>
public sealed class ShearInclinedReportProviderTests
{
    [Fact]
    public void Build_keeps_trace_before_full_station_appendix()
    {
        var document = Build("""
            {
              "traceVersion": 1,
              "inputs": { "vy": { "b": 0.3, "h0": 0.55 } },
              "traceSteps": [{
                "stepId": "sp63.shear.vy.8.56.0.strength",
                "formulaCode": "8.56",
                "formulaLatex": "Q \\le Q_b + Q_{sw}",
                "substitutionLatexTemplate": "{applied} \\le {allowable}",
                "resultLatexTemplate": "\\eta = {ratio}",
                "status": "passed",
                "stationIndex": 0,
                "stationS": 0.0,
                "criticalC": 0.9,
                "values": {
                  "applied": { "value": 100.0, "unit": "Kilonewton" },
                  "allowable": { "value": 120.0, "unit": "Kilonewton" },
                  "ratio": { "value": 0.8334, "unit": "Unitless" }
                }
              }],
              "utilizationExact": 0.8334,
              "utilizationStatus": "ok",
              "stations": [{ "plane": "vy", "s": 0.0, "q": 100.0, "eta": 0.8334 }]
            }
            """);

        var traceIndex = document.Blocks.IndexOf(document.Blocks.OfType<ReportCalculationStep>().Single());
        var pageBreakIndex = document.Blocks.IndexOf(document.Blocks.OfType<ReportPageBreak>().Single());
        var trace = Assert.Single(document.Blocks.OfType<ReportCalculationStep>());

        Assert.True(traceIndex < pageBreakIndex);
        Assert.Equal("Кисп = 0.833", trace.Result.Source);
        Assert.DoesNotContain("η", trace.Result.Source);
        Assert.Contains(document.Blocks.OfType<ReportTable>(), table =>
            table.Headers.Contains("Кисп,Q"));
        Assert.Contains(document.Blocks.OfType<ReportKeyValueTable>(), table =>
            table.Rows.Any(row => row.Key == "Коэффициент использования" && row.Value == "0.834"));
    }

    [Fact]
    public void Build_old_json_without_trace_uses_details_fallback()
    {
        var document = Build("""
            {
              "details": [{
                "plane": "vy", "formula": "8.56", "normRef": "8.1.33",
                "applied": 100.0, "allowable": 120.0, "ratio": 0.833333,
                "passed": true, "variables": { "s": 0.0, "C": 0.9 }
              }],
              "utilization": 0.833333,
              "utilizationStatus": "ok"
            }
            """);

        Assert.Single(document.Blocks.OfType<ReportCalculationStep>());
        Assert.Contains(document.Blocks.OfType<ReportCalculationStep>(), step =>
            step.StepId == "legacy.shear.0");
    }

    [Fact]
    public void Build_usesParametricSectionScheme_WhenSourceIsAvailable()
    {
        var task = new CalcTask { Id = 1, Kind = "shear_inclined", Tag = "test" };
        var result = new CalcResult { Id = 2, TaskKind = task.Kind, DataJson = "{}", Status = "ok" };
        var definition = ParametricRcSectionDefinition.Rectangle(0.30, 0.60) with
        {
            LowerRebar = ParametricLongitudinalLayer.Physical(3, 0.016, -0.25)
        };

        var document = new ShearInclinedReportProvider().Build(new ReportContext(
            task, result, new CrossSection(), null, null, definition));

        var image = Assert.Single(document.Blocks.OfType<ReportImage>());
        Assert.Equal("Параметрическая схема поперечного сечения", image.Name);
        Assert.Contains("class=\"rebar\"", image.Svg);
    }

    static ReportDocument Build(string json)
    {
        var task = new CalcTask { Id = 1, Kind = "shear_inclined", Tag = "test" };
        var result = new CalcResult { Id = 2, TaskKind = task.Kind, DataJson = json, Status = "ok" };
        return new ShearInclinedReportProvider().Build(new ReportContext(task, result));
    }
}
