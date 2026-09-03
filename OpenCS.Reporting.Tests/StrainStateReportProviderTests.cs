using CScore;
using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

public sealed class StrainStateReportProviderTests
{
    [Fact]
    public void Provider_BuildsSp63SectionsAndFormulas()
    {
        var task = new CalcTask { Id = 1, Kind = "strain_state", Tag = "Колонна", CalcType = CalcType.C };
        var result = new CalcResult
        {
            TaskId = task.Id,
            TaskKind = task.Kind,
            TaskTag = task.Tag,
            Status = "ok",
            DataJson = """
                {
                  "converged": true, "iterations": 4, "residual": 0.01,
                  "e0": -0.0002, "ky": 0.001, "kz": -0.002,
                  "N_target": 100, "Mx_target": 20, "My_target": -30,
                  "N_result": 100, "Mx_result": 20, "My_result": -30,
                  "formula_version": "SP63.13330.2021/8.1",
                  "stiffness": { "source": "contour", "d11": 11, "d12": 12, "d13": 13, "d21": 12, "d22": 22, "d23": 23, "d31": 13, "d32": 23, "d33": 33 },
                  "jacobian": { "rows": ["N", "Mx", "My"], "columns": ["e0", "ky", "kz"], "scheme": "central", "h": 0.0000001, "values": [[1,2,3],[4,5,6],[7,8,9]] },
                  "equilibrium": { "n": 100, "mx": 20, "my": -30 },
                  "extrema": { "eps_b_min": -0.001, "eps_b_max": 0.002, "eps_s_min": -0.003, "eps_s_max": 0.004 }
                }
                """
        };

        var document = new StrainStateReportProvider().Build(
            new ReportContext(task, result, new Dictionary<string, string>
            {
                ["strain"] = "<svg data-kind=\"strain\"></svg>",
                ["stress"] = "<svg data-kind=\"stress\"></svg>"
            }));

        var headings = document.Blocks.OfType<ReportHeading>().Select(x => x.Text).ToList();
        var formulas = document.Blocks.OfType<ReportFormula>().Select(x => x.Reference).ToList();

        Assert.Contains("Исходные данные", headings);
        Assert.Contains("Плоскость деформаций", headings);
        Assert.Contains("Матрица жёсткости по СП 63", headings);
        Assert.Contains("Якобиан Ньютона", headings);
        Assert.Contains("(8.26)", formulas);
        Assert.Contains("(8.42)", formulas);
        Assert.Contains("(8.47)", formulas);
        Assert.Equal(2, document.Blocks.OfType<ReportImage>().Count());
    }
}
