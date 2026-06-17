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
         ["strain_state"]       = new StrainStateHandler(),
         ["fire_r_check"]       = new FireRCheckHandler(),
         ["fire_r_check_batch"] = new FireRCheckBatchHandler(),
         ["strain_state_batch"] = new StrainStateBatchHandler()
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
