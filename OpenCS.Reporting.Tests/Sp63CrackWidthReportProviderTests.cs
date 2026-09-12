using System.Text.Json;
using CScore;
using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверки отчёта по упрощённой формульной проверке ширины раскрытия трещин СП 63.</summary>
public sealed class Sp63CrackWidthReportProviderTests
{
    const string CalculatedJson = """
        {"Status":2,"LimitPassed":true,"Cracked":true,"Branch":"cracked",
         "Details":[
            {"Formula":"(8.130)-(8.141)","Description":"Sp63CrackWidth_AcrcCheck","NormReference":"8.2.9-8.2.16",
             "Applied":0.167,"Allowable":0.3,"Variables":{"sigma_s":236.4,"ls":0.4}}],
         "ApplicabilityMessages":[],
         "InformationalMessages":[
            {"Code":"compression_zone_neutral_axis","Kind":1,"NormReference":"8.2.28","Text":"Sp63CrackWidth_NeutralAxisNote"}],
         "Variables":{"N":0.0,"M":50.0}}
        """;

    const string NotApplicableJson = """
        {"Status":1,"LimitPassed":null,"Cracked":null,"Branch":"not_applicable",
         "Details":[],
         "ApplicabilityMessages":[
            {"Code":"zero_moment","Kind":0,"NormReference":"8.2","Text":"Sp63CrackWidth_ZeroMoment"}],
         "InformationalMessages":[
            {"Code":"suggest_full_model","Kind":1,"NormReference":"8.2","Text":"Sp63CrackWidth_SuggestFullModel"}],
         "Variables":{}}
        """;

    static CalcTask MakeTask(string paramsJson = "{}") => new()
    {
        Id = 21,
        Kind = "sp63_crack_width",
        Tag = "Балка Б-1",
        CalcType = CalcType.N,
        ParamsJson = paramsJson
    };

    [Fact]
    public void Provider_BuildsReportForCalculatedResult()
    {
        var task = MakeTask("""{"ShapeKind":"rectangular","Axis":"Mx","Phi1":1.4,"Phi2":0.5,"AcrcLimMm":0.3}""");
        var result = new CalcResult { TaskId = task.Id, TaskKind = task.Kind, DataJson = CalculatedJson };

        var document = new Sp63CrackWidthReportProvider().Build(new ReportContext(task, result));

        Assert.Contains("Упрощённая проверка ширины раскрытия трещин", document.Title);
        var tables = document.Blocks.OfType<ReportTable>().ToList();
        var allCells = tables.SelectMany(t => t.Rows).SelectMany(r => r).ToList();
        Assert.DoesNotContain(allCells, c => c.Contains("Sp63CrackWidth_"));
        Assert.Contains(allCells, c => c.Contains("acrc"));
        var kvTables = document.Blocks.OfType<ReportKeyValueTable>().ToList();
        Assert.Contains(kvTables, t => t.Rows.Any(r => r.Key == "Вердикт" && r.Value.Contains("в пределах")));
        Assert.Contains(kvTables, t => t.Rows.Any(r => r.Key == "Трещины образуются" && r.Value == "да"));
        Assert.DoesNotContain(document.Blocks.OfType<ReportWarning>(), w => w.Text.Contains("неприменима"));
        _ = new HtmlReportRenderer().Render(document);
    }

    [Fact]
    public void Provider_WarnsWhenNotApplicable()
    {
        var task = MakeTask();
        var result = new CalcResult { TaskId = task.Id, TaskKind = task.Kind, DataJson = NotApplicableJson };

        var document = new Sp63CrackWidthReportProvider().Build(new ReportContext(task, result));

        Assert.Contains(document.Blocks.OfType<ReportWarning>(), w => w.Text.Contains("неприменима"));
        var tables = document.Blocks.OfType<ReportTable>().ToList();
        Assert.Contains(tables, t => t.Rows.SelectMany(r => r)
            .Any(c => c.Contains("деформационной модели")));
    }

    [Fact]
    public void Provider_RejectsUnsupportedKind()
    {
        var task = new CalcTask { Kind = "cracking" };
        var result = new CalcResult { TaskKind = task.Kind, DataJson = CalculatedJson };
        Assert.Throws<ArgumentException>(() =>
            new Sp63CrackWidthReportProvider().Build(new ReportContext(task, result)));
    }

    [Fact]
    public void Provider_ThrowsForEmptyResult()
    {
        var task = MakeTask();
        var result = new CalcResult { TaskId = task.Id, TaskKind = task.Kind, DataJson = "" };
        Assert.Throws<JsonException>(() =>
            new Sp63CrackWidthReportProvider().Build(new ReportContext(task, result)));
    }

    [Fact]
    public void Registry_ResolvesSp63CrackWidthProvider()
    {
        var registry = new ReportProviderRegistry([new Sp63CrackWidthReportProvider()]);
        Assert.Contains("sp63_crack_width", registry.SupportedKinds);
        Assert.True(registry.TryResolve(new CalcTask { Kind = "sp63_crack_width" }, out _));
    }
}
