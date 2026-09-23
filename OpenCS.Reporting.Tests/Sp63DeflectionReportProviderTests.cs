using System.Text.Json;
using CScore;
using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

public sealed class Sp63DeflectionReportProviderTests
{
    const string CalculatedJson = """
        {"Status":2,"Scheme":0,"CoefficientS":0.1041666667,"SpanM":6,"DeflectionMm":18.2,
         "DeflectionLimitMm":20,"Utilization":0.91,"DeflectionPassed":true,"Cracked":true,"Branch":"cracked",
         "Curvature":{"Cracked":true,"Total":0.00485,"PhiBCr":2.5,"EpsB1RedLong":0.0028,
          "Terms":[{"Index":1,"LongTerm":false,"M":80,"N":10,"Eb1":7333333,"PsiS":0.7,"Xm":0.1,"IRed":0.0004,"D":2933,"Curvature":0.003},
                   {"Index":2,"LongTerm":false,"M":20,"N":4,"Eb1":7333333,"PsiS":0.7,"Xm":0.1,"IRed":0.0004,"D":2933,"Curvature":0.001},
                   {"Index":3,"LongTerm":true,"M":20,"N":4,"Eb1":3928571,"PsiS":0.7,"Xm":0.1,"IRed":0.0004,"D":1571,"Curvature":0.00285}]},
         "ApplicabilityMessages":[],"InformationalMessages":[],
         "Variables":{"N":10,"M":-80,"Nl":4,"Ml":-20,"S":0.1041666667,"l":6,"f":18.2,"fult":20,"utilization":0.91,"McrcFull":8,"McrcLong":3}}
        """;

    static CalcTask Task(string kind = "sp63_deflection") => new()
    {
        Id = 42, Kind = kind, Tag = "Балка Б-1", CalcType = CalcType.N,
        ParamsJson = """{"shapeKind":"rectangular","axis":"Mx","scheme":"simply_supported_uniform","spanM":6,"deflectionLimitMm":20,"humidity":"40_75","forcesMode":"manual","useManualForces":true,"n":10,"mx":-80,"my":0,"nLongManual":4,"mxLongManual":-20,"myLongManual":0}"""
    };

    [Fact]
    public void Provider_BuildsCurvatureAndDeflectionReportWithLocalizedMessages()
    {
        var task = Task();
        var result = new CalcResult { TaskId = task.Id, TaskKind = task.Kind, DataJson = CalculatedJson };
        var document = new Sp63DeflectionReportProvider().Build(new ReportContext(task, result));

        Assert.Contains(document.Blocks.OfType<ReportHeading>(), h => h.Text.Contains("Кривизна"));
        Assert.Contains(document.Blocks.OfType<ReportHeading>(), h => h.Text.Contains("Прогиб"));
        Assert.Contains(document.Blocks.OfType<ReportFormula>(), f => f.Reference == "(8.139)");
        var cells = document.Blocks.OfType<ReportTable>().SelectMany(t => t.Rows).SelectMany(row => row).ToList();
        Assert.Contains(document.Blocks.OfType<ReportTable>(), table => table.Headers.Contains("M, кН·м"));
        Assert.Contains(document.Blocks.OfType<ReportTable>(), table => table.Headers.Contains("1/r, 1/м"));
        Assert.DoesNotContain(cells, cell => cell.Contains("Sp63Deflection_"));
        _ = new HtmlReportRenderer().Render(document);
    }

    [Fact]
    public void Provider_RejectsUnsupportedTaskKind()
    {
        var task = Task("sp63_crack_width");
        var result = new CalcResult { TaskKind = task.Kind, DataJson = CalculatedJson };
        Assert.Throws<ArgumentException>(() => new Sp63DeflectionReportProvider().Build(new ReportContext(task, result)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    public void Provider_RejectsEmptyOrInvalidResultJson(string json)
    {
        var task = Task();
        var result = new CalcResult { TaskKind = task.Kind, DataJson = json };
        Assert.Throws<JsonException>(() => new Sp63DeflectionReportProvider().Build(new ReportContext(task, result)));
    }

    [Fact]
    public void Registry_ResolvesSp63DeflectionProvider()
    {
        var registry = new ReportProviderRegistry([new Sp63DeflectionReportProvider()]);
        Assert.Contains("sp63_deflection", registry.SupportedKinds);
        Assert.True(registry.TryResolve(Task(), out _));
    }
}
