using CScore;
using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверки разбора и структуры отчёта по прочности нормального сечения.</summary>
public sealed class LimitForceReportProviderTests
{
    const string LimitJson = """
        {"solver_method":"fast","converged":true,"iterations":9,"newton_iterations":9,
         "factor":1.545731,"utilization":0.646943,"governing":"concrete",
         "N_target":0,"Mx_target":-50,"My_target":0,
         "N_limit":0,"Mx_limit":-77.2865,"My_limit":0,
         "e0":0.00985834,"ky":-0.08905561,"kz":0,
         "eps_contour_min":-0.0035,"eps_cu":-0.0035,
         "eps_rebar_max":0.01947635,"eps_su":0.025,
         "N_result":0,"Mx_result":-77.2865,"My_result":0,"eta":null}
        """;

    const string EtaJson = """
        {"solver_method":"fast","converged":true,"iterations":2,"newton_iterations":1,
         "factor":1.2,"utilization":0.8,"governing":"both",
         "N_target":1,"Mx_target":2,"My_target":3,"N_limit":4,"Mx_limit":5,"My_limit":6,
         "e0":0,"ky":0,"kz":0,"eps_contour_min":-0.0035,"eps_cu":-0.0035,
         "eps_rebar_max":0.015,"eps_su":0.015,"N_result":4,"Mx_result":5,"My_result":6,
         "eta":{"mode":"iterative","slendernessThreshold":14,"mxOriginal":2,"myOriginal":3,
         "mxLongOriginal":1,"myLongOriginal":1.5,"l0x":4,"hx":0.3,"slendernessX":13.33,
         "dX":1.1,"etaX":1.05,"ncrX":80,"slenderX":false,"stableX":true,
         "extrapolationFailedX":false,"etaHistoryX":[1,1.05],"l0y":5,"hy":0.25,
         "slendernessY":20,"dY":2.2,"etaY":1.12,"ncrY":70,"slenderY":true,"stableY":true,
         "extrapolationFailedY":false,"etaHistoryY":[1,1.12]}}
        """;

    [Fact]
    public void Parse_ReadsLimitContractAndEta()
    {
        var data = LimitForceReportData.Parse(LimitJson);
        Assert.Equal("fast", data.SolverMethod);
        Assert.True(data.Converged);
        Assert.Equal(9, data.Iterations);
        Assert.Equal(9, data.NewtonIterations);
        Assert.Equal(1.545731, data.Factor);
        Assert.Equal(0.646943, data.Utilization);
        Assert.Equal("concrete", data.Governing);
        Assert.Equal(-77.2865, data.LimitMx);
        Assert.Equal(-0.0035, data.EpsContourMin);
        Assert.Equal(0.025, data.EpsSu);
        Assert.Null(data.Eta);

        var eta = LimitForceReportData.Parse(EtaJson).Eta;
        Assert.NotNull(eta);
        Assert.Equal(14, eta!.SlendernessThreshold);
        Assert.Equal(1.05, eta.EtaX);
        Assert.Equal(1.12, eta.EtaY);
        Assert.Equal(2, eta.EtaHistoryX.Length);
    }

    [Fact]
    public void Provider_BuildsLimitReportAndImagesForAllAliases()
    {
        var provider = new LimitForceReportProvider();
        var task = new CalcTask { Id = 438, Kind = "limit_moment", Tag = "Балка 18", CalcType = CalcType.C };
        var result = new CalcResult { TaskId = task.Id, TaskKind = task.Kind, DataJson = LimitJson };
        var document = provider.Build(new ReportContext(task, result));

        Assert.Contains("Балка 18", document.Title);
        Assert.Contains(document.Blocks.OfType<ReportFormula>(), x => x.Reference == "(8.37)");
        Assert.Contains(document.Blocks.OfType<ReportFormula>(), x => x.Reference == "(8.38)");
        Assert.Contains(document.Blocks.OfType<ReportTable>(), table =>
            table.Rows.SelectMany(row => row).Any(cell => cell.Contains("77.2865")));
        Assert.Contains(document.Blocks, x => x is ReportWarning);
        var images = provider.DescribeImages(task, result);
        Assert.Equal(["strain", "stress"], images.Select(x => x.Key));
        Assert.Equal([ReportImageMode.Strain, ReportImageMode.Stress], images.Select(x => x.Mode));
        _ = new HtmlReportRenderer().Render(document);

        var registry = new ReportProviderRegistry([provider]);
        foreach (var kind in new[] { "limit_force", "limit_moment", "limit_axial" })
            Assert.True(registry.TryResolve(new CalcTask { Kind = kind }, out _));
    }

    [Fact]
    public void CanHandle_UsesThreeLimitAliases()
    {
        var provider = new LimitForceReportProvider();
        Assert.Equal("limit_moment", provider.TaskKind);
        Assert.Equal(["limit_force", "limit_moment", "limit_axial"], provider.SupportedKinds);
        Assert.All(provider.SupportedKinds, kind => Assert.True(provider.CanHandle(new CalcTask { Kind = kind })));
        Assert.False(provider.CanHandle(new CalcTask { Kind = "shear_inclined" }));
    }
}
