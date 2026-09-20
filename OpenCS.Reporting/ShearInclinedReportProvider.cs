using System.Globalization;
using System.Text.RegularExpressions;
using CScore;

namespace OpenCS.Reporting;

/// <summary>Поставщик отчёта по упрощённой проверке наклонных сечений СП 63.</summary>
public sealed class ShearInclinedReportProvider : IReportProvider
{
    static readonly Regex Token = new(@"\{(?<name>[A-Za-z0-9_.-]+)\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <inheritdoc/>
    public string TaskKind => "shear_inclined";

    /// <inheritdoc/>
    public IReadOnlyCollection<string> SupportedKinds => [TaskKind];

    /// <inheritdoc/>
    public bool CanHandle(CalcTask task) => SupportedKinds.Contains(task.Kind,
        StringComparer.Ordinal);

    /// <inheritdoc/>
    public ReportDocument Build(ReportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!CanHandle(context.Task))
            throw new ArgumentException("Поставщик не поддерживает данный тип задачи.", nameof(context));

        var data = ShearInclinedReportData.Parse(context.Result.DataJson);
        string tag = string.IsNullOrWhiteSpace(context.Task.Tag) ? "без метки" : context.Task.Tag;
        var document = new ReportDocument($"Упрощённая проверка наклонных сечений СП 63 — {tag}");
        SectionReportSections.Identification(document, context,
            "N — кН; Q — кН; M — кН·м; линейные размеры — м; коэффициенты — безразмерные");

        document
            .Add(new ReportHeading(1, "Допущения и область применимости"))
            .Add(new ReportParagraph(
                "Проверка выполнена упрощённым формульным методом СП 63.13330. "
                + "Определяющий коэффициент формируется только по проверкам 8.55, 8.56 и 8.63. "
                + "Проверки 8.60 и 8.63s приведены справочно."))
            .Add(new ReportKeyValueTable(
            [
                ("Плоскости", string.Join(", ", data.Inputs.Keys.OrderBy(key => key))),
                ("Статус применимости", ApplicabilityText(data.ApplicabilityStatus)),
                ("Источник усилий", data.ForceLabel)
            ], "Параметр", "Значение"));

        AddInputs(document, data);
        AddGeometry(document, context, data);
        AddTrace(document, data);
        AddSummary(document, data);
        AddStations(document, data);
        AddWarnings(document, data);
        return document;
    }

    static void AddInputs(ReportDocument document, ShearInclinedReportData data)
    {
        document.Add(new ReportHeading(1, "Исходные данные"));
        var rows = new List<(string Key, string Value)>();
        foreach (var (plane, values) in data.Inputs.OrderBy(pair => pair.Key))
        {
            foreach (var key in new[] { "b", "h0", "qsw", "sw", "ns", "rb", "rbt" })
                if (values.TryGetValue(key, out var value))
                    rows.Add(($"{plane}: {key}", FormatValue(value, InputUnit(key))));
        }
        if (rows.Count == 0)
            rows.Add(("Данные", "—"));
        document.Add(new ReportKeyValueTable(rows, "Параметр", "Значение"));
    }

    static void AddGeometry(ReportDocument document, ReportContext context,
        ShearInclinedReportData data)
    {
        document.Add(new ReportHeading(1, "Схема поперечного сечения"));
        if (context.Section is null)
        {
            document.Add(new ReportWarning(
                "Модель сечения не передана: схема поперечного сечения недоступна."));
            return;
        }

        var firstTrace = data.TraceSteps.FirstOrDefault();
        var firstStation = data.Stations.FirstOrDefault();
        var options = new ReportSectionDiagramOptions
        {
            Axis = firstTrace?.Plane,
            TensionSide = firstStation?.TensionOnPositiveSide switch
            {
                true => ReportTensionSide.Positive,
                false => ReportTensionSide.Negative,
                _ => ReportTensionSide.Unknown
            },
            H0 = firstTrace?.Values.TryGetValue("h0", out var h0) == true ? h0.Value : null
        };
        if (context.ParametricSection is { } definition)
            document.Add(new ReportImage("Параметрическая схема поперечного сечения",
                new ParametricRcSectionSvgRenderer().Render(definition, options)));
        else
        {
            document.Add(new ReportImage("Универсальная схема поперечного сечения",
                new CrossSectionReportSvgRenderer().Render(context.Section)));
            if (!string.IsNullOrWhiteSpace(context.ParametricSectionWarning))
                document.Add(new ReportWarning(context.ParametricSectionWarning));
        }
    }

