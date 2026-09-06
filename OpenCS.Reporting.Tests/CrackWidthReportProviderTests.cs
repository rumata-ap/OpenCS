using CScore;
using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверки отчёта по раскрытию нормальных трещин.</summary>
public sealed class CrackWidthReportProviderTests
{
    const string CrackWidthJson = """
        {"N":0,"Mx_long":-50,"Mx_total":-60,"My_long":0,"My_total":0,
         "Mx_long_input":-50,"Mx_total_input":-60,"My_long_input":0,"My_total_input":0,
         "cracked":true,"acrc_long":0.2305,"acrc_short":0.2738,
         "acrc_ult_long":0.3,"acrc_ult_short":0.4,"passed_long":true,"passed_short":true,
         "Mcrc":27.5,"Mx_crc":-27.5,"My_crc":0,"crc_converged":true,
         "eps_max_tension":0.00014963,"eps_tension_limit":0.00015,
         "h0":258,"sigma_s":237.31,"sigma_s_crc":130.99,"sigma_s_crc2":130.99,
         "psi_s":0.6937,"psi_s2":0.7309,"acrc1":0.2305,"acrc2":0.208,"acrc3":0.1646,
         "ls":400,"ds_eq":14,"As_tens":9.2363,"Abt":1483.5,
         "e0":0.00043942,"ky":-0.00691811,"kz":-0,"plane_converged":true,
         "acrc_by_rebar":[
         {"x":-533,"y":-108,"psi_s":0.6937,"acrc_long_mm":0.2305,"psi_s2":0.7309,"acrc_short_mm":0.2738},
         {"x":-319.8,"y":-108,"psi_s":0.6937,"acrc_long_mm":0.2305,"psi_s2":0.7309,"acrc_short_mm":0.2738},
         {"x":-106.6,"y":-108,"psi_s":0.6937,"acrc_long_mm":0.2305,"psi_s2":0.7309,"acrc_short_mm":0.2738},
         {"x":106.6,"y":-108,"psi_s":0.6937,"acrc_long_mm":0.2305,"psi_s2":0.7309,"acrc_short_mm":0.2738},
         {"x":319.8,"y":-108,"psi_s":0.6937,"acrc_long_mm":0.2305,"psi_s2":0.7309,"acrc_short_mm":0.2738},
         {"x":533,"y":-108,"psi_s":0.6937,"acrc_long_mm":0.2305,"psi_s2":0.7309,"acrc_short_mm":0.2738}],
         "eta":null}
        """;

    [Fact]
    public void Parse_ReadsMixedUnitsAndAllSixRebars()
    {
        var data = CrackWidthReportData.Parse(CrackWidthJson);
        Assert.Equal(-50, data.MxLong);
        Assert.Equal(-60, data.MxTotalInput);
        Assert.True(data.Cracked);
        Assert.Equal(0.2305, data.AcrcLong);
        Assert.Equal(0.2738, data.AcrcShort);
        Assert.Equal(258, data.H0);
        Assert.Equal(9.2363, data.AsTens);
        Assert.Equal(400, data.Ls);
        Assert.Equal(6, data.AcrcByRebar.Count);
        Assert.Equal(-533, data.AcrcByRebar[0].X);
        Assert.Null(data.Eta);
        Assert.Null(data.Prestress);
    }

    [Fact]
    public void Provider_BuildsWidthReportWithNeutralPsiBranch()
    {
        var provider = new CrackWidthReportProvider();
        var task = new CalcTask { Id = 33, Kind = "crack_width", Tag = "Сечение 16", CalcType = CalcType.N };
        var result = new CalcResult { TaskId = task.Id, TaskKind = task.Kind, DataJson = CrackWidthJson };
        var document = provider.Build(new ReportContext(task, result));
        var formulas = document.Blocks.OfType<ReportFormula>().ToList();
        var references = formulas.Select(x => x.Reference).ToList();

        Assert.Contains("Расчёт по раскрытию нормальных трещин", document.Title);
        Assert.Contains("(8.128)", references);
        Assert.Contains("(8.136)", references);
        Assert.Contains("(8.137)", references);
        Assert.Contains("(8.161)", references);
        Assert.Contains(document.Blocks.OfType<ReportParagraph>(), x => x.Text.Contains("нейтраль"));
        Assert.Contains(document.Blocks.OfType<ReportTable>(), table =>
            table.Rows.SelectMany(row => row).Any(cell => cell.Contains("0.2305")));
        Assert.Equal(2, document.Blocks.OfType<ReportFormula>().Count(x => x.Reference.Contains("8.2.6")));
        var rebarTable = Assert.Single(document.Blocks.OfType<ReportTable>(),
            table => table.Headers.Contains("a_crc,long, мм"));
        Assert.Equal(6, rebarTable.Rows.Count);
        var image = Assert.Single(provider.DescribeImages(task, result));
        Assert.Equal(ReportImageMode.Strain, image.Mode);
        Assert.Equal(CalcType.N, image.Calc);
        _ = new HtmlReportRenderer().Render(document);
    }

    [Fact]
    public void Provider_HandlesNoCracksAndFailedChecks()
    {
        var task = new CalcTask { Kind = "crack_width", CalcType = CalcType.N };
        var noCracks = CrackWidthJson.Replace("\"cracked\":true", "\"cracked\":false");
        var noCracksDocument = new CrackWidthReportProvider().Build(new ReportContext(task,
            new CalcResult { TaskKind = task.Kind, DataJson = noCracks }));
        Assert.Contains(documentBlock(noCracksDocument), x =>
            x.Contains("трещины не образуются", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(noCracksDocument.Blocks.OfType<ReportFormula>(), x => x.Reference == "(8.128)");

        var failed = CrackWidthJson
            .Replace("\"passed_long\":true", "\"passed_long\":false")
            .Replace("\"passed_short\":true", "\"passed_short\":false");
        var warnings = new CrackWidthReportProvider().Build(new ReportContext(task,
            new CalcResult { TaskKind = task.Kind, DataJson = failed }))
            .Blocks.OfType<ReportWarning>().Where(x => x.Text.Contains("раскрытия")).ToList();
        Assert.Equal(2, warnings.Count);
    }

    static IEnumerable<string> documentBlock(ReportDocument document)
        => document.Blocks.OfType<ReportParagraph>().Select(x => x.Text);
}
