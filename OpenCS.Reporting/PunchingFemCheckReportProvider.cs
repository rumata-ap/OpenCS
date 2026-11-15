using System.Globalization;
using System.Text.Json;

namespace OpenCS.Reporting;

/// <summary>
/// Отчёт по проверке продавливания по МКЭ-схеме (<c>punching_check</c>, спека 2026-11-15, срез 3): идентификация,
/// методика сбора усилий, сводная ведомость узлов, узлы без проверки, вывод, примечания и полный ход вычислений
/// определяющей строки определяющего узла (тело отчёта одиночной задачи).
/// </summary>
public sealed class PunchingFemCheckReportProvider : IReportProvider
{
    /// <summary>Узлов в полной ведомости; больше — только не прошедшие и наибольшие Кисп.</summary>
    public const int FullTableLimit = 400;

    public string TaskKind => "punching_check";

    public IReadOnlyCollection<string> SupportedKinds => [TaskKind];

    public ReportDocument Build(ReportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(context.Result.DataJson) ? "{}" : context.Result.DataJson);
        var r = json.RootElement;
        string tag = string.IsNullOrWhiteSpace(context.Task.Tag) ? "Продавливание" : context.Task.Tag;
        var doc = new ReportDocument($"Проверка плит на продавливание по МКЭ-схеме — {tag}");
        doc.Add(new ReportHeading(1, "Идентификация и единицы"))
           .Add(new ReportKeyValueTable(
           [
               ("Проверка", tag),
               ("Схема МКЭ", S(r, "schema")),
               ("Источник усилий", S(r, "source") is { Length: > 0 } src ? $"расчёт CSfea «{src}»" : "—"),
               ("Вид расчёта", ReportNames.Calc(context.Task.CalcType)),
               ("Результат получен", string.IsNullOrWhiteSpace(context.Result.Created) ? "—" : context.Result.Created),
               ("Статус результата", ReportNames.Status(context.Result.Status)),
               ("Нормативная база", "СП 63.13330.2018, пп. 8.1.46–8.1.52"),
               ("Единицы", "длины — м, силы — кН, моменты — кН·м, напряжения — кПа"),
           ], "Параметр", "Значение"));

        if (S(r, "error") is { Length: > 0 } error)
        {
            doc.Add(new ReportWarning("Проверка не выполнена: " + error));
            return doc;
        }

        doc.Add(new ReportHeading(1, "Методика"))
           .Add(new ReportParagraph("Узел продавливания — стержень колонны (пилона) под углом к горизонтальной плите, конец которого в "
               + "узле плиты или в теле АЖТ. Грузовая площадь — по бетонному сечению стержня в осях узла (ось x — местная ось Y "
               + "стержня, спроецированная на плиту), толщина и привязки арматуры — по сечению пластины, края и отверстия плиты — "
               + "по свободным рёбрам сетки (края — ближе заданного числа h0, отверстия — ближе 6h0)."))
           .Add(new ReportParagraph("Продавливающая сила F принимается за вычетом сил в пределах основания пирамиды продавливания "
               + "(площадь, смещённая на h0 и обрезанная краями плиты; п. 8.1.46). По разрезу — F равна силе, которую основание "
               + "пирамиды передаёт остальной плите, из узловых сил КЭ расчёта (равновесие, без восстановления поперечных сил); по "
               + "колоннам — действие стержней на плиту плюс нагрузка основания. Моменты — неуравновешенный момент колонн на плиту "
               + "Mloc; в продавливание идёт его половина (п. 8.1.46), если не задано иное."))
           .Add(new ReportParagraph("Каждый узел проверяется по всем загружениям и сочетаниям расчёта как отдельная задача со всеми "
               + "вариантами контура; Кисп узла — наибольший. Узел, который нельзя проверить, входит в ведомость с причиной."));

        var joints = r.TryGetProperty("joints", out var js) && js.ValueKind == JsonValueKind.Array ? js.EnumerateArray().ToList() : [];
        AddSummary(doc, r, joints);
        AddTable(doc, joints);
        AddNoCheck(doc, joints);
        AddConclusion(doc, r, joints);
        if (r.TryGetProperty("warnings", out var w) && w.ValueKind == JsonValueKind.Array)
            foreach (var x in w.EnumerateArray())
                if (S(x, "text") is { Length: > 0 } t) doc.Add(new ReportWarning("Примечание: " + t));
        foreach (var j in joints)
            if (j.TryGetProperty("warnings", out var jw) && jw.ValueKind == JsonValueKind.Array)
                foreach (var x in jw.EnumerateArray())
                    if (S(x, "code") is "punching_fem_mismatch" or "punching_fem_cut_failed" or "punching_fem_base_load_unknown" or "fem_beam_in_base"
                        && S(x, "text") is { Length: > 0 } t)
                        doc.Add(new ReportWarning($"Узел {S(j, "node")}: {t}"));

