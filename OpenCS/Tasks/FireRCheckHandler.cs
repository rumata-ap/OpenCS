using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;
using CScore.Fire;
using CScore.Fire.Entities;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Параметры огневой R-проверки в <see cref="CalcTask.ParamsJson"/>.</summary>
public sealed class FireRCheckParams
{
    [JsonPropertyName("fire_section_id")]
    public int FireSectionId { get; set; }

    [JsonPropertyName("thermal_result_id")]
    public int ThermalResultId { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = "fiber";

    [JsonPropertyName("snapshot_index")]
    public int SnapshotIndex { get; set; } = -1;

    public static FireRCheckParams Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new FireRCheckParams();
        return JsonSerializer.Deserialize<FireRCheckParams>(json) ?? new FireRCheckParams();
    }
}

/// <summary>Обработчик задачи R-проверки огнестойкости для одной строки усилий.</summary>
public sealed class FireRCheckHandler : ITaskHandler
{
    public string Kind => "fire_r_check";

    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item, CalcSettings settings, TaskRunContext? ctx = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            if (ctx?.Database is null)
                throw new InvalidOperationException("Для fire_r_check требуется контекст с DatabaseService.");

            var p = FireRCheckParams.Parse(task.ParamsJson);
            if (p.FireSectionId <= 0)
                throw new InvalidOperationException("Не задан fire_section_id в params_json.");

            FireSectionDef? fireDef = ctx.FireSections?.FirstOrDefault(f => f.Id == p.FireSectionId);
            if (fireDef is null)
                throw new InvalidOperationException($"Огневое сечение id={p.FireSectionId} не найдено.");

            var reference = FireThermalReference.Resolve(ctx.Database, p.FireSectionId, p.ThermalResultId);
            if (reference.ErrorKey is not null)
               throw new InvalidOperationException(Loc.S(reference.ErrorKey));

            FireThermalResult thermal = ctx.Database.LoadFireThermalResult(reference.ResultId);
            int? thermalId = reference.ResultId;

            section.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin, pool: ctx?.Database?.Diagrams,
               rebarDifferentialDiagram: settings.RebarDifferentialDiagram, ekbEtaMin: settings.EkbDescEtaMin);
            FireCheckResult check = FireRCheck.Run(
                thermal,
                section,
                item.N,
                item.Mx,
                item.My,
                task.CalcType,
                p.Method,
                p.SnapshotIndex,
                fireDef,
                thermalId,
                settings.Sp63DescEtaMin,
                settings.RebarDifferentialDiagram,
                ctx?.Database?.Diagrams, settings.EkbDescEtaMin);

            var data = new
            {
                criterion = check.Criterion,
                passed = check.Passed,
                margin = Math.Round(check.Margin, 6),
                critical_time_min = check.CriticalTimeMin,
                thermal_result_id = reference.ResultId,
                legacy_thermal_reference = reference.IsLegacyFallback,
                details = check.Details
            };

            return new CalcResult
            {
                TaskId = task.Id,
                TaskKind = task.Kind,
                TaskTag = task.Tag,
                Created = created,
                Status = check.Passed ? "ok" : "not_passed",
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
