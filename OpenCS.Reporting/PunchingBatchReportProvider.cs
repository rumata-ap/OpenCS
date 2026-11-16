using System.Globalization;
using System.Text.Json;
using CScore;
using CScore.Batch;
using CScore.Sp63Punching;

namespace OpenCS.Reporting;

/// <summary>Огибающая варианта контура в сохранённом результате пакета.</summary>
public sealed record PunchingBatchReportVariant(string Id, PunchingVariantKind Kind, PunchingZone Zone, double? Max, int? Num);

/// <summary>Строка пакета продавливания из сохранённого результата.</summary>
public sealed record PunchingBatchReportRow(
    int ForceItemId, int Num, string Label, double N, double Mx, double My, double F,
    string Governing, IReadOnlyDictionary<string, double?> Ratios, string NodeStatus, bool Reversed, BatchRowVerdict Verdict);

/// <summary>Разбор <c>DataJson</c> пакета продавливания — общий для отчёта и окна результата.</summary>
public sealed record PunchingBatchReportData(
    string ForceSetTag, int Total, bool Passed, double? Utilization, int? GoverningNum, string GoverningLabel,
    string GoverningVariant, string Status, double? RatioConcrete, double? RatioMaxSw,
    IReadOnlyList<PunchingBatchReportVariant> Variants, IReadOnlyList<PunchingBatchReportRow> Rows,
    IReadOnlyList<PunchingReportWarning> Warnings, IReadOnlyList<PunchingReportStep> CommonTrace,
    PunchingScene? Scene, JsonElement? Inputs, string? GoverningDetailJson, string? Error,
    PunchingReportDesign? Design = null, int? DesignNum = null, string DesignLabel = "", int DesignRows = 0)
{
    /// <summary>Число краёв плиты (для названий вариантов).</summary>
    public int EdgeCount => Scene?.Edges.Count ?? 2;

    /// <summary>Строки с указанным статусом причины.</summary>
    public int Count(string status) => Rows.Count(r => r.Verdict.Status == status);

    /// <summary>Разбирает JSON; при ошибке пакета заполняет только <see cref="Error"/>.</summary>
    public static PunchingBatchReportData Parse(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var r = doc.RootElement;
        if (r.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
            return new("", 0, false, null, null, "", "", "error", null, null, [], [], [], [], null, null, null, err.GetString());

        var variants = new List<PunchingBatchReportVariant>();
        if (r.TryGetProperty("variants", out var vs) && vs.ValueKind == JsonValueKind.Array)
            foreach (var v in vs.EnumerateArray())
            {
                double? max = Num(v, "envelope");
                if (Bool(v, "envelope_inf")) max = double.PositiveInfinity;
                variants.Add(new(Str(v, "id"),
                    Enum.TryParse<PunchingVariantKind>(Str(v, "kind"), out var k) ? k : PunchingVariantKind.Custom,
                    Enum.TryParse<PunchingZone>(Str(v, "zone"), out var z) ? z : PunchingZone.Main,
                    max, Int(v, "envelope_num")));
            }

        var rows = new List<PunchingBatchReportRow>();
        if (r.TryGetProperty("rows", out var rs) && rs.ValueKind == JsonValueKind.Array)
            foreach (var x in rs.EnumerateArray())
            {
                var ratios = new Dictionary<string, double?>(StringComparer.Ordinal);
                if (x.TryGetProperty("ratios", out var rt) && rt.ValueKind == JsonValueKind.Object)
                    foreach (var p in rt.EnumerateObject())
                        ratios[p.Name] = p.Value.ValueKind == JsonValueKind.Number ? p.Value.GetDouble() : double.PositiveInfinity;
                rows.Add(new(Int(x, "force_item_id") ?? 0, Int(x, "num") ?? rows.Count + 1, Str(x, "label"),
                    Num(x, "N") ?? 0, Num(x, "Mx") ?? 0, Num(x, "My") ?? 0, Num(x, "F") ?? 0,
                    Str(x, "governing"), ratios, Str(x, "node_status"), Bool(x, "reversed"),
                    BatchRowVerdict.TryRead(x) ?? BatchRowVerdict.Error("строка результата не прочитана")));
            }

        var warnings = new List<PunchingReportWarning>();
        if (r.TryGetProperty("warnings", out var w) && w.ValueKind == JsonValueKind.Array)
            foreach (var x in w.EnumerateArray())
                warnings.Add(new(Str(x, "code"), Str(x, "text"),
                    x.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array
                        ? a.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.ToString()).ToList()
                        : []));

        double? util = Num(r, "utilization");
        if (Bool(r, "utilization_inf")) util = double.PositiveInfinity;
        var common = r.TryGetProperty("traceCommon", out var tc) && tc.ValueKind == JsonValueKind.Array
            ? PunchingReportData.Parse($$"""{"traceCommon":{{tc.GetRawText()}}}""").CommonTrace
            : [];
        return new(Str(r, "forceSetTag"), Int(r, "total") ?? rows.Count, Bool(r, "passed"), util, Int(r, "governing_num"),
            Str(r, "governing_label"), Str(r, "governing_variant"), Str(r, "status"), Num(r, "ratio_concrete"), Num(r, "ratio_max_sw"),
            variants, rows, warnings, common,
            r.TryGetProperty("scene", out var s) && s.ValueKind == JsonValueKind.Object ? PunchingSceneJson.Read(s) : null,
            r.TryGetProperty("inputs", out var inp) && inp.ValueKind == JsonValueKind.Object ? inp.Clone() : null,
            r.TryGetProperty("governing_detail", out var gd) && gd.ValueKind == JsonValueKind.Object ? gd.GetRawText() : null,
            null,
            r.TryGetProperty("design", out var dz) && dz.ValueKind == JsonValueKind.Object
                ? PunchingReportData.Parse($$"""{"design":{{dz.GetRawText()}}}""").Design : null,
            Int(r, "design_num"), Str(r, "design_label"), Int(r, "design_rows") ?? 0);
    }

    static string Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static double? Num(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    static int? Int(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
    static bool Bool(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
}

/// <summary>
/// Отчёт по пакету продавливания (образец — strength-web <c>strength/punching/report.py</c>): исходные данные,
/// общие величины, схема по огибающей, огибающая по контурам, таблица строк, вывод, примечания и полный ход
/// вычислений определяющей строки (тело отчёта одиночной задачи).
/// </summary>
public sealed class PunchingBatchReportProvider : IReportProvider
{
    /// <summary>Строк в полной таблице; больше — только не прошедшие и наибольшие Кисп.</summary>
    public const int FullTableLimit = 500;

    /// <summary>Сколько строк с наибольшим Кисп оставлять в сокращённой таблице.</summary>
    public const int TopRows = 20;

    /// <inheritdoc/>
    public string TaskKind => "punching_batch";

    /// <inheritdoc/>
    public IReadOnlyCollection<string> SupportedKinds => [TaskKind];

    /// <inheritdoc/>
    public ReportDocument Build(ReportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var data = PunchingBatchReportData.Parse(context.Result.DataJson);
        string tag = string.IsNullOrWhiteSpace(context.Task.Tag) ? $"№ {context.Task.Num}" : context.Task.Tag;
        var doc = new ReportDocument($"Расчёт плиты на продавливание по набору усилий — {tag}");
        bool pylon = data.Variants.Any(v => v.Kind is PunchingVariantKind.PylonBroken or PunchingVariantKind.PylonReduced);
        int edges = data.EdgeCount;

        doc.Add(new ReportHeading(1, "Идентификация и единицы"))
            .Add(new ReportKeyValueTable(
            [
                ("Задача", $"№ {context.Task.Num} — {tag}"),
                ("Вид расчёта", ReportNames.Calc(context.Task.CalcType)),
                ("Результат получен", string.IsNullOrWhiteSpace(context.Result.Created) ? "—" : context.Result.Created),
                ("Статус результата", ReportNames.Status(context.Result.Status)),
                ("Набор усилий", data.Error is null ? $"«{data.ForceSetTag}», строк: {data.Total}" : "—"),
                ("Нормативная база", "СП 63.13330.2018, пп. 8.1.46–8.1.52" + (pylon ? "; контур пилона — СП 430.1325800, п. 6.3.8" : "")
                    + (data.Variants.Any(v => PunchingReportText.IsWall(v.Kind)) ? "; узел стены — СП 430.1325800.2018 (ред. с Изм. № 1), п. 6.3.8" : "")),
                ("Единицы", "длины — м, силы — кН, моменты — кН·м, напряжения — кПа")
            ], "Параметр", "Значение"));

        if (data.Error is not null)
        {
            doc.Add(new ReportWarning("Расчёт не выполнен: " + data.Error));
            return doc;
        }

        PunchingReportProvider.AddInputs(doc, data.Inputs);
        doc.Add(new ReportParagraph("Каждая строка набора проверяется как отдельная задача: продавливающая сила F = |N|, "
            + "моменты Mx и My строки (оси колонны совпадают с осями плиты). Для каждой строки рассматриваются все "
            + "варианты расчётного контура; Кисп строки — наибольший из них (п. 8.1.46)."));
        if (data.CommonTrace.Count > 0)
        {
            doc.Add(new ReportHeading(1, "Общие величины"));
            foreach (var s in data.CommonTrace) doc.Add(PunchingReportProvider.StepBlock(s));
        }
        if (data.Scene is { } scene)
            doc.Add(new ReportHeading(1, "Расчётная схема — огибающая по набору"))
               .Add(new ReportParagraph("Контуры проведены на расстоянии h0/2 от грузовой площади (п. 8.1.46). Цвет контура — "
                   + "его наибольший Кисп по всем строкам набора; сплошная линия — определяющий контур определяющей строки."))
               .Add(new ReportImage("Расчётные контуры продавливания — огибающая", new PunchingPlanSvgRenderer().Render(scene)));

        AddEnvelope(doc, data, edges);
        AddRows(doc, data, edges);
        AddConclusion(doc, data, edges);
        foreach (var w in data.Warnings.Where(w => !string.IsNullOrWhiteSpace(w.Text)))
            doc.Add(new ReportWarning("Примечание: " + Readable(w.Text, data, edges)));

        if (data.GoverningNum is { } num && data.GoverningDetailJson is { } detailJson)
        {
            doc.Add(new ReportPageBreak())
               .Add(new ReportHeading(1, $"Определяющая строка № {num} «{data.GoverningLabel}» — ход вычислений"));
            var detail = new ReportDocument("");
            PunchingReportProvider.AppendBody(detail, PunchingReportData.Parse(detailJson));
            foreach (var block in detail.Blocks)
                doc.Add(block is ReportHeading h ? h with { Level = Math.Min(h.Level + 1, 6) } : block);
        }
        return doc;
    }

    static void AddEnvelope(ReportDocument doc, PunchingBatchReportData data, int edges)
    {
        if (data.Variants.Count == 0) return;
        doc.Add(new ReportHeading(1, "Огибающая по контурам"))
           .Add(new ReportTable(["Контур", "Kmax", "Строка", "F, кН", "Mx, кН·м", "My, кН·м"],
               data.Variants.Select(v =>
               {
                   var row = data.Rows.FirstOrDefault(r => r.Num == v.Num);
                   return (IReadOnlyList<string>)
                   [
                       PunchingReportText.VariantTitle(v.Kind, v.Zone, v.Id, edges) + (v.Id == data.GoverningVariant ? " *" : ""),
                       v.Max is double k ? K(k) : "—",
                       row is null ? "—" : $"№ {row.Num} {row.Label}".TrimEnd(),
                       row is null ? "—" : PunchingReportProvider.Sig(row.F),
                       row is null ? "—" : PunchingReportProvider.Sig(row.Mx),
                       row is null ? "—" : PunchingReportProvider.Sig(row.My)
                   ];
               }).ToList()))
           .Add(new ReportParagraph("Kmax — наибольший Кисп контура по всем строкам набора; * — контур, определяющий в определяющей строке."));
    }

    static void AddRows(ReportDocument doc, PunchingBatchReportData data, int edges)
    {
        var rows = data.Rows;
        if (rows.Count > FullTableLimit)
        {
            var top = rows.Where(r => r.Verdict.Util.HasValue).OrderByDescending(r => r.Verdict.Util!.Value)
                .Take(TopRows).Select(r => r.Num).ToHashSet();
            rows = rows.Where(r => !r.Verdict.IsPassed || top.Contains(r.Num)).ToList();
        }
        var shown = data.Variants.Where(v => v.Kind != PunchingVariantKind.PylonReduced).ToList();
        var headers = new List<string> { "№", "Метка", "N, кН", "Mx, кН·м", "My, кН·м" };
        headers.AddRange(shown.Select(v => "K: " + PunchingReportText.ShortTitle(v.Kind, v.Zone, v.Id, edges)));
        headers.AddRange(["Определяющий контур", "K", "Статус"]);
        var table = rows.Select(r =>
        {
            var cells = new List<string>
            {
                r.Num == data.GoverningNum ? $"★ {r.Num}" : r.Num.ToString(CultureInfo.InvariantCulture),
                r.Label + (r.Reversed ? " (N другого знака)" : ""),
                PunchingReportProvider.Sig(r.N), PunchingReportProvider.Sig(r.Mx), PunchingReportProvider.Sig(r.My)
            };
            cells.AddRange(shown.Select(v => r.Ratios.TryGetValue(v.Id, out var k) && k is double kk ? K(kk) : "—"));
            var gv = data.Variants.FirstOrDefault(v => v.Id == r.Governing);
            cells.Add(gv is null ? "—" : PunchingReportText.ShortTitle(gv.Kind, gv.Zone, gv.Id, edges));
            cells.Add(r.Verdict.Util is double u ? K(u) : "—");
            cells.Add(r.Verdict.Status switch
            {
                BatchRowStatus.Ok => "проходит",
                BatchRowStatus.NotPassed => "не проходит",
                BatchRowStatus.Error => "ошибка",
                _ => r.Verdict.Status
            });
            return (IReadOnlyList<string>)cells;
        }).ToList();

        doc.Add(new ReportHeading(1, "Строки набора"));
        if (rows.Count < data.Rows.Count)
            doc.Add(new ReportParagraph($"Показано {rows.Count} из {data.Rows.Count} строк: не прошедшие и {TopRows} с наибольшим Кисп."));
        doc.Add(new ReportTable(headers, table))
           .Add(new ReportParagraph("★ — определяющая строка (наибольший Кисп). K округлён в бо́льшую сторону."));
    }

    static void AddConclusion(ReportDocument doc, PunchingBatchReportData data, int edges)
    {
        doc.Add(new ReportHeading(1, "Вывод"));
        var gv = data.Variants.FirstOrDefault(v => v.Id == data.GoverningVariant);
        string contour = gv is null ? "" : ", " + PunchingReportText.VariantTitle(gv.Kind, gv.Zone, gv.Id, edges).ToLowerInvariant();
        if (data.Utilization is double k && data.GoverningNum is { } num)
        {
            string ks = K(k);
            string where = $"строка № {num} «{data.GoverningLabel}»{contour}";
            doc.Add(new ReportParagraph(k <= 1
                ? $"Максимальный коэффициент использования Kmax = {ks} ({where}), запас несущей способности "
                  + $"{((1 - k) * 100).ToString("0.0", CultureInfo.GetCultureInfo("ru-RU"))} % — прочность плиты на продавливание обеспечена во всех строках набора."
                : $"Максимальный коэффициент использования Kmax = {ks} > 1 ({where}) — прочность плиты на продавливание НЕ обеспечена."));
        }
        else doc.Add(new ReportParagraph("Несущая способность не определена — прочность плиты на продавливание не обеспечена."));
        doc.Add(new ReportKeyValueTable(
        [
            ("Строк в наборе", data.Rows.Count.ToString(CultureInfo.InvariantCulture)),
            ("Проходят", data.Count(BatchRowStatus.Ok).ToString(CultureInfo.InvariantCulture)),
            ("Не проходят", data.Count(BatchRowStatus.NotPassed).ToString(CultureInfo.InvariantCulture)),
            ("K без поперечной арматуры (наибольший по строкам)", data.RatioConcrete is double kb ? K(kb) : "—"),
            ("K с предельной арматурой (Fult = 2Fb, Mult = 2Mb)", data.RatioMaxSw is double km ? K(km) : "—"),
            ("Оценка узла", PunchingReportText.Status(data.Status))
        ], "Показатель", "Значение"));
        if (data.Design is { } d)
        {
            PunchingReportProvider.AddDesign(doc, d, $"по строке № {data.DesignNum} «{data.DesignLabel}»");
            doc.Add(new ReportParagraph($"Подбор выполнен по каждой из {data.DesignRows} строк, где бетона недостаточно; "
                + "показана строка с наибольшим требуемым армированием, ширина зоны — по ней же."));
        }
    }

    static string Readable(string text, PunchingBatchReportData data, int edges)
    {
        foreach (var v in data.Variants)
            text = text.Replace($"«{v.Id}»", $"«{PunchingReportText.VariantTitle(v.Kind, v.Zone, v.Id, edges)}»", StringComparison.Ordinal);
        return text;
    }

    static string K(double k) => double.IsFinite(k) ? PunchingReportProvider.Ku(k) : "∞";
}
