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

    const string TeeCalculatedJson = """
        {"Status":2,"StrengthPassed":true,"Branch":"bending",
         "StrengthDetails":[
            {"Formula":"(8.8)","Description":"Sp63Normal_TeeBendingCheck","NormReference":"8.1.11",
             "Applied":1000.0,"Allowable":4741.5,"Variables":{"x":0.3,"hasCompressionFlange":1.0}}],
         "ConstructiveChecks":[],"ApplicabilityMessages":[],"InformationalMessages":[],
         "Variables":{"bw":0.4,"h":0.8,"x":0.3}}
        """;

    const string TeeUnsupportedJson = """
        {"Status":1,"StrengthPassed":null,"Branch":"not_applicable",
         "StrengthDetails":[],"ConstructiveChecks":[],
         "ApplicabilityMessages":[
            {"Code":"unsupported_load_case_for_tee","Kind":0,"NormReference":"8.1.11",
             "Text":"Sp63Normal_UnsupportedLoadCaseForTee"}],
         "InformationalMessages":[
            {"Code":"suggest_ndm","Kind":1,"NormReference":"8.1","Text":"Sp63Normal_SuggestNdm"}],
         "Variables":{}}
        """;

    const string TeeMissingSpanJson = """
        {"Status":1,"StrengthPassed":null,"Branch":"not_applicable",
         "StrengthDetails":[],"ConstructiveChecks":[],
         "ApplicabilityMessages":[
            {"Code":"missing_span_length","Kind":0,"NormReference":"8.1.11",
             "Text":"Sp63Normal_MissingSpanLength"}],
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

    [Fact]
    public void Provider_BuildsTeeInputRows_AndLocalizesMessages()
    {
        var task = MakeTask("""{"ShapeKind":"tee","Axis":"Mx","StructuralScheme":"statically_indeterminate","StabilityMode":"member","Psi":0.5,"SlendernessThreshold":14,"SpanLength":6.3}""");
        var result = new CalcResult { TaskId = task.Id, TaskKind = task.Kind, DataJson = TeeCalculatedJson };

        var document = new Sp63NormalReportProvider().Build(new ReportContext(task, result));

        var kvTables = document.Blocks.OfType<ReportKeyValueTable>().ToList();
        Assert.Contains(kvTables, t => t.Rows.Any(r => r.Key == "Форма сечения" && r.Value == "тавр/двутавр"));
        Assert.Contains(kvTables, t => t.Rows.Any(r => r.Key == "Пролёт элемента l, м" && r.Value.Contains("6.3")));
        var allCells = document.Blocks.OfType<ReportTable>().SelectMany(t => t.Rows).SelectMany(r => r).ToList();
        Assert.DoesNotContain(allCells, c => c.Contains("Sp63Normal_"));
        Assert.Contains(allCells, c => c.Contains("Изгиб тавра"));
    }

    [Fact]
    public void Provider_WarnsForUnsupportedLoadCaseTee_WithNdmHint()
    {
        var task = MakeTask("""{"ShapeKind":"tee","Axis":"Mx"}""");
        var result = new CalcResult { TaskId = task.Id, TaskKind = task.Kind, DataJson = TeeUnsupportedJson };

        var document = new Sp63NormalReportProvider().Build(new ReportContext(task, result));

        var allCells = document.Blocks.OfType<ReportTable>().SelectMany(t => t.Rows).SelectMany(r => r).ToList();
        Assert.Contains(allCells, c => c.Contains("чистого изгиба") || c.Contains("НДМ"));
    }

    [Fact]
    public void Provider_ShowsMissingSpanLengthReason()
    {
        var task = MakeTask("""{"ShapeKind":"tee","Axis":"Mx"}""");
        var result = new CalcResult { TaskId = task.Id, TaskKind = task.Kind, DataJson = TeeMissingSpanJson };

        var document = new Sp63NormalReportProvider().Build(new ReportContext(task, result));

        var allCells = document.Blocks.OfType<ReportTable>().SelectMany(t => t.Rows).SelectMany(r => r).ToList();
        Assert.Contains(allCells, c => c.Contains("8.1.11") || c.Contains("пролёт"));
    }

    const string CircularCalculatedJson = """
        {"Status":2,"StrengthPassed":false,"Branch":"circular_compression",
         "StrengthDetails":[
            {"Formula":"(Д.6)","Description":"Sp63Normal_CircularCheck","NormReference":"Д.2",
             "Applied":120.0,"Allowable":0.0,"Variables":{"conditionD7":0.0,"phi":0.0}}],
         "ConstructiveChecks":[],"ApplicabilityMessages":[],
         "InformationalMessages":[
            {"Code":"appendix_d_recommended","Kind":1,"NormReference":"Д","Text":"Sp63Normal_AppendixDRecommended"},
            {"Code":"resultant_moment_used","Kind":1,"NormReference":"Д","Text":"Sp63Normal_ResultantMomentUsed"},
            {"Code":"circular_rebar_class_by_rs","Kind":1,"NormReference":"Д.2","Text":"Sp63Normal_CircularRebarClassByRs"},
            {"Code":"appendix_d_pure_bending_extension","Kind":1,"NormReference":"Д","Text":"Sp63Normal_AppendixDPureBendingExtension"},
            {"Code":"compression_exceeds_section_capacity","Kind":2,"NormReference":"Д.2","Text":"Sp63Normal_CompressionExceedsSectionCapacity"}],
         "Variables":{"M0":100.0,"M":120.0}}
        """;

    const string AnnularCalculatedJson = """
        {"Status":2,"StrengthPassed":true,"Branch":"annular_bending",
         "StrengthDetails":[
            {"Formula":"(Д.3)","Description":"Sp63Normal_AnnularCheck","NormReference":"Д.1",
             "Applied":50.0,"Allowable":138.6,"Variables":{"annularBranch":2.0}}],
         "ConstructiveChecks":[],"ApplicabilityMessages":[],"InformationalMessages":[],
         "Variables":{"M0":50.0,"M":50.0}}
        """;

    const string CircularNotApplicableJson = """
        {"Status":1,"StrengthPassed":null,"Branch":"not_applicable",
         "StrengthDetails":[],"ConstructiveChecks":[],
         "ApplicabilityMessages":[
            {"Code":"unsupported_geometry","Kind":0,"NormReference":"Д.2","Text":"Sp63Normal_CircularSingleConcreteRegion"},
            {"Code":"unsupported_geometry","Kind":0,"NormReference":"Д.2","Text":"Sp63Normal_CircularHasHole"},
            {"Code":"unsupported_geometry","Kind":0,"NormReference":"Д.1","Text":"Sp63Normal_AnnularHoleCount"},
            {"Code":"unsupported_geometry","Kind":0,"NormReference":"Д.2","Text":"Sp63Normal_NotCircularContour"},
            {"Code":"unsupported_geometry","Kind":0,"NormReference":"Д.1","Text":"Sp63Normal_AnnularNotConcentric"},
            {"Code":"annular_radius_ratio","Kind":0,"NormReference":"Д, примечание 3","Text":"Sp63Normal_AnnularRadiusRatio"},
            {"Code":"circular_tension_not_supported","Kind":0,"NormReference":"Д","Text":"Sp63Normal_CircularTensionNotSupported"},
            {"Code":"circular_insufficient_bars","Kind":0,"NormReference":"Д, примечание 1","Text":"Sp63Normal_CircularInsufficientBars"},
            {"Code":"circular_rebar_not_centered","Kind":0,"NormReference":"Д","Text":"Sp63Normal_CircularRebarNotCentered"},
            {"Code":"circular_rebar_unequal_areas","Kind":0,"NormReference":"Д","Text":"Sp63Normal_CircularRebarUnequalAreas"},
            {"Code":"circular_rebar_not_on_circle","Kind":0,"NormReference":"Д","Text":"Sp63Normal_CircularRebarNotOnCircle"},
            {"Code":"circular_rebar_non_uniform","Kind":0,"NormReference":"Д","Text":"Sp63Normal_CircularRebarNonUniform"},
            {"Code":"circular_rebar_class_above_a400","Kind":0,"NormReference":"Д.2","Text":"Sp63Normal_CircularRebarClassAboveA400"}],
         "InformationalMessages":[
            {"Code":"suggest_ndm","Kind":1,"NormReference":"8.1","Text":"Sp63Normal_SuggestNdm"}],
         "Variables":{}}
        """;

    static List<string> AllText(ReportDocument document) =>
        document.Blocks.OfType<ReportTable>().SelectMany(t => t.Rows).SelectMany(r => r)
            .Concat(document.Blocks.OfType<ReportKeyValueTable>()
                .SelectMany(t => t.Rows).SelectMany(r => new[] { r.Key, r.Value }))
            .ToList();

    static string KeyValue(ReportDocument document, string key) =>
        document.Blocks.OfType<ReportKeyValueTable>().SelectMany(t => t.Rows)
            .First(r => r.Key == key).Value;

    [Fact]
    public void Provider_BuildsCircularReport_WithLocalizedShapeAxisBranchAndMessages()
    {
        var task = MakeTask("""{"ShapeKind":"circular","Axis":"Mx","StructuralScheme":"statically_indeterminate","StabilityMode":"member","Psi":0.5,"SlendernessThreshold":14}""");
        var result = new CalcResult { TaskId = task.Id, TaskKind = task.Kind, DataJson = CircularCalculatedJson };

        var document = new Sp63NormalReportProvider().Build(new ReportContext(task, result));

        Assert.Equal("круглое сплошное", KeyValue(document, "Форма сечения"));
        Assert.Equal("не используется (результирующий момент)", KeyValue(document, "Ось изгиба"));
        Assert.Equal("круглое сечение, внецентренное сжатие", KeyValue(document, "Нормативная ветвь"));
        Assert.DoesNotContain(AllText(document), c => c.Contains("Sp63Normal_"));
        Assert.Contains(AllText(document), c => c.Contains("Круглое сечение: M ≤ Mult"));
        _ = new HtmlReportRenderer().Render(document);
    }

    [Fact]
    public void Provider_BuildsAnnularReport()
    {
        var task = MakeTask("""{"ShapeKind":"annular","Axis":"My"}""");
        var result = new CalcResult { TaskId = task.Id, TaskKind = task.Kind, DataJson = AnnularCalculatedJson };

        var document = new Sp63NormalReportProvider().Build(new ReportContext(task, result));

        Assert.Equal("кольцевое", KeyValue(document, "Форма сечения"));
        Assert.Equal("кольцевое сечение, изгиб", KeyValue(document, "Нормативная ветвь"));
        Assert.DoesNotContain(AllText(document), c => c.Contains("Sp63Normal_"));
    }

    [Fact]
    public void Provider_LocalizesAllCircularApplicabilityTexts()
    {
        var task = MakeTask("""{"ShapeKind":"circular"}""");
        var result = new CalcResult { TaskId = task.Id, TaskKind = task.Kind, DataJson = CircularNotApplicableJson };

        var document = new Sp63NormalReportProvider().Build(new ReportContext(task, result));

        Assert.DoesNotContain(AllText(document), c => c.Contains("Sp63Normal_"));
    }
}
