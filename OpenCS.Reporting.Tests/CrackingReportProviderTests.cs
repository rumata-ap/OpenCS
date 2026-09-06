using CScore;
using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверки отчёта по образованию трещин.</summary>
public sealed class CrackingReportProviderTests
{
    const string CrackingJson = """
        {"converged":true,"N":0,"Mx_crc":-27.5,"My_crc":0,"Mcrc":27.5,
         "eps_max_tension":0.00014963,"eps_tension_limit":0.00015,
         "e0":-2.229E-05,"ky":-0.00114613,"kz":-0,"plane_converged":true}
        """;

    [Fact]
    public void Parse_ReadsActualCrackingContract()
    {
        var data = CrackingReportData.Parse(CrackingJson);
        Assert.True(data.Converged);
        Assert.Equal(0, data.N);
        Assert.Equal(-27.5, data.MxCrc);
        Assert.Equal(27.5, data.Mcrc);
        Assert.Equal(0.00015, data.EpsTensionLimit);
        Assert.Equal(-2.229E-05, data.E0);
        Assert.True(data.PlaneConverged);
        Assert.Null(data.Prestress);
    }

    [Fact]
    public void Provider_BuildsCrackingReportAndImage()
    {
        var provider = new CrackingReportProvider();
        var task = new CalcTask { Id = 34, Kind = "cracking", Tag = "Сечение 17", CalcType = CalcType.CL };
        var result = new CalcResult { TaskId = task.Id, TaskKind = task.Kind, DataJson = CrackingJson };

        var document = provider.Build(new ReportContext(task, result));
        var formulas = document.Blocks.OfType<ReportFormula>().Select(x => x.Reference).ToList();
        Assert.Contains("Расчёт по образованию трещин", document.Title);
        Assert.Contains("(8.26)", formulas);
        Assert.Contains("(8.32)", formulas);
        Assert.Contains(document.Blocks.OfType<ReportTable>(), table =>
            table.Rows.SelectMany(row => row).Any(cell => cell.Contains("27.5")));
        Assert.Contains(document.Blocks.OfType<ReportFormula>(), formula =>
            formula.Formula.Contains("ε") && formula.Substitution.Contains("0.00014963"));
        Assert.DoesNotContain(document.Blocks.OfType<ReportWarning>(), warning =>
            warning.Text.Contains("момента") || warning.Text.Contains("Плоскость деформаций"));

        var images = provider.DescribeImages(task, result);
        var image = Assert.Single(images);
        Assert.Equal("strain", image.Key);
        Assert.Equal(ReportImageMode.Strain, image.Mode);
        Assert.Equal(CalcType.CL, image.Calc);
        _ = new HtmlReportRenderer().Render(document);
    }

    [Fact]
    public void Provider_WarnsForCalculationAndPlaneFailures()
    {
        var brokenJson = CrackingJson
            .Replace("\"converged\":true", "\"converged\":false")
            .Replace("\"plane_converged\":true", "\"plane_converged\":false");
        var task = new CalcTask { Kind = "cracking", CalcType = CalcType.C };
        var result = new CalcResult { TaskId = task.Id, TaskKind = task.Kind, DataJson = brokenJson };

        var warnings = new CrackingReportProvider().Build(new ReportContext(task, result))
            .Blocks.OfType<ReportWarning>()
            .Where(warning => warning.Text.Contains("момента") || warning.Text.Contains("Плоскость деформаций"))
            .ToList();
        Assert.Equal(2, warnings.Count);
    }
}
