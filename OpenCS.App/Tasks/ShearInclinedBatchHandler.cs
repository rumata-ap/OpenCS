using System.Text.Json;
using CScore;
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
                Status = "ok",
                DataJson = JsonSerializer.Serialize(new
                {
                    sectionTag = section.Tag,
                    forceSetTag = forceSet.Tag,
                    utilization = notApplicable || zeroCapacity ? (double?)null : worst,
                    // Нормативный итог пакета недоступен, если хотя бы одна строка вышла
                    // за область применимости. При этом строки не теряются из сводки.
                    utilizationStatus = notApplicable ? "not_applicable"
                        : zeroCapacity ? "no_capacity" : "ok",
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

    sealed record RowOutcome(object Row, IReadOnlyList<string> Warnings, RowKind Kind, double? Utilization);

    static RowOutcome Evaluate(CalcTask task, CrossSection section, LoadItem row, CalcSettings settings, TaskRunContext? ctx)
    {
        var single = ShearInclinedRunner.Run(task, section, row, settings, ctx, includeTrace: false);

        using var doc = JsonDocument.Parse(single.DataJson);
        var root = doc.RootElement;

        if (single.Status != "ok")
        {
            return new RowOutcome(new
            {
                num = row.Num,
                label = row.Label,
                vy = row.Vy,
                vx = row.Vx,
                utilization = (double?)null,
                status = "error",
                worstFormula = "",
                error = root.TryGetProperty("error", out var err) ? err.GetString() : ""
            }, [], RowKind.Error, null);
        }

        var warnings = root.GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetString()).OfType<string>().ToList();

        // null означает нулевую несущую способность — это отказ, а не «нет значения»
        string utilizationStatus = root.TryGetProperty("utilizationStatus", out var statusValue)
            ? statusValue.GetString() ?? "ok" : "ok";
        if (utilizationStatus == "not_applicable")
        {
            return new RowOutcome(new
            {
                num = row.Num,
                label = row.Label,
                vy = row.Vy,
                vx = row.Vx,
                utilization = (double?)null,
                status = "not_applicable",
                worstFormula = "",
                error = ""
            }, warnings, RowKind.NotApplicable, null);
        }

        var utilizationValue = root.GetProperty("utilization");
        double? utilization = utilizationValue.ValueKind == JsonValueKind.Number
            ? utilizationValue.GetDouble()
            : null;

        string worstFormula = root.GetProperty("details").EnumerateArray()
            .OrderByDescending(d => d.GetProperty("ratio").ValueKind == JsonValueKind.Number
                ? d.GetProperty("ratio").GetDouble()
                : double.PositiveInfinity)
            .Select(d => d.GetProperty("formula").GetString() ?? "")
            .FirstOrDefault() ?? "";

        return new RowOutcome(new
        {
            num = row.Num,
            label = row.Label,
            vy = row.Vy,
            vx = row.Vx,
            utilization,
            status = utilization is double ok && ok <= 1.0 ? "ok" : "failed",
            worstFormula
        }, warnings, RowKind.Ok, utilization);
    }
}
