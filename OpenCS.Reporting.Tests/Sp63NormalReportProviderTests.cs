using System.Text.Json;
using CScore;
using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверки отчёта по упрощённой формульной проверке нормального сечения СП 63.</summary>
public sealed class Sp63NormalReportProviderTests
{
    const string CalculatedJson = """
        {"Status":2,"StrengthPassed":true,"Branch":"compression",
         "StrengthDetails":[
            {"Formula":"(8.1.8)","Description":"Sp63Normal_CompressionCheck","NormReference":"8.1.8",
             "Applied":120.5,"Allowable":180.2,"Variables":{"e":0.15,"xi":0.35}}],
         "ConstructiveChecks":[
            {"Formula":"10.3.6","Description":"Sp63Normal_MinReinforcementCompression","NormReference":"10.3.6",
             "Applied":0.001,"Allowable":0.0015,"Variables":{}}],
         "ApplicabilityMessages":[],
         "InformationalMessages":[
            {"Code":"suggest_ndm","Kind":1,"NormReference":"справочно","Text":"Sp63Normal_SuggestNdm"}],
         "Variables":{"N":120.5,"M":45.2},
         "Eta":{"Eta":1.12,"Ncr":950.3,"D":1500.0,"Slender":true,"Stable":true,"MEff":50.6,
                "Iterations":3,"ExtrapolationFailed":false,"EtaHistory":[1.05,1.10,1.12]}}
        """;

    const string NotApplicableJson = """
        {"Status":1,"StrengthPassed":null,"Branch":"not_applicable",
         "StrengthDetails":[],"ConstructiveChecks":[],
         "ApplicabilityMessages":[
            {"Code":"biaxial_load","Kind":0,"NormReference":"8.1","Text":"Sp63Normal_BiaxialLoad"}],
         "InformationalMessages":[],"Variables":{}}
        """;

    static CalcTask MakeTask(string paramsJson = "{}") => new()
    {
        Id = 12,
        Kind = "sp63_normal",
        Tag = "Колонна К-1",
        CalcType = CalcType.C,
        ParamsJson = paramsJson
    };

    [Fact]
    public void Provider_BuildsReportForCalculatedResult()
    {
        var task = MakeTask("""{"ShapeKind":"rectangular","Axis":"Mx","StructuralScheme":"statically_indeterminate","StabilityMode":"member","Psi":0.5,"SlendernessThreshold":14}""");
        var result = new CalcResult { TaskId = task.Id, TaskKind = task.Kind, DataJson = CalculatedJson };

        var document = new Sp63NormalReportProvider().Build(new ReportContext(task, result));

        Assert.Contains("Упрощённая проверка нормального сечения", document.Title);
        var tables = document.Blocks.OfType<ReportTable>().ToList();
        var allCells = tables.SelectMany(t => t.Rows).SelectMany(r => r).ToList();
        Assert.DoesNotContain(allCells, c => c.Contains("Sp63Normal_"));
        Assert.Contains(allCells, c => c.Contains("Внецентренное сжатие"));
        Assert.Contains(tables, t => t.Rows.SelectMany(r => r).Any(c => c.Contains("используйте расчёт по деформационной модели")));
        var kvTables = document.Blocks.OfType<ReportKeyValueTable>().ToList();
        Assert.Contains(kvTables, t => t.Rows.Any(r => r.Key == "Вердикт прочности" && r.Value == "прочность обеспечена"));
        Assert.Contains(kvTables, t => t.Rows.Any(r => r.Key == "η" && r.Value.Contains("1.12")));
        Assert.DoesNotContain(document.Blocks.OfType<ReportWarning>(), w => w.Text.Contains("неприменима"));
        _ = new HtmlReportRenderer().Render(document);
    }

    [Fact]
    public void Provider_WarnsWhenNotApplicable()
    {
        var task = MakeTask();
        var result = new CalcResult { TaskId = task.Id, TaskKind = task.Kind, DataJson = NotApplicableJson };

        var document = new Sp63NormalReportProvider().Build(new ReportContext(task, result));

        Assert.Contains(document.Blocks.OfType<ReportWarning>(), w => w.Text.Contains("неприменима"));
        var tables = document.Blocks.OfType<ReportTable>().ToList();
        Assert.Contains(tables, t => t.Rows.SelectMany(r => r)
            .Any(c => c.Contains("одноосная") && c.Contains("НДМ")));
    }

    [Fact]
    public void Provider_RejectsUnsupportedKind()
    {
        var task = new CalcTask { Kind = "cracking" };
        var result = new CalcResult { TaskKind = task.Kind, DataJson = CalculatedJson };
        Assert.Throws<ArgumentException>(() =>
            new Sp63NormalReportProvider().Build(new ReportContext(task, result)));
    }

    [Fact]
    public void Provider_ThrowsForEmptyResult()
    {
        var task = MakeTask();
        var result = new CalcResult { TaskId = task.Id, TaskKind = task.Kind, DataJson = "" };
        Assert.Throws<JsonException>(() =>
            new Sp63NormalReportProvider().Build(new ReportContext(task, result)));
    }

    [Fact]
    public void Registry_ResolvesSp63NormalProvider()
    {
        var registry = new ReportProviderRegistry([new Sp63NormalReportProvider()]);
        Assert.Contains("sp63_normal", registry.SupportedKinds);
        Assert.True(registry.TryResolve(new CalcTask { Kind = "sp63_normal" }, out _));
    }
}
