using System.Text.Json;
using CScore;

namespace OpenCS.Reporting;

/// <summary>Поставщик отчёта по полной кривизне нормального сечения.</summary>
public sealed class TotalCurvatureReportProvider : IReportProvider
{
    /// <inheritdoc/>
    public string TaskKind => "total_curvature";

    /// <inheritdoc/>
    public IReadOnlyCollection<string> SupportedKinds => [TaskKind];

    /// <inheritdoc/>
    public bool CanHandle(CalcTask task) => SupportedKinds.Contains(task.Kind, StringComparer.Ordinal);

    /// <inheritdoc/>
    public IReadOnlyList<ReportImageRequest> DescribeImages(CalcTask task, CalcResult result)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(result);
        var data = TotalCurvatureReportData.Parse(result.DataJson);
        var requests = new List<ReportImageRequest>();
        AddImageRequest(requests, "stage1", "Карта деформаций ε: стадия 1 — вся нагрузка", data.Stage1);
        AddImageRequest(requests, "stage2", "Карта деформаций ε: стадия 2 — длительная часть кратковременно", data.Stage2);
        AddImageRequest(requests, "stage3", "Карта деформаций ε: стадия 3 — длительная часть продолжительно", data.Stage3);
        return requests;
    }

    /// <inheritdoc/>
    public ReportDocument Build(ReportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!CanHandle(context.Task))
            throw new ArgumentException("Поставщик не поддерживает данный тип задачи.", nameof(context));

        var data = TotalCurvatureReportData.Parse(context.Result.DataJson);
        string tag = string.IsNullOrWhiteSpace(context.Task.Tag) ? "без метки" : context.Task.Tag;
        var document = new ReportDocument($"Расчёт кривизны — {tag}");
        SectionReportSections.Identification(document, context,
            "координаты и размеры — м; координаты стержней в таблицах ψs — м; площади — м²; ε — безразмерная; κ — 1/м; σ — МПа; N — кН; M — кН·м");

        string forcesMode = ReadForcesMode(context.Task.ParamsJson);
        document
            .Add(new ReportHeading(1, "Исходные данные"))
            .Add(new ReportKeyValueTable(
            [
                ("N", SectionReportSections.Force(data.N)),
                ("Mx длительный / полный", $"{SectionReportSections.Moment(data.MxLong)} / {SectionReportSections.Moment(data.MxTotal)}"),
                ("My длительный / полный", $"{SectionReportSections.Moment(data.MyLong)} / {SectionReportSections.Moment(data.MyTotal)}"),
                ("Режим выделения длительной части", forcesMode)
            ], "Параметр", "Значение"))
            .Add(new ReportHeading(1, "Наличие трещин"))
            .Add(new ReportKeyValueTable(
            [
                ("Участок", data.Cracked ? "с трещинами" : "без трещин"),
                ("Mcrc", $"{F(data.Mcrc)} кН·м"),
                ("Mx_crc / My_crc", $"{F(data.MxCrc)} / {F(data.MyCrc)} кН·м"),
                ("Сходимость Mcrc", data.CrcConverged ? "достигнута" : "не достигнута")
            ], "Параметр", "Значение"))
            .Add(new ReportHeading(1, "Расчётный аппарат кривизны"))
            .Add(new ReportFormula("(8.26)",
                $"{Sub("M", "x")} = Σ({Sub("σ", "b")}·{Sub("A", "b")}·{Sub("y", "b")}) + Σ({Sub("σ", "s")}·{Sub("A", "s")}·{Sub("y", "s")})",
                $"{Sub("M", "x")} длительный = {F(data.MxLong)}; полный = {F(data.MxTotal)}", "равновесие по слоям сечения"))
            .Add(new ReportFormula("(8.27)",
                $"{Sub("M", "y")} = Σ({Sub("σ", "b")}·{Sub("A", "b")}·{Sub("x", "b")}) + Σ({Sub("σ", "s")}·{Sub("A", "s")}·{Sub("x", "s")})",
                $"{Sub("M", "y")} длительный = {F(data.MyLong)}; полный = {F(data.MyTotal)}", "равновесие по слоям сечения"))
            .Add(new ReportFormula("(8.28)", $"N = Σ({Sub("σ", "b")}·{Sub("A", "b")}) + Σ({Sub("σ", "s")}·{Sub("A", "s")})",
                $"N = {F(data.N)}", $"N = {F(data.N)} кН"))
            .Add(new ReportFormula("(8.29)",
                $"{Sub("ε", "bi")} = {Sub("ε", "0")} + {Sub("κ", "y")}·{Sub("y", "bi")} + {Sub("κ", "z")}·{Sub("x", "bi")}",
                "гипотеза плоских сечений", "деформации слоёв определяются плоскостью"))
            .Add(new ReportFormula("(8.30)",
                $"{Sub("ε", "si")} = {Sub("ε", "0")} + {Sub("κ", "y")}·{Sub("y", "si")} + {Sub("κ", "z")}·{Sub("x", "si")}",
                "гипотеза плоских сечений", "деформации арматуры определяются плоскостью"))
            .Add(new ReportFormula("(8.31)", $"{Sub("σ", "bi")} = {Sub("E", "b")}·{Sub("ν", "b")}·{Sub("ε", "bi")}",
                "диаграмма бетона", "напряжения бетона в интеграле"))
            .Add(new ReportFormula("(8.32)", $"{Sub("σ", "si")} = {Sub("E", "s")}·{Sub("ν", "s")}·{Sub("ε", "si")}",
                "диаграмма арматуры", "напряжения арматуры в интеграле"))
            .Add(new ReportFormula("(8.160)",
                "σ<sub>sj</sub> = E<sub>sj</sub>·ν<sub>sj</sub>·ε<sub>sj</sub>/ψ<sub>sj</sub>",
                "для каждой точки арматуры стадии", "учёт неравномерности деформаций"))
            .Add(new ReportFormula("(8.161)",
                "ψ<sub>sj</sub> = 1/(1 + 0,8·ε<sub>sj,cr</sub>/ε<sub>sj</sub>)",
                "для применимых точек арматуры", "коэффициент ψsj по деформационной модели"));

        AddStageTables(document, data);
        string sumReference = data.Cracked ? "(8.141)" : "(8.140)";
        string sumFormula = data.Cracked
            ? "1/r = 1/r<sub>1</sub> − 1/r<sub>2</sub> + 1/r<sub>3</sub>"
            : "1/r = 1/r<sub>1</sub> + 1/r<sub>2</sub>";
        document
            .Add(new ReportHeading(1, "Суммирование кривизны"))
            .Add(new ReportFormula(sumReference, sumFormula,
                $"κy: {F(data.Stage1?.Ky)}; {F(data.Stage2?.Ky)}; {F(data.Stage3?.Ky)}; κz: {F(data.Stage1?.Kz)}; {F(data.Stage2?.Kz)}; {F(data.Stage3?.Kz)}",
                $"κy,full = {F(data.KyFull)} 1/м; κz,full = {F(data.KzFull)} 1/м; κfull = {F(data.KFull)} 1/м"))
            .Add(new ReportTable(
                ["Итог", "Значение", "Единица"],
                [
                    (IReadOnlyList<string>)["ky_full", F(data.KyFull), "1/м"],
                    (IReadOnlyList<string>)["kz_full", F(data.KzFull), "1/м"],
                    (IReadOnlyList<string>)["k_full", F(data.KFull), "1/м"]
                ]));

        AddImages(document, context.Images);
        var plane = StagePlane(data.Stage1 ?? data.Stage2 ?? data.Stage3);
        if (context.Section is { } section)
        {
            SectionReportSections.Geometry(document, section, plane);
            SectionReportSections.MaterialsAndDiagrams(document, section, CalcType.N, plane);
            SectionReportSections.Rebar(document, section, plane, CalcType.N);
            SectionReportSections.Prestress(document, data.Prestress);
        }
        else
            SectionReportSections.SectionMissingWarning(document);

        if (!data.AllConverged)
            document.Add(new ReportWarning("Не все стадии расчёта кривизны достигли сходимости."));
        AddStageWarnings(document, data.Stage1, "stage1");
        AddStageWarnings(document, data.Stage2, "stage2");
        AddStageWarnings(document, data.Stage3, "stage3");
        return document;
    }

    static void AddStageTables(ReportDocument document, TotalCurvatureReportData data)
    {
        document.Add(new ReportHeading(1, "Стадии расчёта по п. 8.2.24"))
            .Add(new ReportParagraph("stage1 — вся нагрузка непродолжительно; stage2 — длительная часть непродолжительно; stage3 — длительная часть продолжительно. Вид N — кратковременные характеристики, NL — длительные характеристики."));
        AddStageTable(document, "stage1", "Стадия 1 — вся нагрузка, непродолжительно", data.Stage1);
        AddStageTable(document, "stage2", "Стадия 2 — длительная часть, непродолжительно", data.Stage2);
        AddStageTable(document, "stage3", "Стадия 3 — длительная часть, продолжительно", data.Stage3);
    }

    static void AddStageTable(ReportDocument document, string key, string title, TotalCurvatureStageData? stage)
    {
        if (stage == null)
        {
            document.Add(new ReportParagraph($"{title}: стадия отсутствует в результате."));
            return;
        }
        document.Add(new ReportHeading(2, title))
            .Add(new ReportKeyValueTable(
            [
                ("Mx / My", $"{SectionReportSections.Moment(stage.Mx)} / {SectionReportSections.Moment(stage.My)}"),
                ("ε₀", SectionReportSections.F(stage.E0)),
                ("κy / κz", $"{SectionReportSections.Curvature(stage.Ky)} / {SectionReportSections.Curvature(stage.Kz)}"),
                ("Вид расчёта", CalcTypeTitle(stage.CalcType)),
                ("Растянутый бетон", stage.ConcreteTension ? "учитывается" : "не учитывается"),
                ("Сходимость", stage.Converged ? "достигнута" : "не достигнута")
            ], "Параметр", "Значение"))
            .Add(new ReportTable(
                ["№", "x, м", "y, м", "ψs", "учитывается"],
                stage.PsiSByRebar.Select(row => (IReadOnlyList<string>)[
                    row.Num?.ToString() ?? "—", SectionReportSections.F(row.X), SectionReportSections.F(row.Y),
                    SectionReportSections.F(row.PsiS), row.Applicable ? "да" : "нет"]).ToList()));
    }

    static void AddImages(ReportDocument document, IReadOnlyDictionary<string, string> images)
    {
        AddImage(document, images, "stage1", "Карта деформаций ε: стадия 1 — вся нагрузка");
        AddImage(document, images, "stage2", "Карта деформаций ε: стадия 2 — длительная часть кратковременно");
        AddImage(document, images, "stage3", "Карта деформаций ε: стадия 3 — длительная часть продолжительно");
    }

    static void AddImage(ReportDocument document, IReadOnlyDictionary<string, string> images,
        string key, string title)
    {
        if (images.TryGetValue(key, out var svg))
            document.Add(new ReportImage(title, svg));
    }

    static void AddImageRequest(List<ReportImageRequest> requests, string key, string title,
        TotalCurvatureStageData? stage)
    {
        if (stage == null) return;
        requests.Add(new ReportImageRequest(key, title, StagePlane(stage), CalcTypeFor(stage.CalcType), ReportImageMode.Strain));
    }

    static Kurvature StagePlane(TotalCurvatureStageData? stage) => new()
    {
        e0 = stage?.E0 ?? 0,
        ky = stage?.Ky ?? 0,
        kz = stage?.Kz ?? 0
    };

    static CalcType CalcTypeFor(string? value)
        => string.Equals(value, "NL", StringComparison.OrdinalIgnoreCase) ? CalcType.NL : CalcType.N;

    static string CalcTypeTitle(string? value)
        => CalcTypeFor(value) == CalcType.NL ? "NL — длительные характеристики" : "N — кратковременные характеристики";

    static string ReadForcesMode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "не задан";
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("ForcesMode", out var mode))
                return mode.GetString() ?? "не задан";
        }
        catch (JsonException)
        {
            return "не удалось разобрать ParamsJson";
        }
        return "не задан";
    }

    static void AddStageWarnings(ReportDocument document, TotalCurvatureStageData? stage, string key)
    {
        if (stage is { Converged: false })
            document.Add(new ReportWarning($"Стадия {key} не достигла сходимости."));
    }

    static string F(double value) => SectionReportSections.F(value);
    static string F(double? value) => SectionReportSections.F(value);
    static string Sub(string symbol, string sub) => SectionReportSections.Sub(symbol, sub);
}