        if (S(r, "governingNode") is { Length: > 0 } node && S(r, "governingDetail") is { Length: > 0 } detailJson)
        {
            var gov = joints.FirstOrDefault(j => S(j, "node") == node);
            doc.Add(new ReportPageBreak())
               .Add(new ReportHeading(1, $"Определяющий узел {node}, «{(gov.ValueKind == JsonValueKind.Object ? S(gov, "governingKey") : "")}» — ход вычислений"));
            var detail = new ReportDocument("");
            PunchingReportProvider.AppendBody(detail, PunchingReportData.Parse(detailJson));
            foreach (var block in detail.Blocks)
                doc.Add(block is ReportHeading h ? h with { Level = Math.Min(h.Level + 1, 6) } : block);
        }
        return doc;
    }

    static void AddSummary(ReportDocument doc, JsonElement r, IReadOnlyList<JsonElement> joints)
    {
        doc.Add(new ReportHeading(1, "Сводка"))
           .Add(new ReportKeyValueTable(
           [
               ("Узлов", I(r, "totalJoints")),
               ("Обеспечено (с заданной арматурой)", I(r, "passedJoints")),
               ("Не обеспечено", I(r, "failedJoints")),
               ("Без проверки", I(r, "noCheckJoints")),
               ("Загружений и сочетаний", I(r, "keys")),
               ("Наибольший Кисп", N(r, "ratio") is double k ? ReportNumberFormatter.FormatUtilization(k) : "—"),
               ("Определяющий узел", S(r, "governingNode") is { Length: > 0 } g ? g : "—"),
               ("Требуется армирование (узлов)", joints.Count(j => S(j, "status") == "reinforcement").ToString(CultureInfo.InvariantCulture)),
           ], "Показатель", "Значение"));
    }

    static void AddTable(ReportDocument doc, IReadOnlyList<JsonElement> all)
    {
        var joints = all.Where(j => S(j, "status") != "no_check").OrderByDescending(j => N(j, "ratio") ?? 0).ToList();
        if (joints.Count == 0) return;
        bool cut = joints.Count > FullTableLimit;
        if (cut) joints = joints.Where(j => !B(j, "passed")).Concat(joints.Where(j => B(j, "passed")).Take(50)).ToList();
        var rows = joints.Select(j =>
        {
            var gov = Rows(j).FirstOrDefault(x => S(x, "key") == S(j, "governingKey"));
            bool has = gov.ValueKind == JsonValueKind.Object;
            return (IReadOnlyList<string>)
            [
                S(j, "node"), S(j, "section"),
                $"{S(j, "slabSection")}; h0 = {PunchingReportProvider.Sig(N(j, "h0") ?? 0)} м",
                Position(j), S(j, "governingKey"),
                has ? PunchingReportProvider.Sig(N(gov, "f") ?? 0) : "—",
                has ? PunchingReportProvider.Sig(N(gov, "mx") ?? 0) : "—",
                has ? PunchingReportProvider.Sig(N(gov, "my") ?? 0) : "—",
                has ? (S(gov, "method") == "contour" ? "разрез" : "колонны") : "—",
                N(j, "ratio") is double k ? ReportNumberFormatter.FormatUtilization(k) : "—",
                Status(S(j, "status"), B(j, "passed")),
            ];
        }).ToList();
        doc.Add(new ReportHeading(1, "Ведомость узлов"))
           .Add(new ReportTable(["Узел", "Колонна", "Плита", "Положение", "Определяющее", "F, кН", "Mx, кН·м", "My, кН·м", "Способ", "Кисп", "Статус"], rows));
        doc.Add(new ReportParagraph("F — продавливающая сила за вычетом нагрузки основания пирамиды; Mx, My — полные моменты Mloc в осях узла. "
            + "Статус «требуется армирование» — бетона недостаточно, с предельной поперечной арматурой (2Fb, 2Mb) прочность обеспечивается."
            + (cut ? $" Узлов больше {FullTableLimit}: показаны все не прошедшие и 50 наиболее нагруженных из прошедших." : "")));
    }

    static void AddNoCheck(ReportDocument doc, IReadOnlyList<JsonElement> joints)
    {
        var list = joints.Where(j => S(j, "status") == "no_check").ToList();
        if (list.Count == 0) return;
        doc.Add(new ReportHeading(1, "Узлы без проверки"))
           .Add(new ReportTable(["Узел", "Колонна", "Причина"],
               list.Select(j => (IReadOnlyList<string>)[S(j, "node"), S(j, "section"), S(j, "noCheckText")]).ToList()));
    }

    static void AddConclusion(ReportDocument doc, JsonElement r, IReadOnlyList<JsonElement> joints)
    {
        doc.Add(new ReportHeading(1, "Вывод"));
        int failed = (int)(N(r, "failedJoints") ?? 0), noCheck = (int)(N(r, "noCheckJoints") ?? 0);
        if (joints.Count == 0)
            doc.Add(new ReportWarning("Узлов «колонна — плита» в схеме не найдено."));
        else if (failed == 0 && noCheck == 0)
            doc.Add(new ReportParagraph($"Прочность плит на продавливание обеспечена во всех {joints.Count} узлах "
                + $"(наибольший Кисп = {ReportNumberFormatter.FormatUtilization(N(r, "ratio") ?? 0)})."));
        else
        {
            if (failed > 0)
                doc.Add(new ReportWarning($"Прочность на продавливание не обеспечена в узлах: {string.Join(", ", joints.Where(j => S(j, "status") != "no_check" && !B(j, "passed")).Select(j => S(j, "node")))}."));
            if (noCheck > 0)
                doc.Add(new ReportWarning($"Не проверено узлов: {noCheck} — см. раздел «Узлы без проверки»."));
        }
    }

    static IEnumerable<JsonElement> Rows(JsonElement j) =>
        j.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array ? rows.EnumerateArray() : [];

    static string Position(JsonElement j)
    {
        int edges = (int)(N(j, "edges") ?? 0), holes = (int)(N(j, "holes") ?? 0);
        string p = edges switch { 0 => "внутри", 1 => "у края", _ => "в углу" };
        return holes > 0 ? $"{p}, отверстий {holes}" : p;
    }

    static string Status(string status, bool passed) => status switch
    {
        "concrete" => "обеспечено по бетону",
        "reinforcement" => passed ? "обеспечено с арматурой" : "требуется армирование",
        "not_ensured" => "не обеспечено",
        _ => "нет проверки",
    };

    static string S(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static double? N(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    static bool B(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
    static string I(JsonElement e, string n) => N(e, n) is double d ? d.ToString("0", CultureInfo.InvariantCulture) : "—";
}
