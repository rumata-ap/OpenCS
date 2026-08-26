using System.Text.Json;
using CScore;
using CScore.Fire;
using CScore.Fire.Entities;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Обработчик задачи собственного предела огнестойкости по п. 8.5 СП 468.
/// Усилия — одна комбинация, одна и та же на всех снимках температуры.
/// </summary>
public sealed class FireRTimeHandler : ITaskHandler
{
   public string Kind => "fire_r_time";

   public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
      CalcSettings settings, TaskRunContext? ctx = null)
   {
      var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
      try
      {
         if (ctx?.Database is null)
            throw new InvalidOperationException("Для fire_r_time требуется контекст с DatabaseService.");

         var p = FireTaskParamsBuilder.Parse(Kind, task.ParamsJson);
         if (p.FireSectionId <= 0)
            throw new InvalidOperationException("Не задан fire_section_id в params_json.");

         FireSectionDef? fireDef = ctx.FireSections?.FirstOrDefault(f => f.Id == p.FireSectionId);
         if (fireDef is null)
            throw new InvalidOperationException($"Огневое сечение id={p.FireSectionId} не найдено.");

         var reference = FireThermalReference.Resolve(ctx.Database, p.FireSectionId, p.ThermalResultId);
         if (reference.ErrorKey is not null)
            throw new InvalidOperationException(Loc.S(reference.ErrorKey));

         FireThermalResult thermal = ctx.Database.LoadFireThermalResult(reference.ResultId);

         FireRTimeResult r = FireRTime.Run(
            thermal, section, item.N, item.Mx, item.My, task.CalcType,
            refine: true,
            settings.Sp63DescEtaMin, settings.RebarDifferentialDiagram,
            ctx.Database.Diagrams, settings.EkbDescEtaMin);

         var data = new
         {
            criterion = "R",
            norm_edition = "SP468-2019/izm1",
            thermal_result_id = reference.ResultId,
            legacy_thermal_reference = reference.IsLegacyFallback,
            fire_section_id = fireDef.Id,
            fire_section_name = fireDef.Tag,
            fire_curve = fireDef.FireCurve,
            r_min = r.RMin,
            r_min_lower_bound = r.RMinLowerBound,
            limit_not_reached = r.LimitNotReached,
            failed_at_start = r.FailedAtStart,
            non_monotone = r.NonMonotone,
            unreliable_snapshots = r.UnreliableSnapshots,
            refinement = r.Refinement,
            refinement_bracket_min = r.BracketMin,
            refinement_bracket_max = r.BracketMax,
            N_target = item.N,
            Mx_target = item.Mx,
            My_target = item.My,
            rows = r.Rows.Select(x => new
            {
               snapshot_index = x.SnapshotIndex,
               time_min = x.TimeMin,
               factor = double.IsFinite(x.Factor) ? x.Factor : (double?)null,
               governing = x.Governing,
               converged = x.Converged
            })
         };

         // Предел не достигнут — это отсутствие данных за пределами
         // длительности теплового расчёта, поэтому статус partial.
         string status = r.FailedAtStart ? "not_passed"
                       : r.HasConsecutiveUnreliableSnapshots ? "partial"
                       : r.LimitNotReached ? "partial"
                       : r.RMin is null ? "error"
                       : "ok";

         return new CalcResult
         {
            TaskId = task.Id,
            TaskKind = task.Kind,
            TaskTag = task.Tag,
            Created = created,
            Status = status,
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
