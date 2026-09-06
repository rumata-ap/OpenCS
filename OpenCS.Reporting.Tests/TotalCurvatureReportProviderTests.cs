using CScore;
using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверки отчёта по полной кривизне нормального сечения.</summary>
public sealed class TotalCurvatureReportProviderTests
{
    const string TotalCurvatureJson = """
        {"N":0,"Mx_long":-127.5,"My_long":17,"Mx_total":-150,"My_total":20,
         "cracked":true,"Mcrc":38.375,"Mx_crc":-38.0384,"My_crc":5.0718,"crc_converged":true,
         "stage1":{"Mx":-150,"My":20,"e0":0.0003769338,"ky":-0.00357914,"kz":0.00186572,
         "calc_type":"N","concrete_tension":false,
         "psi_s_by_rebar":[{"num":null,"x":-0.12,"y":0.22,"psi_s":1,"applicable":false},
         {"num":null,"x":-0,"y":0.22,"psi_s":1,"applicable":false},
         {"num":null,"x":0.12,"y":0.22,"psi_s":1,"applicable":false}],"converged":true},
         "stage2":{"Mx":-127.5,"My":17,"e0":0.0003067003,"ky":-0.00288153,"kz":0.00143003,
         "calc_type":"N","concrete_tension":false,
         "psi_s_by_rebar":[{"num":null,"x":-0.12,"y":0.22,"psi_s":1,"applicable":false},
         {"num":null,"x":-0,"y":0.22,"psi_s":1,"applicable":false},
         {"num":null,"x":0.12,"y":0.22,"psi_s":1,"applicable":false}],"converged":true},
         "stage3":{"Mx":-127.5,"My":17,"e0":0.000221648,"ky":-0.00333173,"kz":0.00179651,
         "calc_type":"NL","concrete_tension":false,
         "psi_s_by_rebar":[{"num":null,"x":-0.12,"y":0.22,"psi_s":1,"applicable":false},
         {"num":null,"x":-0,"y":0.22,"psi_s":1,"applicable":false},
         {"num":null,"x":0.12,"y":0.22,"psi_s":1,"applicable":false}],"converged":true},
         "ky_full":-0.00402934,"kz_full":0.0022322,"k_full":0.00460633,"all_converged":true}
        """;

    [Fact]
    public void Parse_ReadsStagesAndPreservesNullRebarNumbers()
    {
        var data = TotalCurvatureReportData.Parse(TotalCurvatureJson);
        Assert.Equal(-127.5, data.MxLong);
        Assert.True(data.Cracked);
        Assert.Equal(0.00460633, data.KFull);
        Assert.True(data.AllConverged);
        Assert.Equal("N", data.Stage1!.CalcType);
        Assert.Equal("NL", data.Stage3!.CalcType);
        Assert.Equal(3, data.Stage1.PsiSByRebar.Count);
        Assert.Null(data.Stage1.PsiSByRebar[0].Num);
        Assert.Equal(-0.12, data.Stage1.PsiSByRebar[0].X);
    }

    [Fact]
    public void Provider_BuildsCrackedReportWithThreeStageImages()
    {
        var provider = new TotalCurvatureReportProvider();
        var task = new CalcTask { Id = 38, Kind = "total_curvature", Tag = "Косой изгиб", ParamsJson = "{\"ForcesMode\":\"share\"}" };
        var result = new CalcResult { TaskId = task.Id, TaskKind = task.Kind, DataJson = TotalCurvatureJson };
        var document = provider.Build(new ReportContext(task, result));
        var formulas = document.Blocks.OfType<ReportFormula>().Select(x => x.Reference).ToList();

        Assert.Contains(document.Blocks.OfType<ReportKeyValueTable>(), table =>
            table.Rows.Any(row => row.Key == "Режим выделения длительной части" && row.Value == "share"));
        Assert.Contains("(8.141)", formulas);
        Assert.DoesNotContain("(8.140)", formulas);
        Assert.Contains(document.Blocks.OfType<ReportTable>(), table =>
            table.Rows.SelectMany(row => row).Any(cell => cell.Contains("0.00460633")));
        Assert.Contains(document.Blocks.OfType<ReportParagraph>(), x => x.Text.Contains("stage1"));
        Assert.Contains(document.Blocks.OfType<ReportParagraph>(), x => x.Text.Contains("N — кратковременные"));
        Assert.Contains(document.Blocks.OfType<ReportParagraph>(), x => x.Text.Contains("NL — длительные"));
        Assert.Equal(3, document.Blocks.OfType<ReportTable>().Count(table => table.Headers.Contains("учитывается")));

        var images = provider.DescribeImages(task, result);
        Assert.Equal(["stage1", "stage2", "stage3"], images.Select(x => x.Key));
        Assert.Equal([CalcType.N, CalcType.N, CalcType.NL], images.Select(x => x.Calc));
        _ = new HtmlReportRenderer().Render(document);
    }

    [Fact]
    public void Provider_WarnsForAggregateAndStageFailures_AndUsesUncrackedFormula()
    {
        var task = new CalcTask { Kind = "total_curvature", ParamsJson = "{}" };
        var failedJson = System.Text.RegularExpressions.Regex.Replace(
            TotalCurvatureJson.Replace("\"all_converged\":true", "\"all_converged\":false"),
            "(\"stage2\"\\s*:\\s*\\{.*?\"converged\"\\s*:\\s*)true",
            "$1false",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        var failedDocument = new TotalCurvatureReportProvider().Build(new ReportContext(task,
            new CalcResult { TaskKind = task.Kind, DataJson = failedJson }));
        var warnings = failedDocument.Blocks.OfType<ReportWarning>()
            .Where(x => x.Text.Contains("сходимости"))
            .ToList();
        Assert.Equal(2, warnings.Count);

        var uncrackedJson = TotalCurvatureJson.Replace("\"cracked\":true", "\"cracked\":false");
        var uncracked = new TotalCurvatureReportProvider().Build(new ReportContext(task,
            new CalcResult { TaskKind = task.Kind, DataJson = uncrackedJson }));
        var refs = uncracked.Blocks.OfType<ReportFormula>().Select(x => x.Reference);
        Assert.Contains("(8.140)", refs);
        Assert.DoesNotContain("(8.141)", refs);
    }
}