    static void AddTrace(ReportDocument document, ShearInclinedReportData data)
    {
        document.Add(new ReportHeading(1, "Ход расчёта"));
        if (data.TraceSteps.Count == 0)
        {
            document.Add(new ReportWarning(
                "В сохранённом результате отсутствует трассировка; пошаговый расчёт недоступен."));
            return;
        }

        foreach (var trace in data.TraceSteps)
        {
            string title = Title(trace.FormulaCode);
            document.Add(new ReportCalculationStep
            {
                StepId = trace.StepId,
                Reference = string.IsNullOrWhiteSpace(trace.FormulaCode)
                    ? trace.CodeReference : trace.FormulaCode,
                Title = title,
                Formula = ReportMathExpression.Latex(trace.FormulaLatex),
                Substitution = ReportMathExpression.Latex(
                    Substitute(trace.SubstitutionTemplate, trace.Values)),
                Result = ReportMathExpression.Latex(
                    NormalizeUtilizationLabel(Substitute(trace.ResultTemplate, trace.Values))),
                Unit = ResultUnit(trace.FormulaCode),
                Status = MapStatus(trace.Status),
                StatusText = StatusText(trace.Status),
                Note = StationNote(trace)
            });
        }
    }

    static void AddSummary(ReportDocument document, ShearInclinedReportData data)
    {
        document
            .Add(new ReportHeading(1, "Итоговая проверка"))
            .Add(new ReportKeyValueTable(
            [
                ("Коэффициент использования", data.UtilizationExact is double value
                    ? ReportNumberFormatter.FormatUtilization(value) : "—"),
                ("Статус", ApplicabilityText(data.UtilizationStatus)),
                ("Определяющий набор", "8.55, 8.56, 8.63"),
                ("Справочные проверки", "8.60, 8.63s")
            ], "Параметр", "Значение"));
    }

    static void AddStations(ReportDocument document, ShearInclinedReportData data)
    {
        if (data.Stations.Count == 0)
            return;

        document.Add(new ReportPageBreak())
            .Add(new ReportHeading(1, "Приложение. Стоянки вдоль элемента"))
            .Add(new ReportParagraph(
                "Приведены все сохранённые стоянки без прореживания. Сторона растяжения "
                + "указана относительно положительной координаты выбранной плоскости."))
            .Add(new ReportTable(
                ["Плоскость", "s, м", "N, кН", "φn", "Q, кН", "Cq, м", "Qb, кН", "Qsw, кН", "Кисп,Q", "Сторона", "Cm, м", "M, кН·м", "Ms, кН·м", "Msw, кН·м", "Кисп,M"],
                data.Stations.Select(station => (IReadOnlyList<string>)[
                    station.Plane,
                    F(station.S, ReportUnit.Meter),
                    F(station.N, ReportUnit.Kilonewton),
                    F(station.PhiN, ReportUnit.Unitless),
                    F(station.Q, ReportUnit.Kilonewton),
                    F(station.CriticalC, ReportUnit.Meter),
                    F(station.Qb, ReportUnit.Kilonewton),
                    F(station.Qsw, ReportUnit.Kilonewton),
                    ReportNumberFormatter.FormatUtilization(station.Eta ?? double.NaN),
                    station.TensionOnPositiveSide == true ? "+" : station.TensionOnPositiveSide == false ? "−" : "—",
                    F(station.CriticalCMoment, ReportUnit.Meter),
                    F(station.MomentApplied, ReportUnit.KilonewtonMeter),
                    F(station.Ms, ReportUnit.KilonewtonMeter),
                    F(station.Msw, ReportUnit.KilonewtonMeter),
                    ReportNumberFormatter.FormatUtilization(station.EtaM ?? double.NaN)
                ]).ToList()));
    }

