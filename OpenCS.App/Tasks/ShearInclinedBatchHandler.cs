using System.Text.Json;
using CScore;
using CScore.Batch;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Пакетный расчёт наклонных сечений по всем строкам набора усилий.</summary>
public class ShearInclinedBatchHandler : ITaskHandler
{
    /// <summary>Идентификатор вида задачи.</summary>
    public string Kind => "shear_inclined_batch";

    /// <summary>Выполняет расчёт по каждой строке набора и собирает сводку.</summary>
    public CalcResult Run(
        CalcTask task, CrossSection section, LoadItem item,
        CalcSettings settings, TaskRunContext? ctx = null)
    {
        string created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            var forceSet = ctx?.Database?.ForceSets.FirstOrDefault(fs => fs.Id == task.ForceSetId)
                ?? throw new InvalidOperationException(
                    "Для пакетного расчёта наклонных сечений требуется контекст с набором усилий.");

            // Строки независимы (расчёт сечение только читает) — при включённой настройке параллельно;
            // сводка и предупреждения собираются после, в порядке строк.
            var items = forceSet.Items.OrderBy(i => i.Num).ToList();
            var outcomes = new RowOutcome[items.Count];
            BatchProgress.ForEachRow(items.Count, settings, ctx, i =>
                outcomes[i] = Evaluate(task, section, items[i], settings, ctx));

            var rows = new List<object>(items.Count);
            var warnings = new HashSet<string>();
            double worst = 0.0;
            bool zeroCapacity = false;
            bool notApplicable = false;
            foreach (var o in outcomes)
            {
                rows.Add(o.Row);
                foreach (var w in o.Warnings) warnings.Add(w);
                if (o.Kind == RowKind.NotApplicable) notApplicable = true;
                else if (o.Kind == RowKind.Ok)
                {
                    if (o.Utilization is double value) worst = Math.Max(worst, value);
                    else zeroCapacity = true;
                }
            }

            return new CalcResult
            {
                TaskId = task.Id,
                TaskKind = task.Kind,
                TaskTag = task.Tag,
                Created = created,
                Status = BatchVerdictSummary.PackageStatus(outcomes.Select(o => o.Verdict)),
                DataJson = JsonSerializer.Serialize(new
                {
                    sectionTag = section.Tag,
                    forceSetTag = forceSet.Tag,
                    utilization = notApplicable || zeroCapacity ? (double?)null : worst,
                    // Нормативный итог пакета недоступен, если хотя бы одна строка вышла
                    // за область применимости. При этом строки не теряются из сводки.
                    utilizationStatus = notApplicable ? "not_applicable"
                        : zeroCapacity ? "no_capacity" : "ok",
                    verdict_summary = BatchVerdictSummary.ToJson(outcomes.Select(o => o.Verdict).ToList(), items.Select(i => i.Num).ToList()),
                    rows,
                    warnings = warnings.ToList()
                })
            };
        }
        catch (Exception ex)
        {
            return new CalcResult
            {
                TaskId = task.Id,
                TaskKind = task.Kind,
                TaskTag = task.Tag,
                Created = created,
                Status = "error",
                DataJson = JsonSerializer.Serialize(new { error = ex.Message })
            };
        }
    }

    enum RowKind { Error, NotApplicable, Ok }

    sealed record RowOutcome(object Row, IReadOnlyList<string> Warnings, RowKind Kind, double? Utilization, BatchRowVerdict Verdict);

    /// <summary>
    /// Причина строки: error — ошибка расчёта, not_applicable — вне области применимости, иначе Кисп худшей
    /// проверки (null — нулевая несущая способность, то есть +∞).
    /// </summary>
    internal static BatchRowVerdict RowVerdict(string status, double? utilization, string worstFormula,
        string worstDescription, string error)
    {
        if (status == "error") return BatchRowVerdict.Error(error);
        if (status == "not_applicable") return BatchRowVerdict.NotApplicable("shear", "вне области применимости метода");
        double u = utilization ?? double.PositiveInfinity;
        string text = worstDescription.Length > 0 ? $"{worstDescription}: Кисп = {BatchVerdictText.F3(u)}" : $"Кисп = {BatchVerdictText.F3(u)}";
        return BatchRowVerdict.Worst(new BatchCriterion("shear", u, worstFormula, text));
    }

    static RowOutcome Evaluate(CalcTask task, CrossSection section, LoadItem row, CalcSettings settings, TaskRunContext? ctx)
    {
        var single = ShearInclinedRunner.Run(task, section, row, settings, ctx, includeTrace: false);

        using var doc = JsonDocument.Parse(single.DataJson);
        var root = doc.RootElement;

        if (single.Status != "ok")
        {
            string error = root.TryGetProperty("error", out var err) ? err.GetString() ?? "" : "";
            var ve = RowVerdict("error", null, "", "", error);
            return new RowOutcome(new
            {
                num = row.Num,
                label = row.Label,
                vy = row.Vy,
                vx = row.Vx,
                utilization = (double?)null,
                status = "error",
                worstFormula = "",
                error,
                verdict = ve.ToJson()
            }, [], RowKind.Error, null, ve);
        }

        var warnings = root.GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetString()).OfType<string>().ToList();

        // null означает нулевую несущую способность — это отказ, а не «нет значения»
        string utilizationStatus = root.TryGetProperty("utilizationStatus", out var statusValue)
            ? statusValue.GetString() ?? "ok" : "ok";
        if (utilizationStatus == "not_applicable")
        {
            var vna = RowVerdict("not_applicable", null, "", "", "");
            return new RowOutcome(new
            {
                num = row.Num,
                label = row.Label,
                vy = row.Vy,
                vx = row.Vx,
                utilization = (double?)null,
                status = "not_applicable",
                worstFormula = "",
                error = "",
                verdict = vna.ToJson()
            }, warnings, RowKind.NotApplicable, null, vna);
        }

        var utilizationValue = root.GetProperty("utilization");
        double? utilization = utilizationValue.ValueKind == JsonValueKind.Number
            ? utilizationValue.GetDouble()
            : null;

        var worstDetail = root.GetProperty("details").EnumerateArray()
            .OrderByDescending(d => d.GetProperty("ratio").ValueKind == JsonValueKind.Number
                ? d.GetProperty("ratio").GetDouble()
                : double.PositiveInfinity)
            .Select(d => (formula: d.GetProperty("formula").GetString() ?? "",
                          description: d.TryGetProperty("description", out var ds) ? ds.GetString() ?? "" : ""))
            .FirstOrDefault();
        string worstFormula = worstDetail.formula ?? "";
        var verdict = RowVerdict("ok", utilization, worstFormula, worstDetail.description ?? "", "");

        return new RowOutcome(new
        {
            num = row.Num,
            label = row.Label,
            vy = row.Vy,
            vx = row.Vx,
            utilization,
            status = utilization is double ok && ok <= 1.0 ? "ok" : "failed",
            worstFormula,
            verdict = verdict.ToJson()
        }, warnings, RowKind.Ok, utilization, verdict);
    }
}
