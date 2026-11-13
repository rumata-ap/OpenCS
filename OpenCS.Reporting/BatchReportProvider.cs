using CScore;
using CScore.Batch;

namespace OpenCS.Reporting;

/// <summary>
/// Отчёт по пакету любого вида: итог по причинам строк, сводная таблица (усилия, статус, Кисп, причина)
/// и подробный ход определяющей строки — отчёт её одиночной задачи, если его передал вызывающий
/// (<see cref="Build(ReportContext, ReportDocument?)"/>; Reporting не считает задачи сам).
/// </summary>
public sealed class BatchReportProvider : IReportProvider
{
    /// <summary>Строк в полной таблице; больше — только не прошедшие, не сошедшиеся, ошибки и наибольшие Кисп.</summary>
    public const int FullTableLimit = 500;

    /// <summary>Сколько строк с наибольшим Кисп показывать в сокращённой таблице.</summary>
    public const int TopRows = 20;

    static readonly IReadOnlyDictionary<string, string> Titles = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["limit_force_batch"] = "Предельные усилия (Кисп)",
        ["limit_moment_batch"] = "Предельный момент (Кисп)",
        ["limit_axial_batch"] = "Предельная продольная сила (Кисп)",
        ["strain_state_batch"] = "Напряжённо-деформированное состояние",
        ["strength_ndm_batch"] = "Проверка прочности по НДМ",
        ["total_curvature_batch"] = "Полная кривизна",
        ["cracking_batch"] = "Момент трещинообразования",
        ["crack_width_batch"] = "Ширина раскрытия трещин",
        ["fire_r_check_batch"] = "Огнестойкость, R-проверка",
        ["shear_inclined_batch"] = "Наклонные сечения",
        ["shell_simpl_wa_sls_batch"] = "Пластина, Вуд–Армер, ПС2",
        ["shell_simpl_wa_uls_batch"] = "Пластина, Вуд–Армер, ПС1",
        ["shell_simpl_capri_sls_batch"] = "Пластина, Капра–Мори, ПС2",
        ["shell_simpl_capri_uls_batch"] = "Пластина, Капра–Мори, ПС1",
        ["shell_simpl_capri_design_uls_batch"] = "Подбор сеток Капра–Мори, ПС1",
        ["shell_simpl_capri_design_sls_batch"] = "Подбор сеток Капра–Мори, ПС2",
        ["shell_strain_state_batch"] = "НДС слоистой пластины",
        ["shell_layered_uls_batch"] = "Прочность слоистой пластины, ПС1",
        ["shell_layered_sls_batch"] = "Ширина трещин слоистой пластины, ПС2",
    };

    static readonly IReadOnlyDictionary<string, string> StatusNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [BatchRowStatus.Ok] = "проходит",
        [BatchRowStatus.NotPassed] = "не проходит",
        [BatchRowStatus.NotConverged] = "не сошлось",
        [BatchRowStatus.NotApplicable] = "не применимо",
        [BatchRowStatus.Error] = "ошибка",
    };

    static readonly IReadOnlyDictionary<string, string> ForceHeaders = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["N"] = "N, кН", ["Mx"] = "Mx, кН·м", ["My"] = "My, кН·м",
        ["Mx_long"] = "Mx,l, кН·м", ["Mx_total"] = "Mx, кН·м", ["My_long"] = "My,l, кН·м", ["My_total"] = "My, кН·м",
        ["vy"] = "Vy, кН", ["vx"] = "Vx, кН",
    };

    /// <inheritdoc/>
    public string TaskKind => "strength_ndm_batch";

    /// <inheritdoc/>
    public IReadOnlyCollection<string> SupportedKinds => Titles.Keys.ToArray();

    /// <inheritdoc/>
    public bool CanHandle(CalcTask task) => Titles.ContainsKey(task.Kind);

    /// <inheritdoc/>
    public ReportDocument Build(ReportContext context) => Build(context, null);

    /// <summary>Строит отчёт; governingDetail — отчёт одиночной задачи определяющей строки (или null).</summary>
    public ReportDocument Build(ReportContext context, ReportDocument? governingDetail)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!CanHandle(context.Task))
            throw new ArgumentException("Поставщик не поддерживает данный тип задачи.", nameof(context));

        string kind = context.Task.Kind;
        string tag = string.IsNullOrWhiteSpace(context.Task.Tag) ? "без метки" : context.Task.Tag;
        var document = new ReportDocument($"Пакетный расчёт: {Titles[kind]} — {tag}");
        var data = BatchReportData.Parse(kind, context.Result.DataJson);

        SectionReportSections.Identification(document, context,
            "усилия — кН, кН·м (пластины — на 1 м); Кисп — безразмерный; ширина трещин — мм");

        if (data.Error.Length > 0)
        {
            document.Add(new ReportWarning($"Расчёт пакета завершился ошибкой: {data.Error}"));
            return document;
        }

        int total = data.Rows.Count;
        document
            .Add(new ReportHeading(1, "Итог"))
            .Add(new ReportKeyValueTable(
            [
                ("Вид проверки", Titles[kind]),
                ("Строк в пакете", total.ToString()),
                ("Проходят", data.Count(BatchRowStatus.Ok).ToString()),
                ("Не проходят", data.Count(BatchRowStatus.NotPassed).ToString()),
                ("Не сошлись", data.Count(BatchRowStatus.NotConverged).ToString()),
                ("Не применимо", data.Count(BatchRowStatus.NotApplicable).ToString()),
                ("Ошибки", data.Count(BatchRowStatus.Error).ToString()),
                ("Определяющая строка", data.GoverningNum is { } g ? Governing(data, g) : NoGoverning(data)),
            ], "Параметр", "Значение"));
        if (data.Count(BatchRowStatus.NotConverged) > 0)
            document.Add(new ReportParagraph(
                "«Не сошлось» — решатель не нашёл напряжённо-деформированное состояние; проверка для такой строки не выполнена, это не вывод «не проходит»."));

        AddTable(document, data);
        AddGoverning(document, data, governingDetail);
        return document;
    }

    static string Governing(BatchReportData data, int num)
    {
        var row = data.Rows.First(r => r.Num == num);
        string util = row.Verdict.Util is { } u ? BatchVerdictText.F3(u) : "—";
        return $"№ {num} ({row.Label}): Кисп = {util}";
    }

    /// <summary>
    /// Почему определяющей строки нет: вид расчёта Кисп не даёт (НДС, полная кривизна) —
    /// или ни одна строка не сошлась.
    /// </summary>
    static string NoGoverning(BatchReportData data)
        => data.Rows.Any(r => r.Verdict.Status is BatchRowStatus.Ok or BatchRowStatus.NotPassed)
            ? "не определяется — у строк этого вида расчёта нет Кисп"
            : "нет — ни у одной строки Кисп не получен";

    static void AddTable(ReportDocument document, BatchReportData data)
    {
        var rows = data.Rows;
        if (rows.Count > FullTableLimit)
        {
            var top = rows.Where(r => r.Verdict.Util.HasValue)
                .OrderByDescending(r => r.Verdict.Util!.Value).Take(TopRows).Select(r => r.Num).ToHashSet();
            rows = rows.Where(r => r.Verdict.Status is BatchRowStatus.NotPassed or BatchRowStatus.NotConverged
                                       or BatchRowStatus.Error || top.Contains(r.Num)).ToList();
        }

        var headers = new List<string> { "№", "Метка" };
        headers.AddRange(data.ForceKeys.Select(k => ForceHeaders[k]));
        headers.AddRange(["Статус", "Кисп", "Причина", "Норма"]);
        var tableRows = rows.Select(r =>
        {
            var cells = new List<string>
            {
                r.Num == data.GoverningNum ? $"★ {r.Num}" : r.Num.ToString(),
                r.Label,
            };
            cells.AddRange(data.ForceKeys.Select(k => r.Forces.TryGetValue(k, out var v) ? BatchReportData.F(v) : "—"));
            cells.Add(StatusNames.TryGetValue(r.Verdict.Status, out var s) ? s : r.Verdict.Status);
            cells.Add(r.Verdict.Util is { } u ? BatchVerdictText.F3(u) : "—");
            cells.Add(r.Verdict.Text.Length > 0 ? r.Verdict.Text : "—");
            cells.Add(r.Verdict.Ref.Length > 0 ? r.Verdict.Ref : "—");
            return (IReadOnlyList<string>)cells;
        }).ToList();

        document.Add(new ReportHeading(1, "Строки пакета"));
        if (rows.Count < data.Rows.Count)
            document.Add(new ReportParagraph(
                $"Показано {rows.Count} из {data.Rows.Count} строк: не прошедшие, не сошедшиеся, с ошибкой и {TopRows} с наибольшим Кисп; полная таблица — экспорт CSV в окне результата."));
        document
            .Add(new ReportTable(headers, tableRows))
            .Add(new ReportParagraph("★ — определяющая строка (наибольший Кисп)."));
    }

    static void AddGoverning(ReportDocument document, BatchReportData data, ReportDocument? detail)
    {
        if (data.GoverningNum is not { } num) return;
        document.Add(new ReportPageBreak()).Add(new ReportHeading(1, $"Определяющая строка № {num}"));
        if (detail == null)
        {
            document.Add(new ReportParagraph(
                $"Подробный ход вычислений для этой строки в отчёт не вошёл: создайте задачу по строке № {num} в окне пакета и постройте её отчёт."));
            return;
        }
        document.Add(new ReportParagraph($"Ниже — отчёт одиночной задачи по строке № {num}: «{detail.Title}»."));
        foreach (var block in detail.Blocks)
            document.Add(block is ReportHeading h ? h with { Level = Math.Min(h.Level + 1, 6) } : block);
    }
}
