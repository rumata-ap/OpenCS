using System.Text.Json;
using CScore;
using CScore.Fire;
using CScore.Fire.Entities;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>R-проверка по всем строкам набора усилий задачи.</summary>
public sealed class FireRCheckBatchHandler : ITaskHandler
{
    public string Kind => "fire_r_check_batch";

    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item, CalcSettings settings, TaskRunContext? ctx = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            if (ctx?.Database is null)
                throw new InvalidOperationException("Для fire_r_check_batch требуется контекст с DatabaseService.");

            var p = FireRCheckParams.Parse(task.ParamsJson);
            if (p.FireSectionId <= 0)
                throw new InvalidOperationException("Не задан fire_section_id в params_json.");

            FireSectionDef? fireDef = ctx.FireSections?.FirstOrDefault(f => f.Id == p.FireSectionId);
            if (fireDef is null)
                throw new InvalidOperationException($"Огневое сечение id={p.FireSectionId} не найдено.");

            var forceSet = ctx.Database.ForceSets.FirstOrDefault(fs => fs.Id == task.ForceSetId);
            if (forceSet is null)
                throw new InvalidOperationException("Набор усилий задачи не найден.");

            FireThermalResult thermal = p.ThermalResultId > 0
                ? ctx.Database.LoadFireThermalResult(p.ThermalResultId)
                : ctx.Database.LoadLatestFireThermalResult(p.FireSectionId)
                  ?? throw new InvalidOperationException(
                      "Тепловой результат не найден. Сначала выполните тепловой расчёт.");

            int? thermalId = p.ThermalResultId > 0
                ? p.ThermalResultId
                : ctx.Database.GetLatestFireThermalResultId(p.FireSectionId);

            section.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin, pool: ctx?.Database?.Diagrams,
               rebarDifferentialDiagram: settings.RebarDifferentialDiagram);

            var rows = new List<object>();
            bool allPassed = true;
            double worstMargin = double.PositiveInfinity;

            foreach (var fi in forceSet.Items)
            {
                FireCheckResult check = FireRCheck.Run(
                    thermal,
                    section,
                    fi.N,
                    fi.Mx,
                    fi.My,
                    task.CalcType,
                    p.Method,
                    p.SnapshotIndex,
                    fireDef,
                    thermalId,
                    settings.Sp63DescEtaMin,
                    settings.RebarDifferentialDiagram,
                    ctx?.Database?.Diagrams);

                if (!check.Passed)
                    allPassed = false;
                if (check.Margin < worstMargin)
                    worstMargin = check.Margin;

                rows.Add(new
                {
                    force_item_id = fi.Id,
                    num = fi.Num,
                    label = fi.Label,
                    passed = check.Passed,
                    margin = check.Margin,
                    factor = check.Details.GetValueOrDefault("factor"),
                    governing = check.Details.GetValueOrDefault("governing")
                });
            }

            var data = new
            {
                criterion = "R",
                passed = allPassed,
                worst_margin = worstMargin,
                rows
            };

            return new CalcResult
            {
                TaskId = task.Id,
                TaskKind = task.Kind,
                TaskTag = task.Tag,
                Created = created,
                Status = allPassed ? "ok" : "not_passed",
                DataJson = JsonSerializer.Serialize(data)
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