    static void AddWarnings(ReportDocument document, ShearInclinedReportData data)
    {
        foreach (var warning in data.Warnings.Where(text => !string.IsNullOrWhiteSpace(text)))
            document.Add(new ReportWarning(warning));
        if (data.UtilizationStatus is "no_capacity" or "error")
            document.Add(new ReportWarning(
                "Несущая способность или итоговый коэффициент не определены; "
                + "это состояние не заменено округлённым числом."));
    }

    static string Substitute(string template,
        IReadOnlyDictionary<string, ShearInclinedReportTraceValue> values)
        => Token.Replace(template ?? "", match =>
        {
            if (!values.TryGetValue(match.Groups["name"].Value, out var value) ||
                value.Value is not double number)
                return "—";
            return ReportNumberFormatter.Format(number, ParseUnit(value.Unit));
        });

    /// <summary>Заменяет старое обозначение коэффициента использования η в сохранённых результатах.
    /// η оставляется только для поправки прогиба по п. 8.1.15.</summary>
    static string NormalizeUtilizationLabel(string value)
        => value.Replace(@"\eta", "Кисп", StringComparison.Ordinal)
            .Replace("η", "Кисп", StringComparison.Ordinal);

    static string StationNote(ShearInclinedReportTraceStep trace)
    {
        var parts = new List<string>();
        if (trace.StationIndex is int index) parts.Add($"стоянка №{index + 1}");
        if (trace.StationS is double s) parts.Add($"s = {F(s, ReportUnit.Meter)} м");
        if (trace.CriticalC is double c) parts.Add($"C = {F(c, ReportUnit.Meter)} м");
        return string.Join(", ", parts);
    }

    static string Title(string formula) => formula switch
    {
        "8.55" => "Бетонная полоса между наклонными сечениями",
        "8.56" => "Несущая способность наклонного сечения по поперечной силе",
        "8.60" => "Справочная приопорная проверка",
        "8.63" => "Несущая способность наклонного сечения по моменту",
        "8.63s" => "Справочная проверка по моменту при C = 2h₀",
        _ => "Расчётная проверка"
    };

    static string ResultUnit(string formula) => formula is "8.63" or "8.63s" ? "кН·м" : "кН";

    static ReportCalculationStatus MapStatus(string status) => status switch
    {
        "passed" or "ok" => ReportCalculationStatus.Passed,
        "failed" => ReportCalculationStatus.Failed,
        "not_applicable" => ReportCalculationStatus.NotApplicable,
        "no_capacity" => ReportCalculationStatus.NoCapacity,
        "error" => ReportCalculationStatus.Error,
        _ => ReportCalculationStatus.Informational
    };

    static string StatusText(string status) => status switch
    {
        "passed" or "ok" => "выполнено",
        "failed" => "не выполнено",
        "not_applicable" => "не применяется",
        "no_capacity" => "несущая способность не определена",
        "error" => "ошибка",
        _ => "справочно"
    };

    static string ApplicabilityText(string status) => status switch
    {
        "ok" => "расчёт выполнен",
        "not_applicable" => "не применимо",
        "no_capacity" => "несущая способность не определена",
        "error" => "ошибка расчёта",
        _ => status
    };

    static string F(double? value, ReportUnit unit)
        => value is double number
            ? ReportNumberFormatter.Format(number, unit)
            : ReportNumberFormatter.UndefinedPlaceholder;

    static string FormatValue(double? value, ReportUnit unit) => F(value, unit);

    static ReportUnit InputUnit(string key) => key switch
    {
        "b" or "h0" or "sw" => ReportUnit.Meter,
        "qsw" or "ns" => ReportUnit.Kilonewton,
        "rb" or "rbt" => ReportUnit.Kilopascal,
        _ => ReportUnit.Unitless
    };

    static ReportUnit ParseUnit(string? unit)
        => Enum.TryParse<ReportUnit>(unit, ignoreCase: true, out var result)
            ? result : ReportUnit.Unitless;
}
