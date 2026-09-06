using CScore;

namespace OpenCS.Reporting;

/// <summary>Поставщик отчёта по прочности нормального сечения для трёх limit_* видов.</summary>
public sealed class LimitForceReportProvider : IReportProvider
{
    /// <inheritdoc/>
    public string TaskKind => "limit_moment";

    /// <inheritdoc/>
    public IReadOnlyCollection<string> SupportedKinds => ["limit_force", "limit_moment", "limit_axial"];

    /// <inheritdoc/>
    public bool CanHandle(CalcTask task) => SupportedKinds.Contains(task.Kind, StringComparer.Ordinal);

    /// <inheritdoc/>
    public IReadOnlyList<ReportImageRequest> DescribeImages(CalcTask task, CalcResult result)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(result);
        var data = LimitForceReportData.Parse(result.DataJson);
        var plane = new Kurvature { e0 = data.E0, ky = data.Ky, kz = data.Kz };
        return
        [
            new("strain", "Карта деформаций ε в предельном состоянии", plane, task.CalcType, ReportImageMode.Strain),
            new("stress", "Карта напряжений σ в предельном состоянии", plane, task.CalcType, ReportImageMode.Stress)
        ];
    }

    /// <inheritdoc/>
    public ReportDocument Build(ReportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!SupportedKinds.Contains(context.Task.Kind, StringComparer.Ordinal))
            throw new ArgumentException("Поставщик не поддерживает данный тип задачи.", nameof(context));

        var data = LimitForceReportData.Parse(context.Result.DataJson);
        string tag = string.IsNullOrWhiteSpace(context.Task.Tag) ? "без метки" : context.Task.Tag;
        var document = new ReportDocument($"Расчёт по прочности нормального сечения — {tag}");
        SectionReportSections.Identification(document, context,
            "координаты и размеры — м; координаты арматуры — мм; площади — м²; ε — безразмерная; κ — 1/м; σ и E — МПа; N — кН; M — кН·м");

        document
            .Add(new ReportHeading(1, "Исходные данные"))
            .Add(new ReportKeyValueTable(
            [
                ("Тип задачи", context.Task.Kind),
                ("Метод решателя", data.SolverMethod),
                ("Итерации", data.Iterations.ToString()),
                ("Итерации Ньютона", data.NewtonIterations.ToString()),
                ("N целевое", SectionReportSections.Force(data.TargetN)),
                ("Mx целевое", SectionReportSections.Moment(data.TargetMx)),
                ("My целевое", SectionReportSections.Moment(data.TargetMy))
            ], "Параметр", "Значение"))
            .Add(new ReportHeading(1, "Расчётный аппарат НДМ"))
            .Add(new ReportFormula(
                "(8.26)",
                $"{Sub("M", "x")} = Σ({Sub("σ", "b")}·{Sub("A", "b")}·{Sub("y", "b")}) + Σ({Sub("σ", "s")}·{Sub("A", "s")}·{Sub("y", "s")})",
                $"{Sub("M", "x")} = {F(data.TargetMx)}", $"{Sub("M", "x")} = {F(data.ResultMx)} кН·м"))
            .Add(new ReportFormula(
                "(8.27)",
                $"{Sub("M", "y")} = Σ({Sub("σ", "b")}·{Sub("A", "b")}·{Sub("x", "b")}) + Σ({Sub("σ", "s")}·{Sub("A", "s")}·{Sub("x", "s")})",
                $"{Sub("M", "y")} = {F(data.TargetMy)}", $"{Sub("M", "y")} = {F(data.ResultMy)} кН·м"))
            .Add(new ReportFormula(
                "(8.28)", $"N = Σ({Sub("σ", "b")}·{Sub("A", "b")}) + Σ({Sub("σ", "s")}·{Sub("A", "s")})",
                $"N = {F(data.TargetN)}", $"N = {F(data.ResultN)} кН"))
            .Add(new ReportFormula(
                "(8.29)",
                $"{Sub("ε", "bi")} = {Sub("ε", "0")} + {Sub("κ", "y")}·{Sub("y", "bi")} + {Sub("κ", "z")}·{Sub("x", "bi")}",
                $"{Sub("ε", "0")} = {F(data.E0)}; {Sub("κ", "y")} = {SectionReportSections.Curvature(data.Ky)}; {Sub("κ", "z")} = {SectionReportSections.Curvature(data.Kz)}",
                "распределение деформаций по плоскому сечению"))
            .Add(new ReportFormula(
                "(8.30)",
                $"{Sub("ε", "si")} = {Sub("ε", "0")} + {Sub("κ", "y")}·{Sub("y", "si")} + {Sub("κ", "z")}·{Sub("x", "si")}",
                $"{Sub("ε", "0")} = {F(data.E0)}; {Sub("κ", "y")} = {SectionReportSections.Curvature(data.Ky)}; {Sub("κ", "z")} = {SectionReportSections.Curvature(data.Kz)}",
                "деформации точек арматуры"))
            .Add(new ReportFormula("(8.31)", $"{Sub("σ", "bi")} = {Sub("E", "b")}·{Sub("ν", "b")}·{Sub("ε", "bi")}", "σb определяется диаграммой бетона", "учтено в интеграле"))
            .Add(new ReportFormula("(8.32)", $"{Sub("σ", "si")} = {Sub("E", "s")}·{Sub("ν", "s")}·{Sub("ε", "si")}", "σs определяется диаграммой арматуры", "учтено в интеграле"))
            .Add(new ReportFormula("(8.35)–(8.36)", $"{Sub("ν", "b")} = {Sub("σ", "b")}/({Sub("E", "b")}·{Sub("ε", "b")}); {Sub("ν", "s")} = {Sub("σ", "s")}/({Sub("E", "s")}·{Sub("ε", "s")})", "коэффициенты секущего модуля", "использованы текущие значения"));

        AddStiffnessFormulas(document, data);
        document
            .Add(new ReportHeading(1, "Критерии предельного состояния"))
            .Add(new ReportFormula(
                "(8.37)", $"|{Sub("ε", "b,max")}| ≤ {Sub("ε", "cu")}",
                $"|{F(data.EpsContourMin)}| ≤ |{F(data.EpsCu)}|", "проверка деформации бетона"))
            .Add(new ReportFormula(
                "(8.38)", $"|{Sub("ε", "s,max")}| ≤ {Sub("ε", "su")}",
                $"|{SectionReportSections.F(data.EpsRebarMax)}| ≤ |{SectionReportSections.F(data.EpsSu)}|", "проверка деформации арматуры"))
            .Add(new ReportParagraph("Предельные деформации принимаются по п. 8.1.30 СП 63: для арматуры 0,025 при физическом и 0,015 при условном пределе текучести."))
            .Add(new ReportHeading(1, "Предельное усилие и коэффициент запаса"))
            .Add(new ReportTable(
                ["Величина", "Целевое", "Предельное", "Фактическое"],
                [
                    (IReadOnlyList<string>)["N, кН", Force(data.TargetN), Force(data.LimitN), Force(data.ResultN)],
                    (IReadOnlyList<string>)["Mx, кН·м", Moment(data.TargetMx), Moment(data.LimitMx), Moment(data.ResultMx)],
                    (IReadOnlyList<string>)["My, кН·м", Moment(data.TargetMy), Moment(data.LimitMy), Moment(data.ResultMy)]
                ]))
            .Add(new ReportKeyValueTable(
            [
                ("Коэффициент запаса", F(data.Factor)),
                ("Коэффициент использования", F(data.Utilization)),
                ("Определяющий критерий", Governing(data.Governing)),
                ("Сходимость", data.Converged ? "достигнута" : "не достигнута")
            ], "Параметр", "Значение"));

        SectionReportSections.Eta(document, data.Eta);
        AddImages(document, context.Images);
        var plane = new Kurvature { e0 = data.E0, ky = data.Ky, kz = data.Kz };
        if (context.Section is { } section)
        {
            SectionReportSections.Geometry(document, section, plane);
            SectionReportSections.MaterialsAndDiagrams(document, section, context.Task.CalcType, plane);
            SectionReportSections.Rebar(document, section, plane, context.Task.CalcType);
            SectionReportSections.Prestress(document, data.Prestress);
        }
        else
            SectionReportSections.SectionMissingWarning(document);

        if (!data.Converged)
            document.Add(new ReportWarning("Расчёт по прочности не достиг заданного критерия сходимости; результаты требуют инженерной проверки."));
        return document;
    }

    static void AddStiffnessFormulas(ReportDocument document, LimitForceReportData data)
    {
        document.Add(new ReportHeading(1, "Матрица жёсткости по СП 63"))
            .Add(new ReportFormula("(8.39)", $"{Sub("M", "x")} = {Sub("D", "11")}·{Sub("κ", "y")} + {Sub("D", "12")}·{Sub("κ", "z")} + {Sub("D", "13")}·{Sub("ε", "0")}", "система жёсткости", $"{Sub("M", "x")} = {F(data.ResultMx)} кН·м"))
            .Add(new ReportFormula("(8.40)", $"{Sub("M", "y")} = {Sub("D", "12")}·{Sub("κ", "y")} + {Sub("D", "22")}·{Sub("κ", "z")} + {Sub("D", "23")}·{Sub("ε", "0")}", "система жёсткости", $"{Sub("M", "y")} = {F(data.ResultMy)} кН·м"))
            .Add(new ReportFormula("(8.41)", $"N = {Sub("D", "13")}·{Sub("κ", "y")} + {Sub("D", "23")}·{Sub("κ", "z")} + {Sub("D", "33")}·{Sub("ε", "0")}", "система жёсткости", $"N = {F(data.ResultN)} кН"))
            .Add(new ReportFormula("(8.42)", $"{Sub("D", "11")} = Σ(EbνbAb yb²) + Σ(EsνsAs ys²)", "интегрирование по бетону и арматуре", "D11"))
            .Add(new ReportFormula("(8.43)", $"{Sub("D", "22")} = Σ(EbνbAb xb²) + Σ(EsνsAs xs²)", "интегрирование по бетону и арматуре", "D22"))
            .Add(new ReportFormula("(8.44)", $"{Sub("D", "12")} = Σ(EbνbAb xb yb) + Σ(EsνsAs xs ys)", "интегрирование по бетону и арматуре", "D12"))
            .Add(new ReportFormula("(8.45)", $"{Sub("D", "13")} = Σ(EbνbAb yb) + Σ(EsνsAs ys)", "интегрирование по бетону и арматуре", "D13"))
            .Add(new ReportFormula("(8.46)", $"{Sub("D", "23")} = Σ(EbνbAb xb) + Σ(EsνsAs xs)", "интегрирование по бетону и арматуре", "D23"))
            .Add(new ReportFormula("(8.47)", $"{Sub("D", "33")} = Σ(EbνbAb) + Σ(EsνsAs)", "интегрирование по бетону и арматуре", "D33"));
    }

    static void AddImages(ReportDocument document, IReadOnlyDictionary<string, string> images)
    {
        if (images.TryGetValue("strain", out var strain))
            document.Add(new ReportImage("Карта деформаций ε в предельном состоянии", strain));
        if (images.TryGetValue("stress", out var stress))
            document.Add(new ReportImage("Карта напряжений σ в предельном состоянии", stress));
    }

    static string Governing(string value) => value switch
    {
        "concrete" => "исчерпание по бетону сжатой зоны",
        "rebar" => "исчерпание по деформации арматуры",
        "both" => "одновременное исчерпание по бетону и арматуре",
        _ => string.IsNullOrWhiteSpace(value) ? "—" : value
    };

    static string F(double value) => SectionReportSections.F(value);
    static string Force(double value) => SectionReportSections.Force(value);
    static string Moment(double value) => SectionReportSections.Moment(value);
    static string Sub(string symbol, string sub) => SectionReportSections.Sub(symbol, sub);
}
