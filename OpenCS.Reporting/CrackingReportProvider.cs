using CScore;

namespace OpenCS.Reporting;

/// <summary>Поставщик отчёта по образованию трещин в нормальном сечении.</summary>
public sealed class CrackingReportProvider : IReportProvider
{
    /// <inheritdoc/>
    public string TaskKind => "cracking";

    /// <inheritdoc/>
    public IReadOnlyCollection<string> SupportedKinds => [TaskKind];

    /// <inheritdoc/>
    public bool CanHandle(CalcTask task) => SupportedKinds.Contains(task.Kind, StringComparer.Ordinal);

    /// <inheritdoc/>
    public IReadOnlyList<ReportImageRequest> DescribeImages(CalcTask task, CalcResult result)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(result);
        var data = CrackingReportData.Parse(result.DataJson);
        var plane = new Kurvature { e0 = data.E0 ?? 0, ky = data.Ky ?? 0, kz = data.Kz ?? 0 };
        return [new("strain", "Карта деформаций ε в момент образования трещины",
            plane, task.CalcType, ReportImageMode.Strain)];
    }

    /// <inheritdoc/>
    public ReportDocument Build(ReportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!CanHandle(context.Task))
            throw new ArgumentException("Поставщик не поддерживает данный тип задачи.", nameof(context));

        var data = CrackingReportData.Parse(context.Result.DataJson);
        string tag = string.IsNullOrWhiteSpace(context.Task.Tag) ? "без метки" : context.Task.Tag;
        var document = new ReportDocument($"Расчёт по образованию трещин — {tag}");
        SectionReportSections.Identification(document, context,
            "координаты и размеры — м; координаты арматуры — мм; площади — м²; ε — безразмерная; κ — 1/м; σ и E — МПа; N — кН; M — кН·м");

        document
            .Add(new ReportHeading(1, "Исходные данные"))
            .Add(new ReportKeyValueTable(
            [
                ("Тип задачи", context.Task.Kind),
                ("N", SectionReportSections.Force(data.N)),
                ("Направление момента", $"Mx = {SectionReportSections.Moment(data.MxCrc)}, My = {SectionReportSections.Moment(data.MyCrc)}"),
                ("Сходимость", data.Converged ? "достигнута" : "не достигнута")
            ], "Параметр", "Значение"))
            .Add(new ReportHeading(1, "Расчётный аппарат образования трещин"))
            .Add(new ReportFormula("(8.26)",
                $"{Sub("M", "x")} = Σ({Sub("σ", "b")}·{Sub("A", "b")}·{Sub("y", "b")}) + Σ({Sub("σ", "s")}·{Sub("A", "s")}·{Sub("y", "s")})",
                $"{Sub("M", "x")} = {F(data.MxCrc)}", "интегрирование с работающим на растяжение бетоном"))
            .Add(new ReportFormula("(8.27)",
                $"{Sub("M", "y")} = Σ({Sub("σ", "b")}·{Sub("A", "b")}·{Sub("x", "b")}) + Σ({Sub("σ", "s")}·{Sub("A", "s")}·{Sub("x", "s")})",
                $"{Sub("M", "y")} = {F(data.MyCrc)}", "интегрирование с работающим на растяжение бетоном"))
            .Add(new ReportFormula("(8.28)", $"N = Σ({Sub("σ", "b")}·{Sub("A", "b")}) + Σ({Sub("σ", "s")}·{Sub("A", "s")})",
                $"N = {F(data.N)}", $"N = {F(data.N)} кН"))
            .Add(new ReportFormula("(8.29)",
                $"{Sub("ε", "bi")} = {Sub("ε", "0")} + {Sub("κ", "y")}·{Sub("y", "bi")} + {Sub("κ", "z")}·{Sub("x", "bi")}",
                $"{Sub("ε", "0")} = {F(data.E0)}; {Sub("κ", "y")} = {SectionReportSections.Curvature(data.Ky)}; {Sub("κ", "z")} = {SectionReportSections.Curvature(data.Kz)}",
                "плоское сечение"))
            .Add(new ReportFormula("(8.30)",
                $"{Sub("ε", "si")} = {Sub("ε", "0")} + {Sub("κ", "y")}·{Sub("y", "si")} + {Sub("κ", "z")}·{Sub("x", "si")}",
                "деформации арматуры по той же плоскости", "учтены в равновесии"))
            .Add(new ReportFormula("(8.31)", $"{Sub("σ", "bi")} = {Sub("E", "b")}·{Sub("ν", "b")}·{Sub("ε", "bi")}",
                "σb определяется диаграммой бетона", "бетон работает на растяжение до εbt,ult"))
            .Add(new ReportFormula("(8.32)", $"{Sub("σ", "si")} = {Sub("E", "s")}·{Sub("ν", "s")}·{Sub("ε", "si")}",
                "σs определяется диаграммой арматуры", "учтено в равновесии"))
            .Add(new ReportHeading(1, "Результат и проверка условия трещинообразования"))
            .Add(new ReportTable(
                ["Величина", "Значение", "Единица"],
                [
                    (IReadOnlyList<string>)["Mcrc", F(data.Mcrc), "кН·м"],
                    (IReadOnlyList<string>)["Mx_crc", F(data.MxCrc), "кН·м"],
                    (IReadOnlyList<string>)["My_crc", F(data.MyCrc), "кН·м"]
                ]))
            .Add(new ReportFormula("(8.1.30)",
                $"{Sub("ε", "bt")} ≤ {Sub("ε", "bt,ult")}",
                $"{Sub("ε", "bt")} = {F(data.EpsMaxTension)}; {Sub("ε", "bt,ult")} = {F(data.EpsTensionLimit)}",
                Math.Abs(data.EpsMaxTension) <= Math.Abs(data.EpsTensionLimit) ? "условие выполнено" : "условие не выполнено"))
            .Add(new ReportHeading(1, "Плоскость деформаций"))
            .Add(new ReportKeyValueTable(
            [
                ("ε₀", F(data.E0)),
                ("κy", SectionReportSections.Curvature(data.Ky)),
                ("κz", SectionReportSections.Curvature(data.Kz)),
                ("Сходимость плоскости", data.PlaneConverged ? "достигнута" : "не достигнута")
            ], "Параметр", "Значение"));

        if (context.Images.TryGetValue("strain", out var strain))
            document.Add(new ReportImage("Карта деформаций ε в момент образования трещины", strain));
        SectionReportSections.Eta(document, null);
        if (context.Section is { } section)
        {
            var plane = new Kurvature { e0 = data.E0 ?? 0, ky = data.Ky ?? 0, kz = data.Kz ?? 0 };
            SectionReportSections.Geometry(document, section, plane);
            SectionReportSections.MaterialsAndDiagrams(document, section, context.Task.CalcType, plane);
            SectionReportSections.Rebar(document, section, plane, context.Task.CalcType);
            SectionReportSections.Prestress(document, data.Prestress);
        }
        else
            SectionReportSections.SectionMissingWarning(document);

        if (!data.Converged)
            document.Add(new ReportWarning("Поиск момента образования трещин не достиг заданного критерия сходимости."));
        if (!data.PlaneConverged)
            document.Add(new ReportWarning("Плоскость деформаций в момент образования трещин не сошлась; результат требует инженерной проверки."));
        return document;
    }

    static string F(double value) => SectionReportSections.F(value);
    static string F(double? value) => SectionReportSections.F(value);
    static string Sub(string symbol, string sub) => SectionReportSections.Sub(symbol, sub);
}
