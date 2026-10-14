using CScore;

namespace OpenCS.Tasks;

/// <summary>Разрешение усилия этапа двухстадийной задачи в список строк нагрузки.</summary>
internal static class TwoStageForceResolver
{
   /// <summary>
   /// Возвращает строки нагрузки для этапа: одну для "manual"/"item",
   /// все строки набора — для "set". Бросает при отсутствии набора/строки.
   /// </summary>
   internal static List<LoadItem> Resolve(StageForce stage, IEnumerable<ForceSet> forceSets)
   {
      switch (stage.Mode)
      {
         case "manual":
            return [new LoadItem { N = stage.N, Mx = stage.Mx, My = stage.My, Label = "—" }];

         case "set":
         {
            var set = forceSets.FirstOrDefault(f => f.Id == stage.ForceSetId)
               ?? throw new InvalidOperationException($"Набор усилий id={stage.ForceSetId} не найден.");
            if (set.Items.Count == 0)
               throw new InvalidOperationException($"Набор усилий «{set.Tag}» пуст.");
            return [.. set.Items];
         }

         case "item":
         default:
         {
            var set = forceSets.FirstOrDefault(f => f.Id == stage.ForceSetId)
               ?? throw new InvalidOperationException($"Набор усилий id={stage.ForceSetId} не найден.");
            var fi = set.Items.FirstOrDefault(i => i.Id == stage.ForceItemId)
               ?? throw new InvalidOperationException($"Строка усилий id={stage.ForceItemId} не найдена.");
            return [fi];
         }
      }
   }
}
