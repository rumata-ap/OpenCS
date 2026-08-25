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

            var rows = new List<object>();
            var warnings = new HashSet<string>();
            double worst = 0.0;
            bool zeroCapacity = false;

            foreach (var row in forceSet.Items.OrderBy(i => i.Num))
            {
                ctx!.CancellationToken.ThrowIfCancellationRequested();
                var single = ShearInclinedRunner.Run(task, section, row, settings, ctx);

                using var doc = JsonDocument.Parse(single.DataJson);
                var root = doc.RootElement;

                if (single.Status != "ok")
                {
                    rows.Add(new
                    {
                        num = row.Num,
                        label = row.Label,
                        vy = row.Vy,
                        vx = row.Vx,
                        utilization = (double?)null,
                        status = "error",
                        worstFormula = "",
                        error = root.TryGetProperty("error", out var err) ? err.GetString() : ""
                    });
                    continue;
                }

                // null означает нулевую несущую способность — это отказ, а не «нет значения»
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

                foreach (var warning in root.GetProperty("warnings").EnumerateArray())
                    if (warning.GetString() is { } text) warnings.Add(text);

                if (utilization is double value) worst = Math.Max(worst, value);
                else zeroCapacity = true;

                rows.Add(new
                {
                    num = row.Num,
                    label = row.Label,
                    vy = row.Vy,
                    vx = row.Vx,
                    utilization,
                    status = utilization is double ok && ok <= 1.0 ? "ok" : "failed",
                    worstFormula
                });
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
                    utilization = zeroCapacity ? (double?)null : worst,
                    utilizationStatus = zeroCapacity ? "no_capacity" : "ok",
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
}
