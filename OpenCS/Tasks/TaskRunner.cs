using System.Collections.Generic;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Tasks
{
   /// <summary>
   /// Диспетчер расчётных задач: выбирает обработчик по Kind и запускает расчёт.
   /// </summary>
   public static class TaskRunner
   {
      static readonly Dictionary<string, ITaskHandler> Handlers = new()
      {
         ["strain_state"]         = new StrainStateHandler(),
         ["fire_r_check"]         = new FireRCheckHandler(),
         ["fire_r_check_batch"]   = new FireRCheckBatchHandler(),
         ["strain_state_batch"]   = new StrainStateBatchHandler(),
         ["two_stage_strain"]     = new TwoStageStrainHandler(),
         ["two_stage_strain_batch"] = new TwoStageStrainBatchHandler(),
         ["limit_force"]          = new LimitForceHandler(),
         ["limit_moment"]         = new LimitMomentHandler(),
         ["limit_axial"]          = new LimitAxialHandler(),
         ["limit_force_batch"]    = new LimitForceBatchHandler(),
         ["limit_moment_batch"]   = new LimitMomentBatchHandler(),
         ["limit_axial_batch"]    = new LimitAxialBatchHandler(),
          ["strength_ndm_batch"]   = new StrengthNDMBatchHandler(),
          ["shell_simpl_wa_sls"]    = new ShellSimplWaSlsHandler(),
          ["shell_simpl_wa_uls"]    = new ShellSimplWaUlsHandler(),
          ["shell_simpl_capri_sls"] = new ShellSimplCapriSlsHandler(),
          ["shell_simpl_capri_uls"] = new ShellSimplCapriUlsHandler(),
          ["shell_simpl_wa_sls_batch"]    = new ShellSimplWaSlsBatchHandler(),
          ["shell_simpl_wa_uls_batch"]    = new ShellSimplWaUlsBatchHandler(),
          ["shell_simpl_capri_sls_batch"] = new ShellSimplCapriSlsBatchHandler(),
          ["shell_simpl_capri_uls_batch"] = new ShellSimplCapriUlsBatchHandler(),
          ["shell_strain_state"]          = new ShellStrainHandler(),
          ["shell_strain_state_batch"]    = new ShellStrainBatchHandler(),
          ["prestress_loss"]              = new PrestressLossHandler(),
       };

      /// <summary>Выполняет задачу. Никогда не бросает — ошибки в CalcResult.Status.</summary>
      public static CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
                                   CalcSettings? settings = null, TaskRunContext? ctx = null)
      {
         settings ??= CalcSettings.Default;
         if (!Handlers.TryGetValue(task.Kind, out var handler))
         {
            return new CalcResult
            {
               TaskId   = task.Id,
               TaskKind = task.Kind,
               TaskTag  = task.Tag,
               Created  = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
               Status   = "error",
               DataJson = $"{{\"error\":\"Unknown task kind: {task.Kind}\"}}"
            };
         }
         return handler.Run(task, section, item, settings, ctx);
      }

      /// <summary>Список зарегистрированных видов задач.</summary>
      public static IReadOnlyCollection<string> KindList => Handlers.Keys;
   }
}
